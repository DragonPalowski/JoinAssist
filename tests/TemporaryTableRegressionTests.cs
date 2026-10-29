using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JoinAssist;

static class TemporaryTableRegressionTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception("Temporary tables: "+message);}
    static string Definitions="DROP TABLE IF EXISTS #Old\nDROP TABLE IF EXISTS #New\n\nSELECT Details.*, Revisions.ListName\nINTO #Old\nFROM Details\nINNER JOIN Revisions ON Details.RevisionId = Revisions.Id AND Details.RevisionId = 159\n\nSELECT Details.*, Revisions.ListName\nINTO #New\nFROM Details\nINNER JOIN Revisions ON Details.RevisionId = Revisions.Id AND Details.RevisionId = 158\n\n";
    static AutomaticTrigger Trigger(string sample)
    {
        int caret=sample.IndexOf('|');string sql=sample.Remove(caret,1);
        var trigger=AutomaticTrigger.TryCreate(sql,caret);Check(trigger!=null,"trigger: "+sample);return trigger;
    }
    static CatalogSnapshot Catalog()
    {
        var catalog=new CatalogSnapshot();
        foreach(var row in new[]{new{Id=1,Name="Details",Columns=new[]{"Id","RevisionId","HealthServiceId","Price"}},new{Id=2,Name="Revisions",Columns=new[]{"Id","ListName"}}})
        {
            var obj=new CatalogObject{Id=row.Id,Schema="dbo",Name=row.Name,Kind="U",BareAllowed=true,Loaded=true};
            obj.Columns.AddRange(row.Columns.Select(n=>new ColumnShape{Name=n,TypeName="int",PrimaryKey=n=="Id",ForeignKey=n=="RevisionId"}));catalog.Objects.Add(obj);
        }
        return catalog;
    }
    internal static void Run()
    {
        foreach(string prior in new[]{"","SELECT * FROM Details WHERE\n","SELECT * FROM Details INNER JOIN\n","SELECT d.\n"})
        {
            string script=prior+Definitions+"SELECT * FROM #Old\nSELECT * FROM #New\nSELECT * FROM #Old\nINNER JOIN #ne|";
            var trigger=Trigger(script);
            Check(trigger.Context.Locals.Count==2 && trigger.Context.Locals.ContainsKey("#OLD") && trigger.Context.Locals.ContainsKey("#new"),"two SELECT INTO definitions survive unfinished neighboring statements");
            using(var db=new FakeDatabase())
            {
                var names=CompletionEngine.LoadAsync(db,trigger,CancellationToken.None).GetAwaiter().GetResult();
                var matches=trigger.OrderMatches(names);Check(matches.Count==1 && matches[0].TargetTable=="#New" && matches[0].ScriptOnly,"JOIN local name available from script only");
                Check(db.Commands.Count==0,"local name lookup never opens/reads database catalog");
                Check(trigger.Insertion(matches[0])=="INNER JOIN #New ","insert local JOIN name without inventing ON");
            }
            trigger=Trigger(prior+Definitions+"SELECT * FROM #ol|");
            var offline=CompletionEngine.LoadAsync(null,trigger,CancellationToken.None).GetAwaiter().GetResult();
            Check(trigger.OrderMatches(offline).Single().CompletionText=="#Old","FROM names work without a metadata connection");
            trigger=Trigger(prior+Definitions+"SELECT o.Pr| FROM #old o");
            new ShapeResolver(Catalog(),CancellationToken.None).ResolveAsync(trigger.Context,false).GetAwaiter().GetResult();
            var columns=CompletionEngine.ColumnItems(trigger.Context.Tables,false);
            Check(trigger.OrderMatches(columns).Single().SearchName=="Price","qualified star columns load from source catalog despite earlier incomplete SELECT");
            var shape=trigger.Context.Tables.Single().Columns;
            Check(shape.Select(c=>c.Name).SequenceEqual(new[]{"Id","RevisionId","HealthServiceId","Price","ListName"}),"qualified star plus explicit joined column retains projection order");
            Check(shape.All(c=>c.BaseObjectId==0 && c.PrimaryKey==null && c.ForeignKey==null),"SELECT INTO retains no fabricated PK/FK lineage");
            trigger=Trigger(prior+Definitions+"SELECT o.| FROM #Old o INNER JOIN #New n ON o.Id=n.Id");
            new ShapeResolver(Catalog(),CancellationToken.None).ResolveAsync(trigger.Context,false).GetAwaiter().GetResult();
            Check(CompletionEngine.ColumnItems(trigger.Context.Tables,false).Count==10,"both temporary sources resolve in JOIN scope");
        }
        using(var db=new FakeDatabase())
        {
            string definitions=Definitions.Replace("Details","Items").Replace("Revisions","Orders").Replace("RevisionId","OrderId").Replace("ListName","TenantId");
            var metadata=Trigger("SELECT * FROM Items WHERE\n"+definitions+"SELECT t.Or| FROM #Old t");
            var proposals=CompletionEngine.LoadAsync(db,metadata,CancellationToken.None).GetAwaiter().GetResult();
            Check(metadata.OrderMatches(proposals).Single().SearchName=="OrderId","full catalog reader and completion pipeline resolves qualified star after unfinished SQL");
            Check(db.Commands.Count>1 && db.Commands.All(c=>c.CommandText.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)),"column lookup reads only synthetic metadata and never executes SELECT INTO");
        }
        string declarations="SELECT * FROM Details WHERE\nCREATE TABLE #Known(Id int, Label nvarchar(30)); DECLARE @Rows TABLE(Code int);\n";
        var localColumn=Trigger(declarations+"SELECT t.La| FROM #Known t");
        var fallback=CompletionEngine.ScriptItemsAsync(localColumn,CancellationToken.None).GetAwaiter().GetResult();
        Check(localColumn.OrderMatches(fallback).Single().SearchName=="Label" && fallback.All(p=>p.ScriptOnly),"explicit local columns survive offline capture failure");
        Check(localColumn.Context.Tables.All(t=>t.Columns==null),"offline resolver does not poison metadata-backed source instances");
        var beforeFrom=Trigger(declarations+"SELECT #Known.La|");
        Check(beforeFrom.OrderMatches(CompletionEngine.ScriptItemsAsync(beforeFrom,CancellationToken.None).GetAwaiter().GetResult()).Single().SearchName=="Label","local qualifier before FROM is available offline");
        var mixed=Trigger(declarations+"SELECT La| FROM #Known t CROSS JOIN Details d");
        Check(mixed.OrderMatches(CompletionEngine.ScriptItemsAsync(mixed,CancellationToken.None).GetAwaiter().GetResult()).Single().CompletionText=="t.Label","offline fallback qualifies names next to unknown physical sources");
        var ambiguous=Trigger(declarations+"SELECT t.La| FROM #Known t CROSS JOIN Details t");
        Check(CompletionEngine.ScriptItemsAsync(ambiguous,CancellationToken.None).GetAwaiter().GetResult().Count==0,"ambiguous local/base alias never guesses offline");
        var star=Trigger(Definitions+"SELECT t.| FROM #Old t");
        Check(CompletionEngine.ScriptItemsAsync(star,CancellationToken.None).GetAwaiter().GetResult().Select(p=>p.SearchName).SequenceEqual(new[]{"ListName"}),"unknown star columns are not guessed offline; named projection stays available");
        var scope=LocalTables.Read(declarations+"\nGO\nSELECT * FROM #",declarations.Length+"\nGO\nSELECT * FROM #".Length);
        Check(scope.ContainsKey("#Known") && !scope.ContainsKey("@Rows"),"recovered temp survives GO while table variable expires");
        string changedDatabase="CREATE TABLE #Known(Id int);\nGO\nUSE OtherDb; SELECT * FROM Details WHERE\nSELECT * FROM #";
        Check(LocalTables.Read(changedDatabase,changedDatabase.Length).Count==0,"uncertain batch with USE clears prior database assumptions");
        string altered=declarations+"ALTER TABLE #Known ADD\nSELECT * FROM #";
        Check(!LocalTables.Read(altered,altered.Length).ContainsKey("#Known"),"malformed ALTER invalidates previous local shape");
        foreach(string unsafeSql in new[]{"IF 1=1 CREATE TABLE #Hidden(Id int);","BEGIN CREATE TABLE #Hidden(Id int); END;","CREATE PROCEDURE p AS CREATE TABLE #Hidden(Id int);","PRINT 'CREATE TABLE #Hidden(Id int)';","-- CREATE TABLE #Hidden(Id int)\n"})
        {
            string sql="SELECT * FROM Details WHERE\n"+unsafeSql+"\nSELECT * FROM #";
            Check(!LocalTables.Read(sql,sql.Length).ContainsKey("#Hidden"),"recovery never lifts hidden/conditional/text declarations: "+unsafeSql);
        }
        string future="SELECT * FROM #|; CREATE TABLE #Future(Id int);";
        Check(!Trigger(future).Context.Locals.ContainsKey("#Future"),"future declarations stay invisible");
        var unknown=Trigger("SELECT * FROM #missing|");
        Check(CompletionEngine.LoadAsync(null,unknown,CancellationToken.None).GetAwaiter().GetResult().Count==0 && unknown.Context.DiagnosticCode=="JA-LOCAL-01","unknown local gets a local diagnostic without a connection");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{CompletionEngine.ScriptItemsAsync(localColumn,cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}Check(stopped,"offline work respects cancellation");}
        Console.WriteLine("PASS: "+checks+" temporary-table regressions (SELECT INTO/star/JOIN, unfinished script recovery, offline names/columns, GO/DDL/scope safety and no fabricated FK).");
    }
}
