using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Security;

public sealed class FakeAuthUi { public int AuthenticationType { get; set; } }
// An open in-memory session for identity checks only; no command can execute and no network exists.
public sealed class IdentityConnection : DbConnection
{
    public string Catalog = "OriginalDb", Host = "test-server";
    public Guid ClientConnectionId { get; set; } = Guid.NewGuid();
    public bool Connected = true;
    public override string ConnectionString { get; set; }
    public override string Database { get { return Catalog; } }
    public override string DataSource { get { return Host; } }
    public override string ServerVersion { get { return "test"; } }
    public override System.Data.ConnectionState State { get { return Connected ? System.Data.ConnectionState.Open : System.Data.ConnectionState.Closed; } }
    public override void ChangeDatabase(string databaseName) { Catalog = databaseName; }
    public override void Open() { throw new NotSupportedException(); }
    public override void Close() { Connected = false; }
    protected override System.Data.Common.DbTransaction BeginDbTransaction(System.Data.IsolationLevel level) { throw new NotSupportedException(); }
    protected override System.Data.Common.DbCommand CreateDbCommand() { throw new NotSupportedException(); }
}
internal static class AuthenticationTests
{
    static int checks;
    static void Check(bool ok,string what) { checks++; if(!ok)throw new Exception(what); }
    static object Call(MethodInfo method,params object[] args)
    {
        try {return method.Invoke(null,args);}catch(TargetInvocationException ex){throw ex.InnerException??ex;}
    }
    static int Main(string[] args)
    {
        string ide=args[0];string[] dirs={ide,ide+@"\Extensions\Application",ide+@"\CommonExtensions\Microsoft\Editor",ide+@"\PublicAssemblies",ide+@"\PrivateAssemblies"};
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(var dir in dirs){string p=Path.Combine(dir,new AssemblyName(e.Name).Name+".dll");if(File.Exists(p))return Assembly.LoadFrom(p);}return null;};
        try
        {
            var plugin=Assembly.LoadFrom(Path.GetFullPath(args[1]));var adapter=plugin.GetType("JoinAssist.SsmsConnection",true);
            var cloneMethod=adapter.GetMethod("CloneEditorConnection",BindingFlags.Static|BindingFlags.NonPublic);
            var getLive=adapter.GetMethod("GetEditorConnection",BindingFlags.Static|BindingFlags.NonPublic);
            var configure=adapter.GetMethod("ConfigureConnection",BindingFlags.Static|BindingFlags.NonPublic);
            var bind=adapter.GetMethod("Bind",BindingFlags.Static|BindingFlags.NonPublic);
            var isCurrent=adapter.GetMethod("IsStillActive");
            var invalidation=adapter.GetMethod("InvalidationReason");
            var editors=Assembly.LoadFrom(ide+@"\Extensions\Application\SQLEditors.dll");
            var sqlEditor=editors.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.SqlScriptEditorControl",true);
            var baseEditor=editors.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptAndResultsEditorControl",true);
            var field=baseEditor.GetField("m_connection",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            Check(field!=null && field.IsFamily && typeof(System.Data.IDbConnection).IsAssignableFrom(field.FieldType),"Contratto reale SSMS: connessione protetta della classe base verificata.");
            Check(sqlEditor.GetField("m_connection",BindingFlags.Instance|BindingFlags.NonPublic).DeclaringType==baseEditor,"Contratto SSMS: connessione dichiarata nella classe base SQL.");
            // No window or SSMS service is created. Only the actual field layout is exercised.
            var editor=FormatterServices.GetUninitializedObject(sqlEditor);GC.SuppressFinalize(editor);
            var uiType=Assembly.LoadFrom(ide+@"\Microsoft.SqlServer.RegSvrEnum.dll").GetType("Microsoft.SqlServer.Management.Smo.RegSvrEnum.UIConnectionInfo",true);
            var syntheticUi=Activator.CreateInstance(uiType);
            var copyOne=uiType.GetMethod("Copy").Invoke(syntheticUi,null);
            var copyTwo=uiType.GetMethod("Copy").Invoke(syntheticUi,null);
            Check(!object.Equals(uiType.GetProperty("Id").GetValue(copyOne,null),uiType.GetProperty("Id").GetValue(copyTwo,null)),"Riproduzione bug 0.3.0: UIConnectionInfo.Copy genera ID diversi anche con stessa connessione.");
            using(var source=new IdentityConnection())
            using(var metadata=new IdentityConnection())
            {
                field.SetValue(editor,source);
                var capture=Call(bind,editor,source,metadata,"OriginalDb");
                Func<bool> current=()=> (bool)isCurrent.Invoke(capture,null);
                Func<string> reason=()=> (string)invalidation.Invoke(capture,null);
                Check(current(),"Sessione invariata valida anche senza finestra globale attiva o servizi SSMS.");
                uiType.GetMethod("Copy").Invoke(syntheticUi,null);
                Check(current(),"Una nuova copia delle informazioni UI non invalida la sessione.");
                using(var replacement=new IdentityConnection())
                {
                    field.SetValue(editor,replacement);
                    Check(!current() && reason().Contains("JA-CONTEXT-02"),"Cambio del client della scheda rilevato anche con stesso server/database.");
                }
                field.SetValue(editor,source);
                source.Connected=false;
                Check(!current() && reason().Contains("JA-CONTEXT-03"),"Scheda scollegata rilevata.");
                source.Connected=true;var sessionId=source.ClientConnectionId;source.ClientConnectionId=Guid.NewGuid();
                Check(!current() && reason().Contains("JA-CONTEXT-04"),"Riapertura della stessa istanza client rilevata tramite ID di sessione SQL.");
                source.ClientConnectionId=sessionId;source.Catalog="OtherDb";
                Check(!current() && reason().Contains("JA-CONTEXT-05"),"Cambio database rilevato.");
                source.Catalog="OriginalDb";source.Host="other-server";
                Check(!current() && reason().Contains("JA-CONTEXT-05"),"Cambio server rilevato.");
                source.Host="test-server";
                Check(current(),"Focus e copie UI ignorati, contesto originale valido.");
                ((IDisposable)capture).Dispose();
                Check(current() && source.Connected,"Chiudere i metadati non invalida o chiude la sessione originale.");
            }
            field.SetValue(editor,null);
            var client=Assembly.LoadFrom(ide+@"\Microsoft.Data.SqlClient.dll").GetType("Microsoft.Data.SqlClient.SqlConnection",true);
            Func<string,DbConnection> fresh=cs=>(DbConnection)Activator.CreateInstance(client,new object[]{cs});
            const string connectionString="Server=joinassist-test.database.windows.net;Database=OriginalDb;Encrypt=True;TrustServerCertificate=False;";
            var entra=new FakeAuthUi{AuthenticationType=3};
            using(var original=fresh(connectionString))
            {
                client.GetProperty("AccessToken").SetValue(original,"JoinAssist-synthetic-token",null);
                field.SetValue(editor,original);
                Check(ReferenceEquals(Call(getLive,editor),original),"Percorso reale SqlScriptEditorControl -> campo della base -> client effettivo.");
                using(var clone=(DbConnection)Call(cloneMethod,Call(getLive,editor),entra))
                {
                    Check(!ReferenceEquals(clone,original) && clone.State==System.Data.ConnectionState.Closed,"Clone indipendente e chiuso.");
                    Check((string)client.GetProperty("AccessToken").GetValue(clone,null)=="JoinAssist-synthetic-token","Token della connessione effettiva copiato senza UIConnectionInfo.RenewableToken.");
                    Call(configure,clone,"MetadataDb");
                    Check((string)client.GetProperty("AccessToken").GetValue(clone,null)=="JoinAssist-synthetic-token","Token mantenuto dopo cambio database e applicazione.");
                    Check(clone.Database=="MetadataDb" && original.Database=="OriginalDb","Cambio database confinato alla copia.");
                    Check(!clone.ConnectionString.Contains("JoinAssist-synthetic-token"),"Token assente dalla stringa di connessione.");
                    var builder=new DbConnectionStringBuilder{ConnectionString=clone.ConnectionString};
                    Check(!Convert.ToBoolean(builder["TrustServerCertificate"]),"Verifica del certificato originale preservata.");
                }
                Check(original.State==System.Data.ConnectionState.Closed && original.Database=="OriginalDb","Disporre la copia non altera la connessione originale.");
                using(var clone=(DbConnection)Call(cloneMethod,original,entra))
                    Check(clone.Database=="OriginalDb","Richiesta successiva crea un altro clone dalla connessione originale.");
            }
            field.SetValue(editor,null);
            Check(Call(getLive,editor)==null && Call(getLive,new object())==null,"Connessione assente o contratto non riconosciuto: nessuna ricerca in altri oggetti.");
            using(var original=fresh(connectionString))
            {
                var property=client.GetProperty("AccessTokenCallback");var signature=property.PropertyType.GetMethod("Invoke");
                var dynamic=new DynamicMethod("NeverInvokedSyntheticCallback",signature.ReturnType,signature.GetParameters().Select(p=>p.ParameterType).ToArray());
                var il=dynamic.GetILGenerator();il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Ret);
                var callback=dynamic.CreateDelegate(property.PropertyType);property.SetValue(original,callback,null);
                using(var clone=(DbConnection)Call(cloneMethod,original,entra))
                {
                    Call(configure,clone,"MetadataDb");
                    Check(object.Equals(property.GetValue(clone,null),callback),"Callback di autenticazione preservato nel clone.");
                }
            }
            using(var original=fresh(connectionString+"Authentication=Active Directory Interactive;User ID=test@example.invalid;"))
            using(var clone=(DbConnection)Call(cloneMethod,original,entra))
            {
                Call(configure,clone,"MetadataDb");
                var originalBuilder=new DbConnectionStringBuilder{ConnectionString=original.ConnectionString};
                var cloneBuilder=new DbConnectionStringBuilder{ConnectionString=clone.ConnectionString};
                Check(object.Equals(originalBuilder["Authentication"],cloneBuilder["Authentication"]),"Modalità Entra nativa preservata anche senza token/callback esplicito.");
            }
            using(var original=fresh(connectionString+"Integrated Security=True;"))
            {
                bool refused=false;try{Call(cloneMethod,original,entra);}catch(InvalidOperationException ex){refused=ex.Message.Contains("JA-CONN-04");}
                Check(refused,"Descrizione Entra con client Windows: rifiuto senza tentativi di login.");
                using(var clone=(DbConnection)Call(cloneMethod,original,new FakeAuthUi{AuthenticationType=0}))
                    Check(clone.ConnectionString.Contains("Integrated Security=True"),"Connessione Windows autentica preservata.");
            }
            using(var password=new SecureString())
            using(var original=fresh(connectionString))
            {
                foreach(char c in "synthetic-password")password.AppendChar(c);password.MakeReadOnly();
                var credentialType=client.Assembly.GetType("Microsoft.Data.SqlClient.SqlCredential",true);
                var credential=Activator.CreateInstance(credentialType,new object[]{"test-user",password});
                client.GetProperty("Credential").SetValue(original,credential,null);
                using(var clone=(DbConnection)Call(cloneMethod,original,new FakeAuthUi{AuthenticationType=1}))
                {
                    Call(configure,clone,"MetadataDb");
                    var copied=client.GetProperty("Credential").GetValue(clone,null); Check(copied!=null && (string)credentialType.GetProperty("UserId").GetValue(copied,null)=="test-user" && ((SecureString)credentialType.GetProperty("Password").GetValue(copied,null)).Length==password.Length,"Credenziale SQL fuori dalla connection string mantenuta.");
                }
            }
            Console.WriteLine("PASS: "+checks+" verifiche sul campo reale dell'editor SSMS e sul driver SqlClient, con dati sintetici; nessun account/server contattato.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
