using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using JoinAssist;
using Microsoft.SqlServer.TransactSql.ScriptDom;

static class DeletePlannerTests
{
    static int checks;
    static void Check(bool ok,string name){checks++;if(!ok)throw new Exception("Delete planner: "+name);}
    static DeleteRequest Request(string text="DELETE FROM Orders WHERE Id=@id;")
    {int caret=text.IndexOf('|');if(caret<0)caret=text.Length;return DeleteRequest.Create(text.Replace("|",""),caret);}
    static DeleteTable Table(int id,string name,params string[] columns)
    {var table=new DeleteTable{Id=id,Name=name,Schema="dbo",BareAllowed=true};table.Columns.Add("Id");table.Columns.AddRange(columns);table.PrimaryKey.Add("Id");return table;}
    static DeleteLink Link(int id,int child,int parent,string childColumn="ParentId",string parentColumn="Id",int action=0)
    {var link=new DeleteLink{Action=action,Key=new ForeignKey{Id=id,Name="FK_"+id,ChildId=child,ParentId=parent}};link.Key.Columns.Add(new KeyColumn{Ordinal=1,Child=childColumn,Parent=parentColumn});return link;}
    static DeleteSchema Schema()
    {
        var schema=new DeleteSchema();schema.Tables.AddRange(new[]{Table(1,"Orders"),Table(2,"Items","OrderId"),Table(3,"Details","ItemId"),Table(4,"Processing","OrderId")});
        schema.Links.AddRange(new[]{Link(10,2,1,"OrderId"),Link(11,3,2,"ItemId"),Link(12,4,1,"OrderId")});return schema;
    }
    static DeletePlan Plan(DeleteSchema schema=null,DeleteRequest request=null){return DeletePlanner.Build(request??Request(),schema??Schema());}
    internal static void Run()
    {
        var request=Request();Check(request.Table=="Orders" && request.Filter=="Id=@id" && request.Original=="DELETE FROM Orders WHERE Id=@id;","request exact filter and span");
        foreach(string sample in new[]{"DELETE Orders WHERE Id=@id;","DELETE FROM dbo.Orders WHERE Id=@id;","DELETE o FROM dbo.Orders o WHERE o.Id=@id;","DELETE FROM o FROM dbo.Orders AS o WHERE o.Id=@id;","SELECT * FROM Items WHERE\nDELETE FROM Orders WHERE I|d=@id;\nSELECT * FROM Customers","DELETE FROM Orders WHERE EXISTS(SELECT 1 FROM Items i WHERE i.OrderId=Orders.Id);","DELETE FROM Orders WHERE Id=@id; \n|"})
        {var parsed=Request(sample);Check(parsed.Table=="Orders" && parsed.Filter.Length>0,"accept simple filtered request "+sample);Check(parsed.Start>=0 && parsed.Length>0,"replacement span");}
        foreach(string sample in new[]{"DELETE FROM Orders;","DELETE FROM Orders WHERE","DELETE FROM Orders WHERE Id","DELETE TOP (1) FROM Orders WHERE Id=1;","DELETE FROM Orders OUTPUT deleted.Id WHERE Id=1;","DELETE o FROM Orders o JOIN Items i ON o.Id=i.OrderId WHERE o.Id=1;","WITH c AS (SELECT * FROM Orders) DELETE FROM c WHERE Id=1;","DELETE FROM db.dbo.Orders WHERE Id=1;","DELETE FROM #Orders WHERE Id=1;","DELETE FROM Orders WITH (ROWLOCK) WHERE Id=1;","SELECT * FROM Orders WHERE Id=1;","USE OtherDb; DELETE FROM Orders WHERE Id=1;","DELETE FROM Orders WHERE Id=@@ROWCOUNT;","DELETE FROM Orders WHERE XACT_STATE()=0;"})
        {bool rejected=false;try{Request(sample);}catch(InvalidOperationException){rejected=true;}Check(rejected,"reject incomplete/unsupported DELETE "+sample);}
        foreach(string sample in new[]{"EXECUTE AS USER='example'; DELETE FROM Orders WHERE Id=1;","EXEC AS USER='example';\nGO\nDELETE FROM Orders WHERE Id=1;","DELETE FROM Orders WHERE Id=SCOPE_IDENTITY();"})
        {bool rejected=false;try{Request(sample);}catch(InvalidOperationException){rejected=true;}Check(rejected,"reject execution/session-state context");}
        var plan=Plan();Check(plan.Ready && plan.Steps.Count==4,"multilevel plan ready");
        var order=plan.Steps.Select(s=>s.Table.Id).ToList();Check(order.IndexOf(3)<order.IndexOf(2) && order.IndexOf(2)<order.IndexOf(1) && order.IndexOf(4)<order.IndexOf(1),"children precede parents");
        Check(plan.Steps.All(s=>s.Explicit),"NO ACTION requires explicit deletes");
        Parse(plan.Script);
        var structure=Structure(plan.Script);
        Check(structure.Stages.Count==4 && structure.Stages.Max()<structure.FirstDelete && structure.ScopedDeletes==4,"all row scopes frozen before any PK-scoped delete");
        Check(plan.Script.IndexOf("SELECT [Orders].[Id]",StringComparison.Ordinal)<plan.Script.IndexOf("DELETE [ja_delete]",StringComparison.Ordinal),"capture root before deletion");
        Check(plan.Script.Contains("WHERE Id=@id") && plan.Script.Contains("[ja_source].[ItemId] = [ja_parent].[Id]") && !plan.Script.Contains("DISABLE") && !plan.Script.Contains("NOCHECK"),"propagate original scope and never disable FK");
        Check(plan.Script.Contains("UPDLOCK, HOLDLOCK") && plan.Script.Contains("@@TRANCOUNT <> 0") && plan.Script.Contains("IMPLICIT_TRANSACTIONS") && plan.Script.Contains("SET ROWCOUNT 0") && plan.Script.Contains("IF XACT_STATE() <> 0 ROLLBACK") && plan.Script.Contains("SET XACT_ABORT OFF"),"transaction, locks, limits and error cleanup");
        string guards=plan.Script.Substring(0,plan.Script.IndexOf("BEGIN TRY",StringComparison.Ordinal));Check(guards.Contains("RAISERROR") && guards.Contains("RETURN;") && !guards.Contains("THROW"),"guards do not throw under caller XACT_ABORT or roll back caller transaction");
        Check(plan.Script.Split(new[]{"ROLLBACK TRANSACTION; -- PROVA"},StringSplitOptions.None).Length==2 && !plan.Script.Split('\n').Any(l=>l.TrimStart().StartsWith("COMMIT",StringComparison.Ordinal)),"default script rolls back and never commits");
        Check(Plan().Script!=plan.Script,"independent plans use distinct temporary names");
        var schema=Schema();schema.Links[0].Action=1;plan=Plan(schema);
        Check(plan.Ready && !plan.Steps.Single(s=>s.Table.Id==2).Explicit && plan.Steps.Single(s=>s.Table.Id==3).Explicit,"cascade child still has explicit blocking grandchild");
        Check(!DeleteTargets(plan.Script).Contains("Items") && DeleteTargets(plan.Script).Contains("Details"),"cascade child not redundantly deleted");
        Parse(plan.Script);
        schema=Schema();schema.Links[0].Action=2;plan=Plan(schema);Check(plan.Ready && plan.Steps.All(s=>s.Table.Id!=2 && s.Table.Id!=3) && plan.Notices.Any(n=>n.Contains("SET NULL")),"SET NULL does not delete child graph");
        schema.Tables[1].Triggers=true;Check(!Plan(schema).Ready,"SET action with active trigger blocks plan even when child is not deleted");schema.Tables[1].Triggers=false;
        schema.Links[0].Action=3;plan=Plan(schema);Check(plan.Notices.Any(n=>n.Contains("SET DEFAULT")),"SET DEFAULT visible");
        schema=Schema();schema.Links[0].Key.Disabled=true;plan=Plan(schema);Check(plan.Steps.Count==2 && plan.Notices.Any(n=>n.Contains("disabilitata")),"disabled key not traversed");
        schema=Schema();schema.Links[0].Key.Untrusted=true;Check(Plan(schema).Ready && Plan(schema).Notices.Any(n=>n.Contains("trusted")),"untrusted enabled FK retained");
        schema=Schema();schema.Links[0].Action=1;schema.Links[1].Action=1;plan=Plan(schema);Check(plan.Steps.Count(s=>s.Explicit)==2 && !DeleteTargets(plan.Script).Contains("Items") && !DeleteTargets(plan.Script).Contains("Details"),"cascade chain keeps only required explicit branches/root");
        schema=Schema();schema.Tables[0].Columns.Add("TenantId");schema.Tables[0].PrimaryKey.Add("TenantId");schema.Tables[1].Columns.Add("TenantId");schema.Links[0].Key.Columns.Add(new KeyColumn{Ordinal=2,Child="TenantId",Parent="TenantId"});plan=Plan(schema);
        Check(plan.Ready && plan.Script.Contains("[ja_source].[OrderId] = [ja_parent].[Id] AND [ja_source].[TenantId] = [ja_parent].[TenantId]") && plan.Script.Contains("[ja_delete].[Id] = [ja_scope].[Id] AND [ja_delete].[TenantId] = [ja_scope].[TenantId]"),"composite FK and PK all pairs");
        Parse(plan.Script);
        schema=Schema();schema.Tables[0].Columns.Add("Code");schema.Links[0].Key.Columns[0].Parent="Code";plan=Plan(schema);
        Check(plan.Script.Contains("SELECT [Orders].[Id], [Orders].[Code] INTO") && plan.Script.Contains("[ja_parent].[Code]"),"non-PK referenced unique key captured before delete");
        schema=Schema();schema.Tables[2].Columns.Add("ProcessingId");schema.Links.Add(Link(13,3,4,"ProcessingId"));plan=Plan(schema);
        Check(plan.Ready && plan.Steps.Count==4 && plan.Script.Contains("\r\n       OR EXISTS") && DeleteTargets(plan.Script).Count(n=>n=="Details")==1,"multiple paths union scopes and delete table once");
        Parse(plan.Script);
        schema=Schema();schema.Links[1].Action=1;schema.Tables[2].Columns.Add("ProcessingId");schema.Links.Add(Link(13,3,4,"ProcessingId"));plan=Plan(schema);
        Check(plan.Steps.Single(s=>s.Table.Id==3).Explicit,"mixed CASCADE/NO ACTION scopes require explicit delete");
        schema=Schema();schema.Tables[0].Columns.Add("SelfId");schema.Links.Add(Link(14,1,1,"SelfId"));Check(!Plan(schema).Ready && Plan(schema).Problems.Any(p=>p.Contains("Ciclo")),"self FK blocks plan");
        schema=Schema();schema.Tables[0].Columns.Add("DetailId");schema.Links.Add(Link(14,1,3,"DetailId"));Check(!Plan(schema).Ready,"multilevel cycle blocks plan");
        schema=Schema();schema.Tables[2].Columns.Add("ProcessingId");schema.Links.Add(Link(13,3,4,"ProcessingId",action:3));plan=Plan(schema);
        Check(plan.Ready && plan.Steps.FindIndex(s=>s.Table.Id==3)<plan.Steps.FindIndex(s=>s.Table.Id==4),"SET DEFAULT between selected branches constrains delete order");
        foreach(string issue in new[]{"pk","trigger","temporal","policy","memory","columns","hidden"})
        {
            schema=Schema();
            if(issue=="pk")schema.Tables[1].PrimaryKey.Clear();if(issue=="trigger")schema.Tables[1].Triggers=true;if(issue=="temporal")schema.Tables[1].Temporal=true;if(issue=="policy")schema.Tables[1].SecurityPolicy=true;
            if(issue=="memory")schema.Tables[1].MemoryOptimized=true;
            if(issue=="columns")schema.Links[0].Key.Columns[0].Child="Missing";if(issue=="hidden")schema.Tables.RemoveAt(1);
            plan=Plan(schema);Check(!plan.Ready && plan.Script==null && plan.Problems.Count>0,"no partial script on unsupported "+issue);
        }
        schema=Schema();schema.Tables[0].Name="Order]s";plan=Plan(schema,Request("DELETE FROM [Order]]s] WHERE [Order]]s].Id=@id;"));Check(plan.Ready && plan.Script.Contains("[dbo].[Order]]s]"),"identifier delimiters escaped");Parse(plan.Script);
        plan=Plan(request:Request("DELETE o FROM Orders o WHERE o.Id=@id;"));Check(plan.Script.Contains("FROM [dbo].[Orders] AS [o] WITH") && plan.Script.Contains("WHERE o.Id=@id"),"original root alias preserved");Parse(plan.Script);
        schema=Schema();schema.Tables.Add(Table(5,"Unrelated","OrderId"));schema.Links.Add(Link(20,1,5,"Id"));Check(Plan(schema).Steps.Count==4,"do not walk from child toward its other parents");
        var rootOnly=new DeleteSchema();rootOnly.Tables.Add(Table(1,"Orders"));Check(Plan(rootOnly).Steps.Count==1,"table without children still scoped");
        rootOnly.Tables[0].RowVersions.Add("Id");plan=Plan(rootOnly);Check(plan.Script.Contains("CONVERT(binary(8), [Orders].[Id]) AS [Id]"),"rowversion keys captured as stable binary values");Parse(plan.Script);
        schema=Schema();schema.Tables.Add(Table(9,"orders"));Check(Plan(schema).Ready,"exact root casing resolves case-sensitive duplicate names");
        schema.Tables[0].Columns.Add("id");Check(!Plan(schema).Ready,"case-only column names not silently collapsed in temp scopes");
        schema=Schema();schema.Links[0].Key.Name="FK_bad\r\nDROP TABLE x;\u2028DELETE y;";plan=Plan(schema);Check(!plan.Script.Contains("\r\nDROP") && !plan.Script.Contains("\u2028"),"metadata comments cannot inject lines");
        string original="SELECT 1;\nDELETE FROM Orders WHERE I|d=@id;\nSELECT 2;";request=Request(original);string changed=original.Replace("|","").Remove(request.Start,request.Length).Insert(request.Start,Plan(request:request).Script);
        Check(changed.StartsWith("SELECT 1;\n",StringComparison.Ordinal) && changed.EndsWith("\nSELECT 2;",StringComparison.Ordinal),"replacement preserves surrounding queries");
        using(var db=new FakeDatabase())
        {
            plan=DeletePlanner.LoadAsync(db,Request(),CancellationToken.None).GetAwaiter().GetResult();Check(plan.Ready && plan.Steps.Select(s=>s.Table.Name).SequenceEqual(new[]{"Items","Orders"}),"reader loads incoming graph only");
            Check(db.Commands.Count==3 && db.Commands.All(c=>c.CommandText.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)),"only metadata SELECTs reach database");
            Check(!db.Commands.Any(c=>c.CommandText.Contains("@id") || c.CommandText.Contains("WHERE Id=")),"original filter never sent to database");
            DeletePlanner.LoadAsync(db,Request(),CancellationToken.None).GetAwaiter().GetResult();Check(db.Commands.Count==6,"planner does not reuse stale completion cache");
        }
        using(var db=new FakeDatabase{DeletePermission=0})
        {bool denied=false;try{DeletePlanner.LoadAsync(db,Request(),CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidOperationException){denied=true;}Check(denied && db.Commands.Count==1,"insufficient metadata visibility blocks plan");}
        schema=new DeleteSchema();schema.Tables.Add(Table(1,"Orders"));for(int i=2;i<=65;i++){schema.Tables.Add(Table(i,"T"+i,"ParentId"));schema.Links.Add(Link(i,i,i-1));}
        Check(!Plan(schema).Ready && Plan(schema).Problems.Any(p=>p.Contains("64")),"graph size limit blocks without partial script");
        using(var db=new FakeDatabase())using(var cancelled=new CancellationTokenSource())
        {cancelled.Cancel();bool stopped=false;try{DeletePlanner.LoadAsync(db,Request(),cancelled.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}Check(stopped,"cancellation respected");}
        Console.WriteLine("PASS: "+checks+" DELETE planner checks (filtered request, FK graph, cycles/actions, frozen key scopes, SQL syntax, rollback default, read-only metadata and replacement span). No DELETE or real database executed.");
    }
    sealed class Deletes:TSqlFragmentVisitor
    {
        internal readonly List<string> Names=new List<string>();
        public override void ExplicitVisit(DeleteStatement node){var visitor=new Names();node.DeleteSpecification.FromClause.Accept(visitor);Names.AddRange(visitor.Tables);}
    }
    sealed class Names:TSqlFragmentVisitor
    {internal readonly List<string> Tables=new List<string>();public override void ExplicitVisit(NamedTableReference node){Tables.Add(node.SchemaObject.BaseIdentifier.Value);}}
    static List<string> DeleteTargets(string sql){IList<ParseError> errors;var script=new TSql160Parser(true).Parse(new StringReader(sql),out errors);var visitor=new Deletes();script.Accept(visitor);return visitor.Names;}
    sealed class ScriptStructure:TSqlFragmentVisitor
    {
        internal readonly List<int> Stages=new List<int>();internal int FirstDelete=int.MaxValue,ScopedDeletes;
        public override void ExplicitVisit(SelectStatement node){if(node.Into!=null)Stages.Add(node.StartOffset);base.ExplicitVisit(node);}
        public override void ExplicitVisit(DeleteStatement node){FirstDelete=Math.Min(FirstDelete,node.StartOffset);if(node.DeleteSpecification.WhereClause?.SearchCondition is ExistsPredicate)ScopedDeletes++;base.ExplicitVisit(node);}
    }
    static ScriptStructure Structure(string sql){IList<ParseError> errors;var script=new TSql160Parser(true).Parse(new StringReader(sql),out errors);var visitor=new ScriptStructure();script.Accept(visitor);return visitor;}
    static void Parse(string sql){IList<ParseError> errors;new TSql160Parser(true).Parse(new StringReader(sql),out errors);Check(errors.Count==0,"generated SQL parses: "+string.Join("; ",errors.Select(e=>e.Message)));}
}
