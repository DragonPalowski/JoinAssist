using System;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Controls;
using JoinAssist;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

sealed class CommitConnection : DbConnection
{
    public Guid ClientConnectionId { get; set; } = Guid.NewGuid();
    public override string ConnectionString { get; set; }
    public override string Database { get { return "TestDb"; } }
    public override string DataSource { get { return "TestServer"; } }
    public override string ServerVersion { get { return "test"; } }
    public override ConnectionState State { get { return ConnectionState.Open; } }
    public override void Open() { throw new Exception("Nessuna rete nei test editor."); }
    public override void Close() { }
    public override void ChangeDatabase(string value) { throw new NotSupportedException(); }
    protected override DbTransaction BeginDbTransaction(IsolationLevel level) { throw new NotSupportedException(); }
    protected override DbCommand CreateDbCommand() { throw new Exception("Nessuna query nei test editor."); }
}
static class ControllerCommitTests
{
    static int checks;
    static void Check(bool ok,string label) { checks++; if(!ok)throw new Exception(label); }
    public static void Run(Assembly plugin)
    {
        string sql="SELECT * FROM Or";
        Scenario(plugin,sql,sql.Length,new JoinProposal { CompletionText="Orders",SearchName="Orders",TargetTable="Orders" },"SELECT * FROM Orders",false,false);
        sql="SELECT o.Cu FROM Orders o";
        Scenario(plugin,sql,sql.IndexOf("Cu")+2,new JoinProposal { CompletionText="CustomerId",SearchName="CustomerId" },"SELECT o.CustomerId FROM Orders o",false,false);
        sql="SELECT o.CustomerId FROM Orders o";
        Scenario(plugin,sql,sql.IndexOf("CustomerId")+3,new JoinProposal { CompletionText="CustomerId",SearchName="CustomerId" },sql,false,false);
        sql="SELECT * FROM Orders JOIN ";
        Scenario(plugin,sql,sql.Length,new JoinProposal { Target="Customers",TargetTable="Customers",Predicate="Orders.CustomerId = Customers.Id",ForeignKeyName="FK_Test" },"SELECT * FROM Orders INNER JOIN Customers ON Orders.CustomerId = Customers.Id ",false,false);
        sql="SELECT * FROM Orders o JOIN Customers c ON ";
        Scenario(plugin,sql,sql.Length,new JoinProposal { Target="Customers",TargetTable="Customers",Predicate="o.CustomerId = c.Id",ForeignKeyName="FK_Test" },sql+"o.CustomerId = c.Id ",false,false);
        sql="SELECT * FROM Orders JOIN ";
        Scenario(plugin,sql,sql.Length,new JoinProposal { Target="Customers",TargetTable="Customers",Predicate="Orders.CustomerId = Customers.Id",ForeignKeyName="FK_Test" },sql,true,false);
        Scenario(plugin,sql,sql.Length,new JoinProposal { Target="Customers",TargetTable="Customers",Predicate="Orders.CustomerId = Customers.Id",ForeignKeyName="FK_Test" },sql.Replace("Orders","Items"),false,true);
        sql="SELECT * FROM custo";
        var customers=new JoinProposal { CompletionText="customers_tb",SearchName="customers_tb",TargetTable="customers_tb" };
        var history=new JoinProposal { CompletionText="customeractivityhistory_vw",SearchName="customeractivityhistory_vw",TargetTable="customeractivityhistory_vw" };
        Scenario(plugin,sql,sql.Length,customers,"SELECT * FROM customers_tb",false,false,new[]{history,customers});
        sql="SELECT * FROM itemdet";
        Scenario(plugin,sql,sql.Length,new JoinProposal { CompletionText="order_item_details_tb",SearchName="order_item_details_tb",TargetTable="order_item_details_tb" },"SELECT * FROM order_item_details_tb",false,false);
        sql="CREATE TABLE #Orders (Id int); SELECT * FROM #Or";
        Scenario(plugin,sql,sql.Length,new JoinProposal { CompletionText="#Orders",SearchName="#Orders",TargetTable="#Orders" },"CREATE TABLE #Orders (Id int); SELECT * FROM #Orders",false,false);
        sql="DECLARE @Items TABLE (Id int); SELECT * FROM @It";
        Scenario(plugin,sql,sql.Length,new JoinProposal { CompletionText="@Items",SearchName="@Items",TargetTable="@Items" },"DECLARE @Items TABLE (Id int); SELECT * FROM @Items",false,false);
        sql="CREATE TABLE #Orders (OrderId int); SELECT t.Or FROM #Orders t";
        Scenario(plugin,sql,sql.IndexOf("t.Or")+4,new JoinProposal { CompletionText="OrderId",SearchName="OrderId" },"CREATE TABLE #Orders (OrderId int); SELECT t.OrderId FROM #Orders t",false,false);
        sql="SELECT * FROM Items WHERE\nSELECT * FROM Items INNER JOIN\nSELECT * FROM Orders WHERE id";
        Scenario(plugin,sql,sql.Length,new JoinProposal { CompletionText="Id",SearchName="Id" },sql.Substring(0,sql.Length-2)+"Id",false,false);
        sql="SELECT o.Na FROM Orders o WHERE o.Id=";
        Scenario(plugin,sql,sql.IndexOf("o.Na")+4,new JoinProposal { CompletionText="Name",SearchName="Name" },"SELECT o.Name FROM Orders o WHERE o.Id=",false,false);
        sql="SELECT * FROM Orders o INNER JOIN \nWHERE o.Id=1\nORDER BY o.Id";
        int joinCaret=sql.IndexOf("\nWHERE");
        Scenario(plugin,sql,joinCaret,new JoinProposal { Target="Customers",TargetTable="Customers",Predicate="o.CustomerId = Customers.Id",ForeignKeyName="FK_Orders_Customers" },"SELECT * FROM Orders o INNER JOIN Customers ON o.CustomerId = Customers.Id \nWHERE o.Id=1\nORDER BY o.Id",false,false);
        sql="SELECT * FROM Orders o INNER JOIN Cu\nWHERE o.Id= -- keep this comment\nORDER BY o.Id";
        joinCaret=sql.IndexOf("\nWHERE");
        Scenario(plugin,sql,joinCaret,new JoinProposal { Target="Customers",TargetTable="Customers",Predicate="o.CustomerId = Customers.Id",ForeignKeyName="FK_Orders_Customers" },"SELECT * FROM Orders o INNER JOIN Customers ON o.CustomerId = Customers.Id \nWHERE o.Id= -- keep this comment\nORDER BY o.Id",false,false);
        sql="SELECT * FROM Orders o JOIN Customers c ON \nWHERE o.Id=";
        Scenario(plugin,sql,sql.IndexOf("\nWHERE"),new JoinProposal { Target="Customers",TargetTable="Customers",Predicate="o.CustomerId = c.Id",ForeignKeyName="FK_Orders_Customers" },"SELECT * FROM Orders o JOIN Customers c ON o.CustomerId = c.Id \nWHERE o.Id=",false,false);
        Console.WriteLine("PASS: "+checks+" controlli di inserimento nel controller editor simulato (nomi, colonne, JOIN, ON, ricerca flessibile, tabelle locali, query/sessione cambiate). Nessuna finestra o rete.");
    }
    static void Scenario(Assembly plugin,string initial,int caretPosition,JoinProposal proposal,string expected,bool reconnect,bool changeQuery,JoinProposal[] options=null)
    {
        string text=initial, status="";
        ITextSnapshot snapshot=null;
        object controller=null;
        var grid=new Grid();
        InterfaceStub buffer=null;
        Func<string,ITextSnapshot> makeSnapshot=value=>{
            var stub=new InterfaceStub(typeof(ITextSnapshot),call=> {
                if(call.MethodName=="get_Length")return value.Length;
                if(call.MethodName=="get_TextBuffer")return buffer.GetTransparentProxy();
                if(call.MethodName=="GetText")return value;
                return call.MethodBase is MethodInfo && ((MethodInfo)call.MethodBase).ReturnType.IsValueType?Activator.CreateInstance(((MethodInfo)call.MethodBase).ReturnType):null;
            });return (ITextSnapshot)stub.GetTransparentProxy();
        };
        string edited=null;
        var edit=new InterfaceStub(typeof(ITextEdit),call=>{
            if(call.MethodName=="Replace") { edited=text.Remove((int)call.Args[0],(int)call.Args[1]).Insert((int)call.Args[0],(string)call.Args[2]);return true; }
            if(call.MethodName=="Apply") { text=edited;snapshot=makeSnapshot(text);return snapshot; }
            return null;
        });
        buffer=new InterfaceStub(typeof(ITextBuffer),call=>call.MethodName=="get_CurrentSnapshot"?(object)snapshot:call.MethodName=="CreateEdit"?edit.GetTransparentProxy():null);
        snapshot=makeSnapshot(text);
        var mapping=new InterfaceStub(typeof(IMappingPoint),null);
        Func<CaretPosition> getPosition=()=>new CaretPosition(new VirtualSnapshotPoint(new SnapshotPoint(snapshot,caretPosition)),(IMappingPoint)mapping.GetTransparentProxy(),PositionAffinity.Successor);
        var caret=new InterfaceStub(typeof(ITextCaret),call=>{
            if(call.MethodName=="get_Position")return getPosition();
            if(call.MethodName=="MoveTo") { caretPosition=((SnapshotPoint)call.Args[0]).Position;return getPosition(); }
            return null;
        });
        var selection=new InterfaceStub(typeof(ITextSelection),call=>call.MethodName=="get_IsEmpty"?(object)true:null);
        var view=new InterfaceStub(typeof(IWpfTextView),call=>{
            switch(call.MethodName) {
                case "get_VisualElement":return grid;
                case "get_TextBuffer":return buffer.GetTransparentProxy();
                case "get_Caret":return caret.GetTransparentProxy();
                case "get_Selection":return selection.GetTransparentProxy();
                case "get_IsClosed":return false;
                case "get_HasAggregateFocus":return false;
                default:return null;
            }
        });
        var controllerType=plugin.GetType("JoinAssist.AutomaticCompletion",true);
        controller=Activator.CreateInstance(controllerType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{view.GetTransparentProxy(),null,null,new Action<string>(s=>status=s)},null);
        var sessionType=controllerType.GetNestedType("Session",BindingFlags.NonPublic);var session=Activator.CreateInstance(sessionType,true);
        var trigger=AutomaticTrigger.TryCreate(initial,caretPosition);Check(trigger!=null,"Trigger del caso editor");
        sessionType.GetField("Trigger").SetValue(session,trigger);sessionType.GetField("Finished").SetValue(session,true);
        var editors=Assembly.Load("SQLEditors");var editorType=editors.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.SqlScriptEditorControl",true);
        var baseType=editors.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptAndResultsEditorControl",true);
        var owner=FormatterServices.GetUninitializedObject(editorType);GC.SuppressFinalize(owner);
        using(var original=new CommitConnection())using(var metadata=new CommitConnection())
        {
            baseType.GetField("m_connection",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(owner,original);
            var binding=typeof(SsmsConnection).GetMethod("Bind",BindingFlags.Static|BindingFlags.NonPublic);
            using(var capture=(SsmsConnection)binding.Invoke(null,new object[]{owner,original,metadata,"TestDb"}))
            {
                sessionType.GetField("Connection").SetValue(session,capture);
                controllerType.GetField("current",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(controller,session);
                var list=(ListBox)controllerType.GetField("list",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);
                list.ItemsSource=options??new[]{proposal};list.SelectedIndex=0;
                if(options!=null)
                {
                    // The old alphabetical selection is still valid but must yield to the new best match.
                    sessionType.GetField("Proposals").SetValue(session,options.ToList());
                    controllerType.GetMethod("ShowResults",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,new[]{session});
                    Check(ReferenceEquals(list.Items[0],proposal) && ReferenceEquals(list.SelectedItem,proposal),"Voce più vicina ordinata E selezionata per Tab, senza mantenere la vecchia scelta automatica");
                    list.SelectedItem=options[0];sessionType.GetField("ManualSelection").SetValue(session,true);
                    controllerType.GetMethod("ShowResults",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,new[]{session});
                    Check(ReferenceEquals(list.SelectedItem,options[0]),"Una scelta esplicita dell'utente rimane selezionata");
                    sessionType.GetField("ManualSelection").SetValue(session,false);
                    controllerType.GetMethod("ShowResults",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,new[]{session});
                }
                if(reconnect)original.ClientConnectionId=Guid.NewGuid();
                if(changeQuery){text=text.Replace("Orders","Items");snapshot=makeSnapshot(text);caretPosition=text.Length;}
                controllerType.GetMethod("Commit",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,null);
                Check(text==expected,"Testo finale editor: "+initial+"; stato="+status);
                Check(reconnect||changeQuery?edited==null:status.StartsWith("Completamento inserito",StringComparison.Ordinal),"Inserimento o rifiuto verificato: "+status);
                Check(capture.Connection!=original,"Il client originale non è usato per i metadati");
            }
        }
        ((IDisposable)controller).Dispose();
    }
}
