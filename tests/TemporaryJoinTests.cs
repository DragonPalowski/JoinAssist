using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using JoinAssist;

static class TemporaryJoinTests
{
    static int checks;
    static void Check(bool value,string name){checks++;if(!value)throw new Exception("Temporary JOIN: "+name);}
    static AutomaticTrigger Trigger(string sample)
    {int at=sample.IndexOf('|');var trigger=AutomaticTrigger.TryCreate(sample.Remove(at,1),at);Check(trigger!=null,"trigger: "+sample);return trigger;}
    static CatalogSnapshot Catalog()
    {
        var catalog=new CatalogSnapshot();var work=new CatalogObject{Id=1,Schema="dbo",Name="Work",Kind="U",Loaded=true,BareAllowed=true};
        work.Columns.AddRange(new[]{new ColumnShape{Name="Id",TypeName="int",PrimaryKey=true},new ColumnShape{Name="TenantId",TypeName="int",PrimaryKey=true},new ColumnShape{Name="PersonId",TypeName="int",ForeignKey=true},new ColumnShape{Name="Label",TypeName="nvarchar(30)"}});
        var people=new CatalogObject{Id=2,Schema="dbo",Name="People",Kind="U",Loaded=true,BareAllowed=true};people.Columns.Add(new ColumnShape{Name="Id",TypeName="int",PrimaryKey=true});
        catalog.Objects.AddRange(new[]{work,people});return catalog;
    }
    static ForeignKey Key()
    {var key=new ForeignKey{Id=10,Name="FK_Work_People",ChildId=1,ParentId=2};key.Columns.Add(new KeyColumn{Ordinal=1,Child="PersonId",Parent="Id"});return key;}
    static List<JoinProposal> Infer(string sql,ForeignKey[] keys=null)
    {
        var trigger=Trigger(sql);var catalog=Catalog();new ShapeResolver(catalog,CancellationToken.None).ResolveAsync(trigger.Context,true).GetAwaiter().GetResult();
        var targets=TemporaryJoins.TargetsAsync(trigger,catalog,CancellationToken.None).GetAwaiter().GetResult();
        return TemporaryJoins.Build(trigger,catalog,targets,keys??new[]{Key()},CancellationToken.None);
    }
    internal static void Run()
    {
        var saved=PreferenceStore.Current;
        try{var preferences=saved.Copy();preferences.TemporaryJoins=true;PreferenceStore.Apply(preferences);Cases();}
        finally{PreferenceStore.Apply(saved);}
        Console.WriteLine("PASS: "+checks+" temporary JOIN inference checks (arbitrary declared/projected locals, origins/keys, type compatibility, aliases, offline fallback, opt-out and no inherited constraints). Synthetic metadata only.");
    }
    static void Cases()
    {
        string snapshot="SELECT * INTO #copy FROM Work WHERE PersonId=7;\n";
        var items=Infer(snapshot+"SELECT * FROM Work LEFT JOIN #co|\nWHERE Work.PersonId=7\nSELECT * FROM People");
        Check(items.Count>0 && items[0].TemporaryJoinBasis.StartsWith("PK origine",StringComparison.Ordinal) && items[0].Predicate=="Work.Id = #copy.Id AND Work.TenantId = #copy.TenantId","reported filtered snapshot prefers complete original composite PK");
        Check(items.All(p=>p.TemporaryJoinBasis!=null && p.PopupDetail.Contains("da verificare")) && !items[0].ScriptOnly,"inferred metadata-backed candidates visibly require review");
        var trigger=Trigger(snapshot+"SELECT * FROM Work LEFT JOIN #co|\nWHERE Work.PersonId=7\nSELECT * FROM People");string text=snapshot+"SELECT * FROM Work LEFT JOIN #co\nWHERE Work.PersonId=7\nSELECT * FROM People";
        string result=text.Remove(trigger.Context.ReplaceStart,trigger.Context.ReplaceLength).Insert(trigger.Context.ReplaceStart,trigger.Insertion(items[0]));
        Check(result.Contains("LEFT JOIN #copy ON Work.Id = #copy.Id AND Work.TenantId = #copy.TenantId") && result.EndsWith("\nWHERE Work.PersonId=7\nSELECT * FROM People",StringComparison.Ordinal),"complete LEFT JOIN inserted without altering surrounding clauses");
        string declared="CREATE TABLE #anything(Id int, TenantId int, Label nvarchar(60));\n";
        items=Infer(declared+"SELECT * FROM Work JOIN #any|");
        Check(items.Any(p=>p.Predicate=="Work.Id = #anything.Id AND Work.TenantId = #anything.TenantId" && p.TemporaryJoinBasis.Contains("chiave completa")),"arbitrary CREATE TABLE works without SELECT INTO origin");
        Check(items.Any(p=>p.Predicate=="Work.Label = #anything.Label" && p.TemporaryJoinBasis=="nomi e tipi compatibili"),"non-key columns remain explicit lower-ranked alternatives");
        string locals="CREATE TABLE #a(Id int PRIMARY KEY, Label nvarchar(20)); CREATE TABLE #b(Id bigint, Label nvarchar(40));\n";
        trigger=Trigger(locals+"SELECT * FROM #a JOIN #b|");items=CompletionEngine.LoadAsync(null,trigger,CancellationToken.None).GetAwaiter().GetResult();
        var inferred=trigger.OrderMatches(items).Where(p=>p.TemporaryJoinBasis!=null).ToList();
        Check(inferred.Count==2 && inferred.All(p=>p.ScriptOnly) && inferred[0].Predicate=="#a.Id = #b.Id","two declared locals infer typed predicates offline and prioritize declared key");
        Check(items.Any(p=>p.IsName && p.TargetTable=="#b"),"ordinary name-only insertion remains available");
        foreach(string sample in new[]{"DECLARE @rows TABLE(Id int); CREATE TABLE #other(Id bigint); SELECT * FROM @rows JOIN #ot|", "CREATE TABLE #other(Id bigint); DECLARE @rows TABLE(Id int); SELECT * FROM #other JOIN @ro|"})
        {
            trigger=Trigger(sample);var variables=CompletionEngine.LoadAsync(null,trigger,CancellationToken.None).GetAwaiter().GetResult();
            Check(variables.Any(p=>p.TemporaryJoinBasis!=null && p.ScriptOnly && p.Predicate.Contains("@rows.Id")),"table-variable inference in either direction without connection");
        }
        trigger=Trigger("CREATE TABLE #a(Id int, LeftOnly int); CREATE TABLE #b(Id int); SELECT * FROM #a a JOIN #b b ON a.Left|");
        var columnFallback=CompletionEngine.ScriptItemsAsync(trigger,CancellationToken.None).GetAwaiter().GetResult();
        Check(trigger.OrderMatches(columnFallback).Any(p=>p.IsName && p.SearchName=="LeftOnly"),"unmatched inferred predicates never hide ordinary local columns in partial ON offline");
        string wide=string.Join(",",Enumerable.Range(0,40).Select(i=>"C"+i+" int").Concat(new[]{"ZId int"}));
        trigger=Trigger("CREATE TABLE #a("+wide+"); CREATE TABLE #b("+wide+"); SELECT * FROM #a a JOIN #b b ON a.Z|");
        var wideItems=CompletionEngine.ScriptItemsAsync(trigger,CancellationToken.None).GetAwaiter().GetResult();
        Check(trigger.OrderMatches(wideItems).Any(p=>p.TemporaryJoinBasis!=null && p.Predicate=="a.ZId = b.ZId"),"partial ON prefix searches beyond bounded default equality alternatives");
        using(var db=new FakeDatabase{RichColumns=true})
        {
            trigger=Trigger("SELECT * INTO #snap FROM Items; SELECT * FROM Items LEFT JOIN #sn|\nWHERE Items.Id=1");
            var loaded=CompletionEngine.LoadAsync(db,trigger,CancellationToken.None).GetAwaiter().GetResult();
            Check(trigger.OrderMatches(loaded).First().TemporaryJoinBasis.StartsWith("PK origine",StringComparison.Ordinal),"real completion pipeline reads metadata for local target and ranks inferred PK before name fallback");
            Check(trigger.Context.DiagnosticCode!="JA-FK-01","successful inferred list is not reported as missing FK candidates");
            Check(db.Commands.Count>1 && db.Commands.All(c=>c.CommandText.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)) && db.Commands.All(c=>!c.CommandText.Contains("#snap")),"only catalog SELECTs reach connection, never user definitions or data");
        }
        using(var db=new FakeDatabase{RichColumns=true})
        {
            trigger=Trigger("SELECT OrderId AS OwnerId INTO #child FROM Items; SELECT Id AS ParentKey INTO #parent FROM Orders; SELECT * FROM #child c JOIN #par|");
            var loaded=CompletionEngine.LoadAsync(db,trigger,CancellationToken.None).GetAwaiter().GetResult();
            Check(loaded.Any(p=>p.TemporaryJoinBasis!=null && p.TemporaryJoinBasis.Contains("FK di origine") && p.Predicate=="c.OwnerId = #parent.ParentKey" && !p.ScriptOnly),"two SELECT INTO sources use original FK metadata even though neither has an inherited FK map");
            Check(db.Commands.Any(c=>c.CommandText.Contains("sys.foreign_keys") && c.Parameters.Cast<System.Data.Common.DbParameter>().Any(p=>Convert.ToInt32(p.Value)==3)),"read FK metadata for snapshot origin IDs");
        }
        items=Infer(snapshot+"SELECT * FROM #copy t JOIN Wo|");Check(items.Any(p=>p.Predicate=="t.Id = Work.Id AND t.TenantId = Work.TenantId"),"temporary source can join persistent target");
        items=Infer(snapshot+"SELECT * FROM Work w JOIN #copy d ON w.I|");Check(items.Any(p=>p.Predicate=="w.Id = d.Id AND w.TenantId = d.TenantId"),"partial ON retains existing aliases");
        items=Infer("SELECT Id AS CopyId, TenantId AS CopyTenant INTO #renamed FROM Work; SELECT * FROM Work w JOIN #rena|");
        Check(items.Any(p=>p.Predicate=="w.Id = #renamed.CopyId AND w.TenantId = #renamed.CopyTenant" && p.TemporaryJoinBasis.StartsWith("PK origine",StringComparison.Ordinal)),"direct renamed projections map original composite key");
        string copied="SELECT PersonId AS OwnerKey INTO #owners FROM Work; SELECT * FROM People p JOIN #ow|";
        items=Infer(copied);Check(items.Any(p=>p.Predicate=="p.Id = #owners.OwnerKey" && p.TemporaryJoinBasis.Contains("FK di origine")),"copied renamed FK columns can use real origin constraint");
        var disabled=Key();disabled.Disabled=true;Check(Infer(copied,new[]{disabled}).Count==0,"disabled origin FK does not create a proposal");
        Check(Infer(copied,new ForeignKey[0]).Count==0,"unrelated renamed columns do not invent a constraint");
        Check(Infer("SELECT CAST(PersonId AS varchar(20)) AS OwnerKey INTO #owners FROM Work; SELECT * FROM People p JOIN #ow|").Count==0,"converted expressions lose original FK identity");
        items=Infer("SELECT Id INTO #partial FROM Work; SELECT * FROM Work JOIN #part|");
        Check(items.Count==1 && items.All(p=>p.TemporaryJoinBasis=="nomi e tipi compatibili"),"missing composite-key columns never produce a partial-key claim");
        items=Infer("CREATE TABLE #wrong(Id nvarchar(20), TenantId xml, Label image); SELECT * FROM Work JOIN #wr|");Check(items.Count==0,"incompatible and unsupported equality types produce no ON");
        items=Infer("CREATE TABLE #dup(Id int, id int); SELECT * FROM Work JOIN #du|");Check(items.Count==0,"case-ambiguous output columns are excluded");
        trigger=Trigger(snapshot+"SELECT c.Id| FROM #copy c");var catalog=Catalog();new ShapeResolver(catalog,CancellationToken.None).ResolveAsync(trigger.Context,false).GetAwaiter().GetResult();
        var source=trigger.Context.Tables.Single();Check(source.Maps.Count()==0 && source.Columns.All(c=>c.BaseObjectId==0 && c.PrimaryKey==null && c.ForeignKey==null),"snapshot origins never become actual key constraints or FK maps");
        Check(source.Columns.Any(c=>c.SnapshotObjectId==1 && c.SnapshotColumn=="Id"),"separate descriptive origin is retained");
        items=Infer("SELECT Id INTO #union FROM Work UNION ALL SELECT Id FROM Work; CREATE TABLE #typed(Id int); SELECT * FROM #union u JOIN #typ|");
        Check(items.Count>0 && items.All(p=>!p.ScriptOnly),"a projection that loses origin IDs still requires connection validation when resolved using catalog types");
        items=Infer(snapshot+"SELECT * FROM #copy JOIN #co|");Check(items.All(p=>p.Alias=="j" && p.Predicate.Contains("j.")),"self-join creates only the necessary distinct target alias");
        Check(Infer("CREATE TABLE #gone(Id int); DROP TABLE #gone; SELECT * FROM Work JOIN #go|").Count==0,"dropped local cannot receive inferred predicate");
        Check(Infer("SELECT * FROM Work JOIN #future|; CREATE TABLE #future(Id int);").Count==0,"future declarations stay unavailable");
        trigger=Trigger("CREATE TABLE #typed(Id int); SELECT * FROM Work JOIN #typ|");
        var fallback=CompletionEngine.ScriptItemsAsync(trigger,CancellationToken.None).GetAwaiter().GetResult();
        Check(fallback.All(p=>p.IsName) && trigger.Context.Tables.All(t=>t.Columns==null),"offline fallback never guesses physical source columns or poisons later resolution");
        foreach(var pair in new[]{new[]{"int","BIGINT"},new[]{"decimal(10,2)","numeric(14,3)"},new[]{"NVARCHAR (20)","varchar(40)"},new[]{"date","datetime2"},new[]{"uniqueidentifier","uniqueidentifier"}})
            Check(TemporaryJoins.Compatible(new ColumnShape{TypeName=pair[0]},new ColumnShape{TypeName=pair[1]}),"supported equality families: "+pair[0]);
        foreach(string type in new[]{"xml","text","ntext","image","geography","sql_variant",null})Check(!TemporaryJoins.Compatible(new ColumnShape{TypeName=type},new ColumnShape{TypeName=type}),"do not guess equality for "+type);
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{InferCancelled(snapshot+"SELECT * FROM Work JOIN #co|",cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"metadata resolution honors cancellation");}
        var preferences=PreferenceStore.Current;preferences.TemporaryJoins=false;PreferenceStore.Apply(preferences);
        trigger=Trigger(locals+"SELECT * FROM #a JOIN #b|");Check(CompletionEngine.LoadAsync(null,trigger,CancellationToken.None).GetAwaiter().GetResult().All(p=>p.IsName),"opt-out restores name-only local completion");
        Check(!trigger.Matches(inferred[0]),"turning option off invalidates a previously shown inferred candidate");
        string file=Path.Combine(Path.GetTempPath(),"ja-temp-join-"+Guid.NewGuid().ToString("N")+".json");
        try{File.WriteAllText(file,"{}");Check(PreferenceStore.Load(file).TemporaryJoins,"older preferences inherit enabled default");PreferenceStore.Write(file,preferences);Check(!PreferenceStore.Load(file).TemporaryJoins,"opt-out persists");}
        finally{if(File.Exists(file))File.Delete(file);}
    }
    static void InferCancelled(string sample,CancellationToken cancel)
    {var trigger=Trigger(sample);new ShapeResolver(Catalog(),cancel).ResolveAsync(trigger.Context,true).GetAwaiter().GetResult();}
}
