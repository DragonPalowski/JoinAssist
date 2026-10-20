using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Controls;
using JoinAssist;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

static class DeleteEditorTests
{
    internal static void Run(Assembly plugin)
    {
        foreach(string scenario in new[]{"insert","snapshot","caret","connection","closed","blocked","span"})Test(plugin,scenario);
        Console.WriteLine("PASS: 7 DELETE editor cases (explicit insertion, exact span and stale/blocked refusal). No SQL, dialog or network executed.");
    }
    static void Test(Assembly plugin,string scenario)
    {
        string original="SELECT 1;\nDELETE FROM Orders WHERE Id=@id;\nSELECT 2;",text=original;
        int originalCaret=original.IndexOf("Id=@id",StringComparison.Ordinal)+2,caretPosition=originalCaret;bool closed=false;int edits=0;
        ITextSnapshot snapshot=null;InterfaceStub buffer=null;
        Func<string,ITextSnapshot> make=value=>(ITextSnapshot)new InterfaceStub(typeof(ITextSnapshot),call=>call.MethodName=="GetText"?(object)value:call.MethodName=="get_Length"?value.Length:call.MethodName=="get_TextBuffer"?buffer.GetTransparentProxy():null).GetTransparentProxy();
        string edited=null;
        var edit=new InterfaceStub(typeof(ITextEdit),call=>{
            if(call.MethodName=="Replace"){edits++;edited=text.Remove((int)call.Args[0],(int)call.Args[1]).Insert((int)call.Args[0],(string)call.Args[2]);return true;}
            if(call.MethodName=="Apply"){text=edited;snapshot=make(text);return snapshot;}return null;
        });
        buffer=new InterfaceStub(typeof(ITextBuffer),call=>call.MethodName=="get_CurrentSnapshot"?(object)snapshot:call.MethodName=="CreateEdit"?edit.GetTransparentProxy():null);snapshot=make(text);
        var mapping=new InterfaceStub(typeof(IMappingPoint),null);
        Func<CaretPosition> position=()=>new CaretPosition(new VirtualSnapshotPoint(new SnapshotPoint(snapshot,caretPosition)),(IMappingPoint)mapping.GetTransparentProxy(),PositionAffinity.Successor);
        var caret=new InterfaceStub(typeof(ITextCaret),call=>{if(call.MethodName=="get_Position")return position();if(call.MethodName=="MoveTo"){caretPosition=((SnapshotPoint)call.Args[0]).Position;return position();}return null;});
        var view=new InterfaceStub(typeof(IWpfTextView),call=>call.MethodName=="get_TextBuffer"?buffer.GetTransparentProxy():call.MethodName=="get_Caret"?caret.GetTransparentProxy():call.MethodName=="get_IsClosed"?(object)closed:call.MethodName=="get_VisualElement"?new Grid():null);
        var type=plugin.GetType("JoinAssist.JoinEditorController",true);var controller=FormatterServices.GetUninitializedObject(type);
        type.GetField("view",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(controller,view.GetTransparentProxy());
        var request=DeleteRequest.Create(original,originalCaret);var schema=new DeleteSchema();var table=new DeleteTable{Id=1,Schema="dbo",Name="Orders",BareAllowed=true};table.Columns.Add("Id");table.PrimaryKey.Add("Id");schema.Tables.Add(table);
        var plan=DeletePlanner.Build(request,schema);var originalSnapshot=snapshot;
        var editors=Assembly.Load("SQLEditors");var ownerType=editors.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.SqlScriptEditorControl",true);
        var owner=FormatterServices.GetUninitializedObject(ownerType);GC.SuppressFinalize(owner);
        var ownerBase=editors.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptAndResultsEditorControl",true);
        using(var native=new CommitConnection())using(var metadata=new CommitConnection())
        {
            ownerBase.GetField("m_connection",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(owner,native);
            var bind=typeof(SsmsConnection).GetMethod("Bind",BindingFlags.NonPublic|BindingFlags.Static);
            using(var capture=(SsmsConnection)bind.Invoke(null,new object[]{owner,native,metadata,"TestDb"}))
            {
                if(scenario=="snapshot"){text=original.Replace("@id","@other");snapshot=make(text);}
                if(scenario=="caret")caretPosition++;
                if(scenario=="connection")native.ClientConnectionId=Guid.NewGuid();
                if(scenario=="closed")closed=true;
                if(scenario=="blocked")plan.Problems.Add("Ciclo");
                if(scenario=="span")request.Original="changed";
                bool refused=false;
                try{type.GetMethod("InsertDeletePlan",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,new object[]{originalSnapshot,originalCaret,capture,request,plan});}
                catch(TargetInvocationException ex){if(!(ex.InnerException is InvalidOperationException))throw;refused=true;}
                if(scenario=="insert")
                {
                    if(refused || edits!=1 || text!=original.Remove(request.Start,request.Length).Insert(request.Start,plan.Script) || caretPosition!=request.Start)
                        throw new Exception("DELETE editor insertion must replace only current DELETE in one edit and keep surrounding SELECTs.");
                }
                else if(!refused || edits!=0)throw new Exception("DELETE editor must refuse stale/blocked scenario: "+scenario);
            }
        }
    }
}
