using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JoinAssist;

static class AllFeaturesTests
{
    static int count;
    static void Check(bool ok,string label){count++;if(!ok)throw new Exception("0.7 features: "+label);}
    static AutomaticTrigger Trigger(string sample)
    {
        int caret=sample.IndexOf('|');var t=AutomaticTrigger.TryCreate(sample.Remove(caret,1),caret);Check(t!=null,"trigger "+sample);return t;
    }
    static List<JoinProposal> Complete(string sample,FakeDatabase db=null,MetadataScope cache=null)
    {
        var t=Trigger(sample);bool own=db==null;db=db??new FakeDatabase();
        try{return t.OrderMatches(CompletionEngine.LoadAsync(db,t,CancellationToken.None,cache).GetAwaiter().GetResult());}finally{if(own)db.Dispose();}
    }
    internal static void Run()
    {
        var original=PreferenceStore.Current;
        try{PreferenceStore.Apply(new Preferences());Cache().GetAwaiter().GetResult();Dml();Contexts();Paths();Details();Settings();Performance();}
        finally{PreferenceStore.Apply(original);SqlAnalysis.ClearCache();}
        Console.WriteLine("PASS: "+count+" additional checks (cache isolation/refresh/concurrency, preferences, nested recovery, DML, FK paths, rich metadata and focused loading). No server or user settings accessed.");
    }
    static async Task Cache()
    {
        var owner=new object();var scope=MetadataCache.ScopeFor(owner,"server","db","session1");int loads=0;
        Func<Task<int>> load=()=>Task.FromResult(++loads);
        Check(await MetadataCache.ReadAsync(scope,"value",load,CancellationToken.None)==1,"first cache read");
        Check(await MetadataCache.ReadAsync(scope,"value",load,CancellationToken.None)==1 && loads==1,"cache hit");
        Check(ReferenceEquals(scope,MetadataCache.ScopeFor(owner,"server","db","session1")),"same native/session/db scope");
        Check(!ReferenceEquals(scope,MetadataCache.ScopeFor(new object(),"server","db","session1")),"different identity isolated");
        Check(!ReferenceEquals(scope,MetadataCache.ScopeFor(owner,"server","other","session1")),"database isolated");
        Check(!ReferenceEquals(scope,MetadataCache.ScopeFor(owner,"other","db","session1")),"server isolated");
        Check(!ReferenceEquals(scope,MetadataCache.ScopeFor(owner,"server","db","session2")),"reconnection isolated");
        MetadataCache.Invalidate(scope);Check(await MetadataCache.ReadAsync(scope,"value",load,CancellationToken.None)==2,"explicit refresh");
        scope.Expires=DateTime.UtcNow.AddSeconds(-1);Check(await MetadataCache.ReadAsync(scope,"value",load,CancellationToken.None)==3,"expiration");
        var start=new TaskCompletionSource<bool>();var finish=new TaskCompletionSource<int>();
        var running=MetadataCache.ReadAsync(scope,"flight",()=>{start.SetResult(true);return finish.Task;},CancellationToken.None);
        await start.Task;MetadataCache.Invalidate(scope);finish.SetResult(8);Check(await running==8,"in-flight can complete");
        Check(await MetadataCache.ReadAsync(scope,"flight",()=>Task.FromResult(9),CancellationToken.None)==9,"in-flight refresh cannot repopulate old value");
        MetadataCache.Invalidate(scope);loads=0;
        var concurrent=Enumerable.Range(0,8).Select(i=>MetadataCache.ReadAsync(scope,"shared",async()=>{Interlocked.Increment(ref loads);await Task.Yield();return 12;},CancellationToken.None));
        Check((await Task.WhenAll(concurrent)).All(i=>i==12) && loads==1,"concurrent cache reads share one load");
        bool failed=false;try{await MetadataCache.ReadAsync<int>(scope,"error",()=>{throw new IOException();},CancellationToken.None);}catch(IOException){failed=true;}
        Check(failed && await MetadataCache.ReadAsync(scope,"error",()=>Task.FromResult(15),CancellationToken.None)==15,"failed load not cached");
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();bool stopped=false;try{await MetadataCache.ReadAsync(scope,"shared",load,cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"cancelled hit respects cancellation");
        }
        using(var db=new FakeDatabase())
        {
            var shared=new MetadataScope();Complete("SELECT o.Cu| FROM Orders o",db,shared);int commands=db.Commands.Count;
            var second=Complete("SELECT o.Cu| FROM Orders o",db,shared);
            Check(commands==2 && db.Commands.Count==commands && second.Any(p=>p.SearchName=="CustomerId"),"catalog and object data reused");
            var catalog=await CatalogReader.ReadAsync(db,CancellationToken.None,shared);catalog.Objects[0].Name="mutated";
            var clean=await CatalogReader.ReadAsync(db,CancellationToken.None,shared);Check(clean.Objects[0].Name=="Orders","catalog clones prevent cache poisoning");
            MetadataCache.Invalidate(shared);Complete("SELECT o.Cu| FROM Orders o",db,shared);Check(db.Commands.Count==commands+2,"catalog and columns reread after refresh");
        }
    }
    static void Dml()
    {
        var deleted=Complete("delete from Orders where Cu|");
        Check(deleted.Any(p=>p.SearchName=="CustomerId"),"DELETE WHERE binds its target");
        Check(Complete("update Orders set TenantId=2 where Cu|").Any(p=>p.SearchName=="CustomerId"),"UPDATE WHERE binds its target");
        foreach(string sample in new[]{
            "DELETE FROM dbo.Orders WHERE Cu|",
            "DELETE Orders WHERE (Cu|)",
            "DELETE TOP (10) FROM Orders OUTPUT deleted.Id WHERE Cu|",
            "DELETE FROM Orders WITH (ROWLOCK) WHERE TenantId=1 AND Cu|",
            "DELETE FROM Orders WHERE EXISTS(SELECT 1 FROM Items WHERE OrderId=1) AND (Cu|)",
            "DELETE o FROM Orders o JOIN Items i ON o.Id=i.OrderId WHERE o.Cu|",
            "DELETE FROM o FROM Orders o JOIN Items i ON o.Id=i.OrderId WHERE o.Cu|",
            "DELETE o FROM Orders o JOIN Items i ON o.Id=i.OrderId WHERE Cu|",
            "UPDATE o SET TenantId=1 FROM Orders o JOIN Items i ON o.Id=i.OrderId WHERE o.Cu|",
            "UPDATE Orders SET TenantId=1 FROM Items i WHERE Cu|",
            "WITH target AS (SELECT CustomerId AS Buyer FROM Orders) DELETE FROM target WHERE Bu|",
            "WITH a AS (SELECT CustomerId FROM Orders), b AS (SELECT CustomerId AS Buyer FROM a) DELETE FROM b WHERE Bu|",
            "SELECT * FROM Customers WHERE\nDELETE FROM Orders WHERE Cu|\nSELECT * FROM Items WHERE",
            "SELECT * FROM Customers;\nGO\nDELETE FROM Orders WHERE Cu|; SELECT * FROM Items",
            "DELETE FROM Orders WHERE [Cu|stomerId]=1 OPTION (RECOMPILE)",
            "CREATE TABLE #Target (Id int IDENTITY, Name nvarchar(20), Total AS (1)); DELETE FROM #Target WHERE To|",
            "DECLARE @Target TABLE (idParent int, idProduct int); DELETE FROM @Target WHERE idp|"})
        {
            var t=Trigger(sample);Check(t.Mode==CompletionMode.Expression || t.Mode==CompletionMode.Column,"DML filters use ordinary read columns");
            Check(Complete(sample).Any(p=>p.Category=="Colonna"),"DML WHERE source recovery: "+sample);
        }
        Check(Complete("DELETE o FROM Orders o JOIN Items i ON o.Id=i.OrderId WHERE i.Or|").Single().SearchName=="OrderId","joined DELETE exposes joined source columns");
        Check(Complete("SELECT * FROM Customers c; DELETE FROM Orders WHERE c.Na|").Count==0,"DELETE does not inherit earlier aliases");
        Check(Complete("DELETE FROM Orders WHERE EXISTS(SELECT 1 FROM Items i WHERE i.Or|)").Single().SearchName=="OrderId","nested DELETE subquery keeps inner source");
        using(var db=new FakeDatabase{RichColumns=true})
            Check(Complete("DELETE FROM Orders WHERE I|",db).Any(p=>p.SearchName=="Id" && p.Column.Identity==true),"DELETE filter includes identity columns");
        string filter="DELETE FROM Orders WHERE Cust|omerId=3 AND TenantId=2; SELECT * FROM Items";
        var filterTrigger=Trigger(filter);string filterSql=filter.Replace("|","");
        string filterChanged=filterSql.Remove(filterTrigger.Context.ReplaceStart,filterTrigger.Context.ReplaceLength).Insert(filterTrigger.Context.ReplaceStart,"CustomerId");
        Check(filterChanged==filter.Replace("|",""),"DELETE column insertion preserves WHERE, trailing clauses and later statements");
        foreach(string sample in new[]{"INSERT INTO Orders (Cu|) VALUES (1)","INSERT Orders (TenantId, Cu|) VALUES (1,2)","INSERT INTO dbo.Orders (Cu|,TenantId) VALUES (1,2)"})
        {
            var t=Trigger(sample);Check(t.Mode==CompletionMode.InsertColumn,"INSERT column mode");var columns=Complete(sample);Check(columns.Any(p=>p.SearchName=="CustomerId") && columns.All(p=>p.SearchName!="TenantId" || !sample.Contains("TenantId")),"INSERT excludes other listed columns");
        }
        Check(Complete("INSERT INTO Orders (Id, |)").Select(p=>p.SearchName).OrderBy(n=>n).SequenceEqual(new[]{"CustomerId","TenantId"}),"INSERT empty item after comma");
        foreach(string sample in new[]{"UPDATE Orders SET Cu|=1 WHERE Id=2","UPDATE dbo.Orders SET TenantId=1, Cu|=2 WHERE Id=3","UPDATE o SET Cu|=2 FROM Orders o WHERE Id=3","UPDATE o SET o.Cu|=2 FROM Orders o WHERE Id=3"})
        {
            Check(Trigger(sample).Mode==CompletionMode.UpdateColumn,"UPDATE assignment mode");Check(Complete(sample).Any(p=>p.SearchName=="CustomerId"),"UPDATE target and alias binding");
        }
        Check(Complete("UPDATE Orders SET |, CustomerId=2 WHERE Id=3").All(p=>p.SearchName!="CustomerId"),"UPDATE excludes later assignments");
        Check(Complete("UPDATE Orders SET CustomerId=COALESCE(1,2), | WHERE Id=3").Select(p=>p.SearchName).OrderBy(n=>n).SequenceEqual(new[]{"Id","TenantId"}),"nested RHS comma does not split assignments");
        Check(Trigger("UPDATE Orders SET CustomerId=Cu| WHERE Id=1").Mode!=CompletionMode.UpdateColumn,"RHS never treated as assignment column");
        Check(Complete("CREATE TABLE #T (Id int IDENTITY(1,1), Name nvarchar(30), Total AS (1)); INSERT #T (|").Select(p=>p.SearchName).SequenceEqual(new[]{"Name"}),"local INSERT excludes identity/computed");
        Check(Complete("DECLARE @T TABLE (Id int, Name nvarchar(20)); UPDATE @T SET Na|=N'x'").Any(p=>p.SearchName=="Name"),"table variable UPDATE");
        string text="UPDATE Orders SET Cust|omerId=3 WHERE Id=1";var trigger=Trigger(text);string sql=text.Replace("|","");string changed=sql.Remove(trigger.Context.ReplaceStart,trigger.Context.ReplaceLength).Insert(trigger.Context.ReplaceStart,trigger.Insertion(new JoinProposal{CompletionText="CustomerId",SearchName="CustomerId"}));
        Check(changed=="UPDATE Orders SET CustomerId=3 WHERE Id=1","DML replaces full current token, keeps suffix");
    }
    static void Contexts()
    {
        foreach(string sample in new[]{
            "SELECT * FROM Orders o WHERE EXISTS (SELECT 1 FROM Items i WHERE o.Cu| AND i.OrderId=)",
            "SELECT (SELECT o.Cu| FROM Items i WHERE i.OrderId=) FROM Orders o",
            "SELECT * FROM Orders o CROSS APPLY (SELECT o.Cu| FROM Items i WHERE i.OrderId=) q",
            "SELECT * FROM Orders o OUTER APPLY (SELECT o.Cu| FROM Items i WHERE i.OrderId=) q",
            "WITH a AS (SELECT CustomerId FROM Orders), b AS (SELECT a.Cu| FROM a WHERE a.CustomerId=) SELECT * FROM b",
            "WITH a AS (SELECT CustomerId FROM Orders), b AS (SELECT CustomerId AS Buyer FROM a WHERE) SELECT b.Bu| FROM b WHERE b.Buyer=",
            "WITH c AS (SELECT CustomerId AS Buyer FROM Orders) SELECT Buyer FROM c UNION ALL SELECT c.Bu| FROM c WHERE c.Buyer=",
            "SELECT * FROM Orders o WHERE EXISTS(SELECT * FROM Items i WHERE EXISTS(SELECT 1 FROM Customers c WHERE o.Cu| AND c.Id=))"})
            Check(Complete(sample).Count>0,"nested/CTE/UNION recovery: "+sample);
        Check(Complete("SELECT * FROM Orders o JOIN (SELECT o.Cu| FROM Items i WHERE i.Id=) q ON 1=1").Count==0,"ordinary derived does not inherit aliases");
        Check(Complete("SELECT * FROM Orders o WHERE EXISTS(SELECT * FROM Items o WHERE o.Cu| AND o.Id=)").Count==0,"nearest alias shadows outer alias");
        Check(Complete("SELECT * FROM Orders o; SELECT * FROM Items i WHERE EXISTS(SELECT 1 FROM Customers c WHERE o.Cu| AND c.Id=)").Count==0,"no alias leak across statements");
        Check(Complete("SELECT * FROM Orders o CROSS APPLY (SELECT c.Na| FROM Items i) q CROSS APPLY (SELECT Name FROM Customers) c").Count==0,"APPLY cannot see a later source");
        Check(Complete("SELECT * FROM Orders o CROSS APPLY (SELECT q.Cu| FROM Items i) q").Count==0,"APPLY cannot see its own derived alias");
        Check(Complete("SELECT * FROM Orders o JOIN ((SELECT o.Cu| FROM Items i WHERE i.Id=)) q ON 1=1").Count==0,"parenthesized ordinary derived remains a correlation barrier");
    }
    static void Paths()
    {
        var t=Trigger("SELECT * FROM Items JOIN Cu|");using(var db=new FakeDatabase())
        {
            var all=CompletionEngine.LoadAsync(db,t,CancellationToken.None).GetAwaiter().GetResult();var path=t.OrderMatches(all).Single(p=>p.Steps!=null);
            string sql=path.Sql("INNER");Check(sql.Contains("INNER JOIN Orders ON Items.OrderId = Orders.Id") && sql.Contains("INNER JOIN Customers ON Orders.CustomerId = Customers.Id AND Orders.TenantId = Customers.TenantId"),"two-hop path uses complete real composite FK");
            Check(path.Caption.Contains("2 JOIN") && path.Caption.Contains("tramite Orders"),"path explicitly labelled and previewed");
            var catalog=CatalogReader.ReadAsync(db,CancellationToken.None).GetAwaiter().GetResult();var keys=MetadataReader.ReadAsync(db,t.Context,CancellationToken.None).GetAwaiter().GetResult();
            keys.Single(k=>k.Id==100).Disabled=true;Check(JoinPaths.Build(t.Context,keys,catalog).Count==0,"disabled edge excluded from paths");
            keys.Single(k=>k.Id==100).Disabled=false;keys.Single(k=>k.Id==100).Untrusted=true;
            Check(JoinPaths.Build(t.Context,keys,catalog).Single().Caption.Contains("non verificata"),"untrusted path warning retained");
        }
        var prefs=PreferenceStore.Current;prefs.JoinPaths=false;PreferenceStore.Apply(prefs);
        Check(Complete("SELECT * FROM Items JOIN Cu|").All(p=>p.Steps==null),"paths can be disabled");prefs.JoinPaths=true;PreferenceStore.Apply(prefs);
        Check(Complete("SELECT * FROM Items JOIN Customers ON |").All(p=>p.Steps==null),"ON never inserts intermediate tables");
    }
    static void Details()
    {
        Check(new ColumnShape{TypeName="nvarchar",MaxLength=100,Nullable=false,PrimaryKey=true,ForeignKey=true}.Description=="nvarchar(50) · NOT NULL · PK · FK","unicode character length and constraints");
        Check(new ColumnShape{TypeName="varchar",MaxLength=-1,Nullable=true}.Description=="varchar(max) · NULL","max length and nullable");
        Check(new ColumnShape{TypeName="decimal",Precision=18,Scale=4}.Description=="decimal(18,4)","decimal precision/scale");
        Check(new ColumnShape{TypeName="datetime2",Scale=3}.Description=="datetime2(3)","time scale");
        using(var db=new FakeDatabase{RichColumns=true})
        {
            var columns=Complete("SELECT c.Na| FROM Customers c",db);Check(columns.First().SearchName=="Name" && columns.First().Caption.Contains("nvarchar(50) · NULL"),"rich catalog metadata reaches popup");
            columns=Complete("SELECT o.Id| FROM Orders o",db);Check(columns.Single(p=>p.SearchName=="Id").Caption.Contains("NOT NULL · PK · IDENTITY"),"identity and PK reach popup");
            columns=Complete("INSERT Orders (|)",db);Check(columns.Select(p=>p.SearchName).SequenceEqual(new[]{"CustomerId"}),"catalog INSERT excludes identity/computed columns");
            columns=Complete("UPDATE Orders SET |",db);Check(columns.Select(p=>p.SearchName).SequenceEqual(new[]{"CustomerId"}),"catalog UPDATE excludes identity/computed columns");
        }
        string declaration="CREATE TABLE #T (Id int PRIMARY KEY, Label nvarchar(20) NOT NULL);";var local=LocalTables.Read(declaration,declaration.Length).Values.First();
        Check(local.Columns[0].PrimaryKey==true && local.Columns[0].Nullable==false,"local primary key details");
        Check(local.Columns[1].Description.Contains("20") && local.Columns[1].Nullable==false,"local size and nullability");
        var unknown=Trigger("SELECT x.Cu| FROM Orders o");using(var db=new FakeDatabase())CompletionEngine.LoadAsync(db,unknown,CancellationToken.None).GetAwaiter().GetResult();
        Check(unknown.Context.DiagnosticCode=="JA-SCOPE-01","missing alias diagnostic");
        string quiet;
        Check(AutomaticTrigger.TryCreate("SELECT 'unfinished",18,out quiet)==null && quiet==null,"typing inside strings is silent");
        Check(!Diagnostics.IsProblem("JoinAssist pronto.") && Diagnostics.IsProblem("[JA-METADATA-01] failure"),"diagnostic problem indicator is explicit");
        string report=Diagnostics.Report("[JA-SCOPE-01] secret-server secret-db SELECT secret-password");
        Check(report.Contains("JA-SCOPE-01") && !report.Contains("secret"),"diagnostic strips arbitrary SQL/identity text");
    }
    static void Settings()
    {
        string folder=Path.Combine(Path.GetTempPath(),"JoinAssist-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,"settings.json");
        try
        {
            var prefs=new Preferences{Automatic=false,AcceptEnter=false,DelayMilliseconds=350,CacheMinutes=5,Shortcut="Ctrl+Shift+J",AlwaysSchema=true,QuoteNames=true,AutomaticAliases=true,UppercaseKeywords=false,JoinPaths=false};
            PreferenceStore.Write(path,prefs);var restored=PreferenceStore.Load(path);
            Check(!restored.Automatic && !restored.AcceptEnter && restored.CacheMinutes==5 && restored.Shortcut=="Ctrl+Shift+J" && restored.DelayMilliseconds==350,"preferences round trip");
            PreferenceStore.Write(path,new Preferences());Check(PreferenceStore.Load(path).Automatic,"atomic replacement");
            prefs.DelayMilliseconds=0;bool rejected=false;try{PreferenceStore.Write(path,prefs);}catch(ArgumentException){rejected=true;}Check(rejected && PreferenceStore.Load(path).Automatic,"invalid settings preserve saved file");prefs.DelayMilliseconds=350;
            PreferenceStore.Apply(prefs);Check(SqlName.Format("Orders")=="[Orders]" && SqlName.Format("@T")=="@T","quoting configurable; variables remain valid");
            var results=Complete("SELECT * FROM Items JOIN Or|");Check(results.First(p=>!p.IsName).Sql("INNER").StartsWith("inner join [dbo].[Orders] as [j] on "),"schema aliases and keyword casing preferences");
            var copy=PreferenceStore.Current;copy.DelayMilliseconds=1000;Check(PreferenceStore.Current.DelayMilliseconds==350,"current preference returns isolated copy");
            File.WriteAllText(path,"{");bool malformed=false;try{PreferenceStore.Load(path);}catch(ArgumentException){malformed=true;}Check(malformed,"malformed JSON rejected");
        }
        finally{foreach(string file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);PreferenceStore.Apply(new Preferences());}
    }
    static void Performance()
    {
        using(var db=new FakeDatabase())
        {
            var rows=Complete("SELECT o.Cu| FROM Orders o JOIN Customers c ON c.Id=o.CustomerId JOIN Items i ON i.OrderId=o.Id",db);
            Check(rows.Any(p=>p.SearchName=="CustomerId") && db.Commands.Count==2,"qualified completion loads only one object's columns");
        }
        string sql="SELECT * FROM Orders WHERE Id=1";var tokens=SqlAnalysis.Tokens(sql);Check(ReferenceEquals(tokens,SqlAnalysis.Tokens(sql)),"tokenization reuses same snapshot");
        var first=SqlAnalysis.ParseIndex(sql);first.Scopes[0].Ctes.Clear();var second=SqlAnalysis.ParseIndex(sql);Check(!ReferenceEquals(first.Scopes[0],second.Scopes[0]),"cached parser returns independent scope graph");
        SqlAnalysis.ClearCache();Check(!ReferenceEquals(tokens,SqlAnalysis.Tokens(sql)),"analysis cache can be cleared");
        sql="CREATE TABLE #T (Id int);";var locals=LocalTables.Read(sql,sql.Length);locals["#T"].Columns[0].Name="mutated";
        Check(LocalTables.Read(sql,sql.Length)["#T"].Columns[0].Name=="Id","local shape cache returns independent objects");
    }
}
