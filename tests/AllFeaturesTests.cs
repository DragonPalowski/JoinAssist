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
        try{PreferenceStore.Apply(new Preferences());Cache().GetAwaiter().GetResult();Dml();Inserts();Contexts();Paths();SharedKeys();Details();Settings();Performance();}
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
        Check(Complete("CREATE TABLE #T (Id int IDENTITY(1,1), Name nvarchar(30), Total AS (1)); INSERT #T (|").Where(p=>!p.IsInsertTemplate).Select(p=>p.SearchName).SequenceEqual(new[]{"Name"}),"local INSERT excludes identity/computed");
        Check(Complete("DECLARE @T TABLE (Id int, Name nvarchar(20)); UPDATE @T SET Na|=N'x'").Any(p=>p.SearchName=="Name"),"table variable UPDATE");
        string text="UPDATE Orders SET Cust|omerId=3 WHERE Id=1";var trigger=Trigger(text);string sql=text.Replace("|","");string changed=sql.Remove(trigger.Context.ReplaceStart,trigger.Context.ReplaceLength).Insert(trigger.Context.ReplaceStart,trigger.Insertion(new JoinProposal{CompletionText="CustomerId",SearchName="CustomerId"}));
        Check(changed=="UPDATE Orders SET CustomerId=3 WHERE Id=1","DML replaces full current token, keeps suffix");
    }
    static void Inserts()
    {
        string declaration="CREATE TABLE #Target (Zebra int, Id int IDENTITY, Alpha nvarchar(20) DEFAULT 'x', Total AS (1), Stamp rowversion, LastValue int); ";
        var t=Trigger(declaration+"INSERT INTO #Target |");
        Check(t.Mode==CompletionMode.InsertTemplate && t.Label=="Struttura INSERT","INSERT skeleton trigger after target space");
        var proposal=Complete(declaration+"INSERT INTO #Target |").Single();
        string nl=Environment.NewLine;
        string expected="("+nl+"    Zebra,"+nl+"    Alpha,"+nl+"    LastValue"+nl+")"+nl+"VALUES ("+nl+"    @Zebra,"+nl+"    @Alpha,"+nl+"    @LastValue"+nl+");";
        Check(proposal.IsInsertTemplate && proposal.CompletionText==expected,"INSERT skeleton keeps ordinal order, defaults and aligned placeholders");
        Check(proposal.CompletionText.Substring(proposal.CaretOffset.Value).StartsWith("@Zebra",StringComparison.Ordinal),"caret goes to first value");
        System.Collections.Generic.IList<Microsoft.SqlServer.TransactSql.ScriptDom.ParseError> insertErrors;
        var parsed=new Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser(true).Parse(new StringReader("INSERT #Target "+expected),out insertErrors) as Microsoft.SqlServer.TransactSql.ScriptDom.TSqlScript;
        var spec=((Microsoft.SqlServer.TransactSql.ScriptDom.InsertStatement)parsed.Batches[0].Statements[0]).InsertSpecification;
        Check(insertErrors.Count==0 && spec.Columns.Select(c=>c.MultiPartIdentifier.Identifiers.Last().Value).SequenceEqual(new[]{"Zebra","Alpha","LastValue"}) &&
            ((Microsoft.SqlServer.TransactSql.ScriptDom.ValuesInsertSource)spec.InsertSource).RowValues[0].ColumnValues.Count==3,"generated SQL parses as INSERT with aligned columns/values");
        Check(t.Matches(proposal) && t.Insertion(proposal)==expected && t.InsertionCaret(proposal,expected)==proposal.CaretOffset,"template insertion contract");
        foreach(string sample in new[]{"INSERT Orders |","INSERT INTO dbo.Orders |","INSERT INTO [dbo].[Orders] |","SELECT * FROM Items WHERE\nINSERT INTO Orders |","INSERT Orders |; SELECT * FROM Items"})
        {
            using(var db=new FakeDatabase{RichColumns=true})
            {
                var items=Complete(sample,db);Check(items.Single().IsInsertTemplate && items[0].CompletionText.Contains("@CustomerId"),"catalog INSERT structure: "+sample);
                Check(db.Commands.Count==2 && db.Commands.All(c=>c.CommandText.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)),"INSERT uses only catalog/column metadata");
            }
        }
        Check(!Complete("INSERT Orders |;",new FakeDatabase{RichColumns=true}).Single().CompletionText.EndsWith(";",StringComparison.Ordinal),"existing semicolon preserved without duplication");
        foreach(string sample in new[]{declaration+"INSERT #Target (|)",declaration+"INSERT #Target (Zebra, |) VALUES (1,2)",declaration+"INSERT #Target (|, LastValue)"})
        {
            var items=Complete(sample);var fill=items.First();Check(fill.IsInsertTemplate && fill.SearchName=="Inserisci colonne mancanti","fill list precedes individual columns");
            Check(!fill.CompletionText.Contains("Id") && !fill.CompletionText.Contains("Stamp") && !fill.CompletionText.Contains("Total") && !fill.CompletionText.Contains("VALUES"),"fill changes only writable column names");
            if(sample.Contains("Zebra, |"))Check(fill.CompletionText=="Alpha, LastValue","fill skips prior assigned column");
            if(sample.Contains("|, LastValue"))Check(fill.CompletionText=="Zebra, Alpha","fill skips later assigned column");
            Check(items.Any(p=>!p.IsInsertTemplate && p.Category=="Colonna"),"individual column proposals remain available");
        }
        Check(Complete(declaration+"INSERT #Target (Al|)").All(p=>!p.IsInsertTemplate) && Complete(declaration+"INSERT #Target (Al|)").First().SearchName=="Alpha","typing a column prefix keeps ordinary completion");
        Check(Complete(declaration+"INSERT #Target (Zebra, Alpha, LastValue, |)").All(p=>!p.IsInsertTemplate),"no template when every writable column is assigned");
        Check(Complete("CREATE TABLE #Only (Id int IDENTITY, Total AS (1)); INSERT #Only |").Count==0,"all-generated target has no empty template");
        using(var db=new FakeDatabase())Check(Complete("INSERT Orders |",db).Count==0,"unknown writeability suppresses template");
        using(var db=new FakeDatabase{RichColumns=true})Check(Complete("WITH Orders AS (SELECT Name FROM Customers) INSERT Orders |",db).Count==0,"CTE shadow never uses physical table writeability");
        foreach(string sample in new[]{"INSERT Orders|","INSERT Orders | SELECT Id FROM Items","INSERT Orders | VALUES (1)","INSERT Orders | OUTPUT inserted.Id","INSERT Orders | DEFAULT VALUES","INSERT db.dbo.Orders |","INSERT TOP (10) Orders |","INSERT Orders (|CustomerId)","INSERT Orders VALUES (|)","SELECT 'INSERT Orders |'","-- INSERT Orders |"})
        {
            int position=sample.IndexOf('|');var trigger=AutomaticTrigger.TryCreate(sample.Replace("|",""),position);
            Check(trigger==null || trigger.Mode!=CompletionMode.InsertTemplate && !trigger.FillInsertColumns,"do not scaffold existing syntax/partial names/comments: "+sample);
        }
        PreferenceStore.Apply(new Preferences{InsertIncludeDefaults=false});
        Check(!Complete(declaration+"INSERT #Target |").Single().CompletionText.Contains("Alpha"),"default columns optional in full template");
        Check(!Complete(declaration+"INSERT #Target (|)").First().CompletionText.Contains("Alpha") && Complete(declaration+"INSERT #Target (Al|)").Any(p=>p.SearchName=="Alpha"),"default option affects bulk action only");
        PreferenceStore.Apply(new Preferences{UppercaseKeywords=false,QuoteNames=true});
        Check(Complete(declaration+"insert #Target |").Single().CompletionText.Contains("values ("+nl+"    @Zebra") && Complete(declaration+"insert #Target |").Single().CompletionText.Contains("[Zebra]"),"template honors quote/case preferences");
        PreferenceStore.Apply(new Preferences());
        string unusual="CREATE TABLE #Odd ([Order Date] int,[Order-Date] int,[1st] int,[select] int);\n  INSERT #Odd |";
        var odd=Complete(unusual).Single();
        Check(odd.CompletionText.Contains("[Order Date]") && odd.CompletionText.Contains("@Order_Date,") && odd.CompletionText.Contains("@Order_Date_2,") && odd.CompletionText.Contains("@col_1st,") && odd.CompletionText.Contains("@select"),"safe unique parameter names and necessary delimiters");
        Check(!odd.CompletionText.Contains("\r") && odd.CompletionText.Contains("\n      [Order-Date]") && odd.CompletionText.Contains("\n  VALUES"),"LF line endings and existing indentation preserved");
        string generated="CREATE TABLE #History (Id int, StartTime datetime2 GENERATED ALWAYS AS ROW START NOT NULL, EndTime datetime2 GENERATED ALWAYS AS ROW END NOT NULL, PERIOD FOR SYSTEM_TIME(StartTime,EndTime)); INSERT #History |";
        Check(Complete(generated).Single().CompletionText.Contains("@Id") && !Complete(generated).Single().CompletionText.Contains("StartTime"),"generated-always columns excluded");
        string useDiagnostic;
        Check(AutomaticTrigger.TryCreate("USE OtherDb; INSERT Orders ",27,out useDiagnostic)==null && useDiagnostic!=null,"INSERT refuses metadata after USE in same batch");
        Check(Trigger("USE OtherDb\nGO\nINSERT Orders |").Mode==CompletionMode.InsertTemplate,"USE in earlier batch does not suppress INSERT template");
    }
    static void Contexts()
    {
        string unfinishedProjection="SELECT\n    Ord|\n    Customers.Name,\n    Items.Id,\n    TenantId\nFROM Items\nINNER JOIN Orders ON Items.OrderId=Orders.Id\nLEFT JOIN Customers ON Orders.CustomerId=Customers.Id\nWHERE Items.Id IS NULL OR Orders.TenantId IS NULL\nORDER BY Items.Id DESC";
        var references=Complete(unfinishedProjection);
        Check(references.Any(p=>p.Category=="Riferimento" && p.CompletionText=="Orders"),"table reference before a qualified SELECT item without its comma");
        var referenceTrigger=Trigger(unfinishedProjection);
        string originalSql=unfinishedProjection.Replace("|","");
        var chosen=references.Single(p=>p.Category=="Riferimento" && p.CompletionText=="Orders");
        string inserted=originalSql.Remove(referenceTrigger.Context.ReplaceStart,referenceTrigger.Context.ReplaceLength).Insert(referenceTrigger.Context.ReplaceStart,referenceTrigger.Insertion(chosen));
        Check(inserted==unfinishedProjection.Replace("Ord|","Orders"),"reference completion replaces only the typed name and preserves every following clause");
        Check(Complete(inserted.Replace("Orders\n","Orders.|\n")).Any(p=>p.SearchName=="CustomerId"),"column proposals remain available after the completed reference and dot");
        foreach(string sample in new[]{
            "SELECT Ord| Customers.Name FROM Orders JOIN Customers ON Orders.CustomerId=Customers.Id",
            "select\tord|\n Customers.Name, Items.Id from Items join Orders on Items.OrderId=Orders.Id left join Customers on Orders.CustomerId=Customers.Id where Items.Id=1 order by Orders.Id",
            "SELECT Customers.Name, Ord| Items.Id FROM Items JOIN Orders ON Items.OrderId=Orders.Id JOIN Customers ON Orders.CustomerId=Customers.Id",
            "SELECT TOP (100) Ord| Customers.Name FROM Orders JOIN Customers ON Orders.CustomerId=Customers.Id",
            "SELECT Ord|, Customers.Name FROM Orders JOIN Customers ON Orders.CustomerId=Customers.Id",
            "SELECT * FROM Items; SELECT Ord| Customers.Name FROM Orders JOIN Customers ON Orders.CustomerId=Customers.Id; SELECT * FROM Items"})
            Check(Complete(sample).Any(p=>p.Category=="Riferimento" && p.CompletionText=="Orders"),"unfinished projection exposes its own table references: "+sample);
        foreach(string sample in new[]{
            "SELECT ord| c.Name FROM Orders orderSource JOIN Customers c ON orderSource.CustomerId=c.Id",
            "WITH orderSource AS (SELECT Id, CustomerId FROM Orders) SELECT ord| c.Name FROM orderSource JOIN Customers c ON orderSource.CustomerId=c.Id",
            "SELECT ord| c.Name FROM (SELECT Id, CustomerId FROM Orders) orderSource JOIN Customers c ON orderSource.CustomerId=c.Id"})
            Check(Complete(sample).Any(p=>p.Category=="Riferimento" && p.CompletionText=="orderSource"),"unfinished projection retains aliases, CTEs and derived references: "+sample);
        Check(Complete("SELECT ord| c.Name FROM Orders orders JOIN Items orderLines ON orders.Id=orderLines.OrderId JOIN Customers orderCustomers ON orders.CustomerId=orderCustomers.Id")
            .Count(p=>p.Category=="Riferimento")==3,"all joined references matching a prefix are offered");
        Check(Complete("SELECT Ord|; SELECT * FROM Orders").Count==0,"a SELECT without FROM cannot borrow a following statement's tables");
        Check(Complete("SELECT Ord|; SELECT Ord Customers.Name FROM Orders JOIN Customers ON Orders.CustomerId=Customers.Id").Count==0,"partial AST recovery cannot switch to a later SELECT");
        Check(Complete("SELECT ord| c.Name FROM Items JOIN Customers c ON Items.Id=c.Id; SELECT * FROM Orders").All(p=>p.CompletionText!="Orders"),"projection recovery cannot borrow a following statement's source");
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
    static ForeignKey SharedKey(int id,int child,string name,int parent,string parentName,params string[] pairs)
    {
        var key=new ForeignKey{Id=id,Name="FK_"+name+"_"+parentName,ChildId=child,ParentId=parent,ChildSchema="dbo",ChildTable=name,ParentSchema="dbo",ParentTable=parentName};
        for(int i=0;i<pairs.Length;i+=2)key.Columns.Add(new KeyColumn{Ordinal=i/2+1,Child=pairs[i],Parent=pairs[i+1]});return key;
    }
    static void SharedKeys()
    {
        var context=new QueryContext();var source=new TableUse{ObjectId=1,Schema="dbo",Name="WorkDetails_tb"};context.Tables.Add(source);
        var a=SharedKey(10,1,"WorkDetails_tb",2,"Services_tb","idService","idService");a.Untrusted=true;
        var b=SharedKey(11,3,"ServicesRev_tb",2,"Services_tb","serviceKey","idService");
        var keys=new List<ForeignKey>{a,b};
        var p=JoinPaths.SharedKeys(context,keys).Single();
        Check(p.Sql("INNER")=="INNER JOIN ServicesRev_tb ON WorkDetails_tb.idService = ServicesRev_tb.serviceKey","common parent yields one JOIN with actual child-column names");
        Check(p.Steps==null && p.SharedKeyParent=="dbo.Services_tb" && p.PopupDetail.StartsWith("1 JOIN · FK comuni",StringComparison.Ordinal) && p.PopupDetail.Contains("non FK diretta"),"derived equality is never labelled as a direct FK");
        Check(p.Caption.Contains(a.Name) && p.Caption.Contains(b.Name) && p.Warning.Contains("non verificate"),"actual support constraints and untrusted warning remain visible");
        Check(p.Sql("LEFT").StartsWith("LEFT JOIN",StringComparison.Ordinal) && !p.Sql("LEFT").Contains("JOIN Services_tb"),"selected kind belongs to the single proposed JOIN; no implicit parent insertion");
        a.Untrusted=false;Check(!JoinPaths.SharedKeys(context,keys).Single().Warning.Contains("non verificate"),"trusted constraints have no untrusted warning");
        b.Disabled=true;Check(JoinPaths.SharedKeys(context,keys).Count==0,"disabled destination FK cannot support inference");b.Disabled=false;
        a.Disabled=true;Check(JoinPaths.SharedKeys(context,keys).Count==0,"disabled source FK cannot support inference");a.Disabled=false;
        b.ParentId=20;Check(JoinPaths.SharedKeys(context,keys).Count==0,"identical referenced column names in different parent objects do not match");b.ParentId=2;
        b.Columns[0].Parent="OtherKey";Check(JoinPaths.SharedKeys(context,keys).Count==0,"different referenced keys in same parent do not match");b.Columns[0].Parent="idService";
        a.Columns.Add(new KeyColumn{Ordinal=2,Child="TenantId",Parent="TenantId"});
        Check(JoinPaths.SharedKeys(context,keys).Count==0,"composite key cannot be matched by partial intersection");
        b.Columns[0].Ordinal=2;b.Columns.Add(new KeyColumn{Ordinal=1,Child="Tenant",Parent="TenantId"});
        Check(JoinPaths.SharedKeys(context,keys).Single().Predicate=="WorkDetails_tb.idService = ServicesRev_tb.serviceKey AND WorkDetails_tb.TenantId = ServicesRev_tb.Tenant","full composite key pairs by referenced column despite different ordinals");
        source.Alias="d";
        Check(JoinPaths.SharedKeys(context,keys).Single().Predicate.StartsWith("d.idService =",StringComparison.Ordinal),"existing source alias retained");
        var map=new SourceMap{ObjectId=1};map.Columns["idService"]="Service";map.Columns["TenantId"]="Tenant";
        source.ObjectId=0;source.IsVirtual=true;source.Name="detailCte";source.Sources.Add(map);
        Check(JoinPaths.SharedKeys(context,keys).Single().Predicate.StartsWith("d.Service =",StringComparison.Ordinal),"direct CTE lineage exposes renamed child key");
        map.Columns.Remove("TenantId");Check(JoinPaths.SharedKeys(context,keys).Count==0,"missing/computed CTE key column suppresses incomplete equality");map.Columns["TenantId"]="Tenant";
        context.CteTargets.Add(new TableUse{Name="revisions",IsVirtual=true});var targetMap=new SourceMap{ObjectId=3};targetMap.Columns["serviceKey"]="Service";targetMap.Columns["Tenant"]="Tenant";context.CteTargets[0].Sources.Add(targetMap);
        Check(JoinPaths.SharedKeys(context,keys).Any(x=>x.Target=="revisions" && x.Predicate=="d.Service = revisions.Service AND d.Tenant = revisions.Tenant"),"CTE target with complete lineage uses exposed key columns");
        context.CteTargets.Clear();context.Tables.Clear();source=new TableUse{ObjectId=1,Schema="dbo",Name="WorkDetails_tb"};context.Tables.Add(source);
        context.Ctes["ServicesRev_tb"]=null;
        Check(JoinPaths.SharedKeys(context,keys).Single().Target=="dbo.ServicesRev_tb","CTE shadow forces physical target schema");context.Ctes.Clear();
        PreferenceStore.Apply(new Preferences{QuoteNames=true,AlwaysSchema=true,AutomaticAliases=true,UppercaseKeywords=false});
        p=JoinPaths.SharedKeys(context,keys).Single();
        Check(p.Sql("INNER").StartsWith("inner join [dbo].[ServicesRev_tb] as [j] on",StringComparison.Ordinal) && p.Predicate.Contains("[j].[serviceKey]"),"inferred candidate honors schema, quoting, alias and casing preferences");
        PreferenceStore.Apply(new Preferences());
        context.PredicateOnly=true;context.LastJoinTarget=new TableUse{ObjectId=3,Name="ServicesRev_tb"};
        Check(JoinPaths.SharedKeys(context,keys).Count==0,"ON completion remains based on direct FK, not an inferred sibling relationship");context.PredicateOnly=false;
        context.Tables.Add(new TableUse{ObjectId=3,Name="ServicesRev_tb"});
        Check(JoinPaths.SharedKeys(context,keys).Count==0,"already-used destination is not added again");context.Tables.RemoveAt(1);
        var bad=SharedKey(12,3,"ServicesRev_tb",2,"Services_tb","serviceKey","idService","Tenant","idService");
        Check(JoinPaths.SharedKeys(context,new[]{a,bad}).Count==0,"duplicate referenced columns do not create a candidate");
        // Real FK candidates first, then single inferred JOIN, then two JOIN alternatives.
        var catalog=new CatalogSnapshot();foreach(var entry in new[]{new CatalogObject{Id=1,Schema="dbo",Name="WorkDetails_tb"},new CatalogObject{Id=2,Schema="dbo",Name="Services_tb"},new CatalogObject{Id=3,Schema="dbo",Name="ServicesRev_tb"},new CatalogObject{Id=4,Schema="dbo",Name="Branches_tb"}}){entry.Kind="U";entry.BareAllowed=true;catalog.Objects.Add(entry);}
        keys.Add(SharedKey(13,1,"WorkDetails_tb",4,"Branches_tb","BranchId","Id"));keys.Add(SharedKey(14,3,"ServicesRev_tb",4,"Branches_tb","Branch","Id"));
        var trigger=Trigger("SELECT * FROM WorkDetails_tb JOIN ServicesRev|");trigger.Context.Tables[0].ObjectId=1;
        var items=trigger.OrderMatches(CompletionEngine.BuildJoinItems(catalog,trigger,keys));
        Check(items.First().SharedKeyParent=="dbo.Services_tb" && items.First().Steps==null,"matching parent entity ranks ahead of generic shared branch and longer paths");
        Check(items.Any(x=>x.Steps!=null) && items.First().ContextRank<items.First(x=>x.Steps!=null).ContextRank,"two-JOIN alternatives retained below single inferred JOIN");
        Check(items.First().ChoiceIdentity!=items.First(x=>x.Steps!=null && x.ForeignKeyName==items.First().ForeignKeyName).ChoiceIdentity,"choice history distinguishes inferred equality from corresponding two-hop path");
        var direct=SharedKey(15,1,"WorkDetails_tb",3,"ServicesRev_tb","idService","serviceKey");direct.Columns.Add(new KeyColumn{Ordinal=2,Child="TenantId",Parent="Tenant"});keys.Add(direct);
        items=trigger.OrderMatches(CompletionEngine.BuildJoinItems(catalog,trigger,keys));
        Check(items.First().SharedKeyParent==null && items.First().ForeignKeyName==direct.Name,"actual direct FK ranks first");
        Check(items.Count(x=>x.Sql("INNER")==items[0].Sql("INNER"))==1,"actual direct FK suppresses duplicate inferred SQL");
        // End-to-end metadata loading: the destination FK is only visible on the second hop.
        using(var db=new FakeDatabase{SharedKeys=true})
        {
            items=Complete("SELECT * FROM Orders JOIN CustomersRev|",db);
            p=items.First();
            Check(p.SharedKeyParent=="crm.Customers" && p.Predicate=="Orders.CustomerId = CustomersRev.BuyerId AND Orders.TenantId = CustomersRev.TenantId","catalog/metadata/resolver/engine produce shared composite-key candidate");
            Check(items.Any(x=>x.Steps!=null) && db.Commands.Count(c=>c.CommandText.Contains("FROM sys.foreign_keys AS fk"))==2,"support FK fetched through parent; original path retained");
            Check(db.Commands.All(c=>c.CommandText.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)),"inference only reads metadata; no edited SQL executed");
        }
        PreferenceStore.Apply(new Preferences{JoinPaths=false});
        using(var db=new FakeDatabase{SharedKeys=true})
        {
            items=Complete("SELECT * FROM Orders JOIN CustomersRev|",db);
            Check(items.All(x=>x.SharedKeyParent==null && x.Steps==null) && items.Any(x=>x.IsName),"existing optional path setting disables inferred joins but retains table completion");
            Check(db.Commands.Count(c=>c.CommandText.Contains("FROM sys.foreign_keys AS fk"))==1,"disabled path setting avoids extra metadata round trip");
        }
        PreferenceStore.Apply(new Preferences());
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
            columns=Complete("INSERT Orders (|)",db);Check(columns.Where(p=>!p.IsInsertTemplate).Select(p=>p.SearchName).SequenceEqual(new[]{"CustomerId"}),"catalog INSERT excludes identity/computed columns");
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
            var prefs=new Preferences{Automatic=false,AcceptEnter=false,DelayMilliseconds=350,CacheMinutes=5,Shortcut="Ctrl+Shift+J",AlwaysSchema=true,QuoteNames=true,AutomaticAliases=true,UppercaseKeywords=false,JoinPaths=false,InsertIncludeDefaults=false};
            PreferenceStore.Write(path,prefs);var restored=PreferenceStore.Load(path);
            Check(!restored.Automatic && !restored.AcceptEnter && restored.CacheMinutes==5 && restored.Shortcut=="Ctrl+Shift+J" && restored.DelayMilliseconds==350,"preferences round trip");
            Check(!restored.InsertIncludeDefaults,"INSERT default inclusion option persists");
            File.WriteAllText(path,"{}");Check(PreferenceStore.Load(path).InsertIncludeDefaults,"existing preferences include default columns unless explicitly excluded");
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
