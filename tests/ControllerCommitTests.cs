using System;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;
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
        sql="SELECT * FROM Orders o JOIN Customers c ON o.Cust WHERE o.Id=1";
        Scenario(plugin,sql,sql.IndexOf(" WHERE",StringComparison.Ordinal),new JoinProposal{Target="Customers",TargetTable="Customers",Predicate="o.CustomerId = c.Id",ForeignKeyName="FK_Test"},"SELECT * FROM Orders o JOIN Customers c ON o.CustomerId = c.Id  WHERE o.Id=1",false,false);
        sql="SELECT * FROM Orders o JOIN Customers c ON c.I WHERE o.Id=1";
        Scenario(plugin,sql,sql.IndexOf(" WHERE",StringComparison.Ordinal),new JoinProposal{Target="Customers",TargetTable="Customers",Predicate="o.CustomerId = c.Id",ForeignKeyName="FK_Test"},"SELECT * FROM Orders o JOIN Customers c ON c.Id = o.CustomerId  WHERE o.Id=1",false,false);
        sql="SELECT * FROM Orders o JOIN Customers c ON c.Na";
        Scenario(plugin,sql,sql.Length,new JoinProposal{CompletionText="c.Name",SearchName="Name"},"SELECT * FROM Orders o JOIN Customers c ON c.Name",false,false);
        sql="SELECT * FROM Orders JOIN ";
        Scenario(plugin,sql,sql.Length,new JoinProposal { Target="Customers",TargetTable="Customers",Predicate="Orders.CustomerId = Customers.Id",ForeignKeyName="FK_Test" },sql,true,false);
        Scenario(plugin,sql,sql.Length,new JoinProposal { Target="Customers",TargetTable="Customers",Predicate="Orders.CustomerId = Customers.Id",ForeignKeyName="FK_Test" },sql.Replace("Orders","Items"),false,true);
        sql="SELECT * FROM custo";
        var customers=new JoinProposal { CompletionText="customers_tb",SearchName="customers_tb",TargetTable="customers_tb" };
        var history=new JoinProposal { CompletionText="customeractivityhistory_vw",SearchName="customeractivityhistory_vw",TargetTable="customeractivityhistory_vw" };
        Scenario(plugin,sql,sql.Length,customers,"SELECT * FROM customers_tb",false,false,new[]{history,customers});
        Scenario(plugin,sql,sql.Length,customers,"SELECT * FROM customeractivityhistory_vw",false,false,new[]{history,customers},typingSuffix:"m",commitManual:true);
        Scenario(plugin,sql,sql.Length,customers,"SELECT * FROM customeractivityhistory_vw",false,false,new[]{history,customers},commitManual:true,mouseCommit:true);
        sql="sel";
        Scenario(plugin,sql,sql.Length,new JoinProposal{CompletionText="SELECT ",SearchName="SELECT",Keyword="SELECT"},"SELECT ",false,false);
        sql="SELECT * froM Orders WHERE Id=1";
        Scenario(plugin,sql,sql.IndexOf("froM")+3,new JoinProposal{CompletionText="FROM",SearchName="FROM",Keyword="FROM"},"SELECT * FROM Orders WHERE Id=1",false,false);
        sql="SELECT * FROM Customers WHERE Name LIKE AND Id=1";
        int templateAt=sql.IndexOf(" AND",StringComparison.Ordinal);
        Scenario(plugin,sql,templateAt,new JoinProposal{CompletionText="LIKE '%%'",SearchName="LIKE",IsFilterTemplate=true,TemplateOperator="LIKE",CaretOffset=7},"SELECT * FROM Customers WHERE Name LIKE '%%' AND Id=1",false,false,expectedCaret:sql.IndexOf("LIKE",StringComparison.Ordinal)+7);
        sql="SELECT * FROM Customers WHERE Name NOT IN";
        Scenario(plugin,sql,sql.Length,new JoinProposal{CompletionText="NOT IN ('', '')",SearchName="NOT IN",IsFilterTemplate=true,TemplateOperator="NOT IN",CaretOffset=9},sql+" ('', '')",false,false,expectedCaret:sql.IndexOf("NOT IN",StringComparison.Ordinal)+9);
        sql="SELECT * FROM patietns";
        Scenario(plugin,sql,sql.Length,new JoinProposal{CompletionText="patients_tb",SearchName="patients_tb",TargetTable="patients_tb"},"SELECT * FROM patients_tb",false,false);
        sql="SELECT p.Custmoer FROM Orders p WHERE p.Id=1";
        Scenario(plugin,sql,sql.IndexOf("Custmoer")+8,new JoinProposal{CompletionText="CustomerId",SearchName="CustomerId"},"SELECT p.CustomerId FROM Orders p WHERE p.Id=1",false,false);
        sql="SELECT Ord FROM Orders p";
        Scenario(plugin,sql,sql.IndexOf("Ord")+3,new JoinProposal{CompletionText="p",SearchName="p",AlternateSearchName="Orders",Category="Riferimento"},"SELECT p FROM Orders p",false,false);
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
        sql="INSERT Orders (Cu) VALUES (1)";
        Scenario(plugin,sql,sql.IndexOf("Cu")+2,new JoinProposal{CompletionText="CustomerId",SearchName="CustomerId"},"INSERT Orders (CustomerId) VALUES (1)",false,false);
        sql="UPDATE Orders SET Cu=1 WHERE Id=3";
        Scenario(plugin,sql,sql.IndexOf("Cu")+2,new JoinProposal{CompletionText="CustomerId",SearchName="CustomerId"},"UPDATE Orders SET CustomerId=1 WHERE Id=3",false,false);
        sql="INSERT INTO Orders ";
        string structure="(\r\n    CustomerId,\r\n    TenantId\r\n)\r\nVALUES (\r\n    @CustomerId,\r\n    @TenantId\r\n);";
        var skeleton=new JoinProposal{IsInsertTemplate=true,SearchName="Prepara INSERT",CompletionText=structure,CaretOffset=structure.IndexOf("@CustomerId",StringComparison.Ordinal)};
        Scenario(plugin,sql,sql.Length,skeleton,sql+structure,false,false,expectedCaret:sql.Length+skeleton.CaretOffset.Value);
        Scenario(plugin,sql,sql.Length,skeleton,sql,true,false);
        Scenario(plugin,sql,sql.Length,skeleton,sql.Replace("Orders","Items"),false,true);
        sql="INSERT Orders (Id, ) VALUES (1,2,3); SELECT * FROM Items";
        int insertCaret=sql.IndexOf(")",StringComparison.Ordinal);
        Scenario(plugin,sql,insertCaret,new JoinProposal{IsInsertTemplate=true,SearchName="Inserisci colonne mancanti",CompletionText="CustomerId, TenantId"},sql.Insert(insertCaret,"CustomerId, TenantId"),false,false);
        sql="SELECT * FROM Items JOIN Cu\nWHERE Items.Id=3 -- preserved";
        var step1=new JoinProposal{Target="Orders",TargetTable="Orders",Predicate="Items.OrderId = Orders.Id"};
        var step2=new JoinProposal{Target="Customers",TargetTable="Customers",Predicate="Orders.CustomerId = Customers.Id"};
        Scenario(plugin,sql,sql.IndexOf("\nWHERE"),new JoinProposal{Target="Customers",TargetTable="Customers",Steps=new System.Collections.Generic.List<JoinProposal>{step1,step2}},
            "SELECT * FROM Items INNER JOIN Orders ON Items.OrderId = Orders.Id"+Environment.NewLine+"INNER JOIN Customers ON Orders.CustomerId = Customers.Id \nWHERE Items.Id=3 -- preserved",false,false);
        sql="SELECT * FROM WorkDetails_tb INNER JOIN ServicesRev\nWHERE WorkDetails_tb.Id=3 -- preserved";
        var common=new JoinProposal{Target="ServicesRev_tb",TargetTable="ServicesRev_tb",SharedKeyParent="dbo.Services_tb",Predicate="WorkDetails_tb.idService = ServicesRev_tb.idService"};
        Scenario(plugin,sql,sql.IndexOf("\nWHERE",StringComparison.Ordinal),common,
            "SELECT * FROM WorkDetails_tb INNER JOIN ServicesRev_tb ON WorkDetails_tb.idService = ServicesRev_tb.idService \nWHERE WorkDetails_tb.Id=3 -- preserved",false,false);
        Scenario(plugin,sql,sql.IndexOf("\nWHERE",StringComparison.Ordinal),common,sql,true,false);
        sql=";WITH Requests AS (SELECT r.outcome, JSON_VALUE(j.value,'$.id') AS JsonId FROM Orders r CROSS APPLY OPENJSON(r.Name) j OUTER APPLY OPENJSON(j.value) a) SELECT * FROM Requests;";
        int jsonCaret=sql.IndexOf("outcome",StringComparison.Ordinal)+3;
        var outcome=new JoinProposal{CompletionText="Outcome",SearchName="Outcome",Category="Colonna"};
        Scenario(plugin,sql,jsonCaret,outcome,sql.Replace("r.outcome","r.Outcome"),false,false);
        Scenario(plugin,sql,jsonCaret,outcome,sql,true,false);
        foreach(string op in new[]{"LIKE","NOT LIKE","IN","NOT IN"})
        {
            sql="SELECT * FROM Customers WHERE Name "+op+"\n\nSELECT * FROM Orders";
            int filterCaret=sql.IndexOf("\n",StringComparison.Ordinal);
            var trigger=AutomaticTrigger.TryCreate(sql,filterCaret);
            foreach(var table in trigger.Context.Tables)table.Columns=new System.Collections.Generic.List<ColumnShape>{new ColumnShape{Name="Name",TypeName="varchar",MaxLength=50}};
            var item=TypingTemplates.FilterItems(trigger).Single();
            string result=sql.Substring(0,trigger.Context.ReplaceStart)+item.CompletionText+sql.Substring(trigger.Context.ReplaceStart+trigger.Context.ReplaceLength);
            Scenario(plugin,sql,filterCaret,item,result,false,false,expectedCaret:trigger.Context.ReplaceStart+item.CaretOffset.Value);
        }
        foreach(var pair in new[]{new[]{"li","LIKE"},new[]{"lik","LIKE"},new[]{"no","NOT LIKE"},new[]{"not","NOT IN"},new[]{"not li","NOT LIKE"},new[]{"in","IN"},new[]{"not in","NOT IN"}})
        {
            sql="SELECT * FROM Customers WHERE Name "+pair[0]+"\n\nSELECT * FROM Orders";
            int filterCaret=sql.IndexOf("\n",StringComparison.Ordinal);var trigger=AutomaticTrigger.TryCreate(sql,filterCaret);
            foreach(var table in trigger.Context.Tables)table.Columns=new System.Collections.Generic.List<ColumnShape>{new ColumnShape{Name="Name",TypeName="varchar",MaxLength=50}};
            var item=TypingTemplates.FilterItems(trigger).Single(p=>p.TemplateOperator==pair[1]);
            string result=sql.Substring(0,trigger.Context.ReplaceStart)+item.CompletionText+sql.Substring(trigger.Context.ReplaceStart+trigger.Context.ReplaceLength);
            Scenario(plugin,sql,filterCaret,item,result,false,false,expectedCaret:trigger.Context.ReplaceStart+item.CaretOffset.Value);
        }
        sql="SELECT * FROM Customers WHERE Name li";
        Scenario(plugin,sql,sql.Length,new JoinProposal{Keyword="LIKE",SearchName="LIKE",CompletionText="LIKE "},"SELECT * FROM Customers WHERE Name LIKE ",false,false,noConnection:true);
        Scenario(plugin,sql,sql.Length,new JoinProposal{IsFilterTemplate=true,TemplateOperator="LIKE",SearchName="LIKE",CompletionText="LIKE '%%'"},sql,false,false,noConnection:true);
        Console.WriteLine("PASS: "+checks+" controlli di inserimento nel controller editor simulato (nomi, colonne, JOIN, ON, ricerca flessibile, tabelle locali, query/sessione cambiate). Nessuna finestra o rete.");
    }
    static void Scenario(Assembly plugin,string initial,int caretPosition,JoinProposal proposal,string expected,bool reconnect,bool changeQuery,JoinProposal[] options=null,int? expectedCaret=null,string typingSuffix=null,bool commitManual=false,bool mouseCommit=false,bool noConnection=false)
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
                sessionType.GetField("Connection").SetValue(session,noConnection?null:capture);
                controllerType.GetField("current",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(controller,session);
                var list=(ListBox)controllerType.GetField("list",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);
                list.ItemsSource=options??new[]{proposal};list.SelectedIndex=0;
                if(proposal.IsInsertTemplate || proposal.IsFilterTemplate)
                {
                    var preview=(TextBlock)controllerType.GetField("preview",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);
                    Check(preview.Visibility==System.Windows.Visibility.Visible && preview.Text.Length>0,"Anteprima modello visibile prima dell'inserimento");
                }
                if(options!=null)
                {
                    // The old alphabetical selection is still valid but must yield to the new best match.
                    sessionType.GetField("Proposals").SetValue(session,options.ToList());
                    Present(controllerType,controller,session);
                    Check(ReferenceEquals(list.Items[0],proposal) && ReferenceEquals(list.SelectedItem,proposal),"Voce più vicina ordinata E selezionata per Tab, senza mantenere la vecchia scelta automatica");
                    var row=Row(list,options[0]);
                    var click=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent};
                    var label=Child<ProposalText>(row);var run=label==null?null:label.Inlines.OfType<Run>().FirstOrDefault();
                    Check(run!=null,"Test anche sul testo interno della voce, non solo sul bordo della riga");run.RaiseEvent(click);
                    Check(click.Handled && ReferenceEquals(list.SelectedItem,options[0]) && edited==null,"Il clic reale sulla riga non focalizzabile seleziona proprio quella voce, senza inserirla");
                    Present(controllerType,controller,session);
                    Check(ReferenceEquals(list.SelectedItem,options[0]),"Una scelta esplicita dell'utente rimane selezionata");
                    if(typingSuffix!=null)
                    {
                        text=text.Insert(caretPosition,typingSuffix);caretPosition+=typingSuffix.Length;snapshot=makeSnapshot(text);
                        var updated=AutomaticTrigger.TryCreate(text,caretPosition);Check(updated!=null,"Nuovo prefisso durante selezione manuale");
                        Wait((Task)controllerType.GetMethod("ApplyTrigger",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,new object[]{updated,snapshot,null}));
                        Check(ReferenceEquals(list.SelectedItem,options[0]),"La selezione esplicita resta stabile continuando a digitare");
                    }
                    if(!commitManual)
                    {
                        sessionType.GetField("ManualSelection").SetValue(session,false);
                        var previous=snapshot;
                        var pending=(Task)controllerType.GetMethod("ShowResults",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,new[]{session});
                        snapshot=makeSnapshot(text);Wait(pending);
                        Check(ReferenceEquals(list.SelectedItem,options[0]),"I risultati di uno snapshot superato non cambiano la selezione");snapshot=previous;
                        Present(controllerType,controller,session);
                    }
                }
                if(reconnect)original.ClientConnectionId=Guid.NewGuid();
                if(changeQuery){text=text.Replace("Orders","Items");snapshot=makeSnapshot(text);caretPosition=text.Length;}
                if(mouseCommit)
                {var row=Row(list,options[0]);Check((bool)controllerType.GetMethod("SelectMouseItem",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,new object[]{row,2}),"Il doppio clic sulla voce scelta viene gestito");}
                else controllerType.GetMethod("Commit",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,null);
                Check(text==expected,"Testo finale editor: "+initial+"; stato="+status);
                if(expectedCaret.HasValue)Check(caretPosition==expectedCaret.Value,"Cursore sul primo parametro INSERT");
                Check(reconnect||changeQuery||noConnection && proposal.Keyword==null?edited==null:status.StartsWith("Completamento inserito",StringComparison.Ordinal),"Inserimento o rifiuto verificato: "+status);
                Check(capture.Connection!=original,"Il client originale non è usato per i metadati");
            }
        }
        ((IDisposable)controller).Dispose();
    }
    static void Present(Type type,object controller,object session)
    {
        var task=(Task)type.GetMethod("ShowResults",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(controller,new[]{session});
        Wait(task);
    }
    static ListBoxItem Row(ListBox list,JoinProposal proposal)
    {
        list.Measure(new Size(600,240));list.Arrange(new Rect(0,0,600,240));list.UpdateLayout();list.ScrollIntoView(proposal);list.UpdateLayout();
        var row=list.ItemContainerGenerator.ContainerFromItem(proposal) as ListBoxItem;
        Check(row!=null && !row.Focusable,"Riga generata con il reale stile non focalizzabile");return row;
    }
    static T Child<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {var child=VisualTreeHelper.GetChild(parent,i);var result=child as T??Child<T>(child);if(result!=null)return result;}
        return null;
    }
    static void Wait(Task task)
    {
        var until=DateTime.UtcNow.AddSeconds(10);
        while(!task.IsCompleted)
        {
            if(DateTime.UtcNow>until)throw new TimeoutException("Async presentation did not finish");
            var frame=new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>frame.Continue=false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
}
