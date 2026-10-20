using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JoinAssist;

static class QueryContextRegressionTests
{
    static int count;
    static void Check(bool ok,string label) { count++; if(!ok)throw new Exception("Query context: "+label); }
    static CatalogSnapshot Catalog()
    {
        var catalog=new CatalogSnapshot();
        foreach(var pair in new[]{new{Id=1,Name="Orders",Columns=new[]{"Id","CustomerId","Name"}},new{Id=2,Name="Customers",Columns=new[]{"Id","Name"}},new{Id=3,Name="Items",Columns=new[]{"Id","OrderId","Name"}}})
        {
            var obj=new CatalogObject{Id=pair.Id,Name=pair.Name,Schema="dbo",Kind="U",BareAllowed=true,Loaded=true};
            obj.Columns.AddRange(pair.Columns.Select(n=>new ColumnShape{Name=n,TypeName="int"}));catalog.Objects.Add(obj);
        }
        return catalog;
    }
    static ForeignKey Key()
    {
        var key=new ForeignKey{Id=10,Name="FK_Orders_Customers",ChildId=1,ParentId=2,ChildSchema="dbo",ChildTable="Orders",ParentSchema="dbo",ParentTable="Customers"};
        key.Columns.Add(new KeyColumn{Ordinal=1,Child="CustomerId",Parent="Id"});return key;
    }
    static AutomaticTrigger Trigger(string sample)
    {
        int caret=sample.IndexOf('|');if(caret<0)throw new Exception("Missing caret in test");
        string sql=sample.Remove(caret,1);var trigger=AutomaticTrigger.TryCreate(sql,caret);
        Check(trigger!=null,"trigger: "+sample);return trigger;
    }
    static List<JoinProposal> Columns(string sample)
    {
        var trigger=Trigger(sample);
        Check(trigger.Mode==CompletionMode.Column||trigger.Mode==CompletionMode.Expression,"column mode: "+sample);
        new ShapeResolver(Catalog(),CancellationToken.None).ResolveAsync(trigger.Context,false).GetAwaiter().GetResult();
        var sources=trigger.Qualifier.Length==0?trigger.Context.Tables:ShapeResolver.MatchTables(trigger.Context.Tables.Concat(trigger.Context.OuterTables),trigger.Qualifier);
        return trigger.OrderMatches(CompletionEngine.ColumnItems(sources,true));
    }
    internal static void Run()
    {
        // Deliberately synthetic: reproduce unfinished earlier statements without production SQL/data.
        string earlier="-- Investigation notes\nSELECT TOP 100 * FROM Items ORDER BY Id DESC\nSELECT * FROM Items WHERE Name='example'\nSELECT * FROM Items WHERE\nSELECT * FROM Items INNER JOIN\n-- another unfinished statement\nSELECT * FROM Orders WHERE Id=14\n";
        Check(Columns(earlier+"SELECT * FROM Orders WHERE id|").Any(p=>p.SearchName=="Id"),"current unqualified columns survive earlier unfinished WHERE/JOIN");
        Check(Columns(earlier+"SELECT o.Na| FROM Orders o WHERE o.Id=1").Single().SearchName=="Name","current alias resolves after unfinished statements");
        string large=string.Concat(Enumerable.Range(0,80).Select(i=>"SELECT * FROM Items WHERE Id="+i+";\n"))+earlier;
        Check(Columns(large+"SELECT * FROM Orders WHERE id|").Any(p=>p.SearchName=="Id"),"large multi-statement script keeps current FROM");
        foreach(string sample in new[]{
            "SELECT o.Id,o.Na|, FROM Orders o WHERE o.Id=1",
            "SELECT o.Na| FROM Orders o WHERE o.Id=",
            "SELECT o.Na| FROM Orders o JOIN Customers c ON c.Id=",
            "SELECT o.Id FROM Orders o WHERE o.Na| AND o.Id=1",
            "SELECT o.Id FROM Orders o WHERE o.Id LIKE Na|",
            "SELECT o.Id FROM Orders o WHERE o.Id BETWEEN Na| AND 100",
            "SELECT DISTINCT Na| FROM Orders",
            "SELECT TOP 100 Na| FROM Orders",
            "SELECT DISTINCT TOP (100) Na| FROM Orders",
            "SELECT TOP (10) PERCENT WITH TIES Na| FROM Orders ORDER BY Id",
            "SELECT CASE WHEN Id>0 THEN Na| ELSE '' END FROM Orders",
            "SELECT * FROM Orders WHERE Id>=Na|",
            "SELECT * FROM Orders WHERE Id<>Na|",
            "SELECT * FROM Orders WHERE Id!=Na|",
            "WITH c AS (SELECT * FROM Orders) SELECT c.Na| FROM c WHERE c.Id=",
            "WITH c AS (SELECT CustomerId AS Buyer FROM Orders) SELECT c.Bu| FROM c WHERE c.Buyer="})
            Check(Columns(sample).Count>0,"columns with unfinished expressions/modifiers: "+sample);
        Check(Columns("SELECT o.Na| FROM Orders o; SELECT * FROM Items WHERE").Single().SearchName=="Name","unfinished later statement does not break current one");
        Check(Columns("SELECT * FROM Orders o WHERE EXISTS(SELECT 1 FROM Items i WHERE o.Na|)").Single().SearchName=="Name","valid correlated scope preserved");
        Check(Columns("SELECT * FROM Orders o JOIN (SELECT o.Na| FROM Items i) q ON 1=1").Count==0,"non-correlated derived scope never borrows outer alias");
        Check(Columns("SELECT * FROM Orders o JOIN (SELECT o.Na| FROM Items i WHERE i.Id=) q ON 1=1").Count==0,"recovered derived scope never borrows outer alias");
        Check(Columns("SELECT * FROM Orders o WHERE EXISTS(SELECT i.Na| FROM Items i WHERE i.Id=)").Single().SearchName=="Name","recover the actual nested FROM");
        Check(Columns("WITH bad AS (SELECT * FROM Items WHERE) SELECT o.Na| FROM Orders o WHERE o.Id=").Single().SearchName=="Name","invalid CTE preamble does not hide ordinary table aliases");
        Check(Trigger("SELECT * FROM Orders; UPDATE Items SET Na|=1").Mode==CompletionMode.UpdateColumn,"UPDATE has its own target context");
        Check(Columns("SELECT * FROM Items; DELETE FROM Orders WHERE Cu|").Single().SourceReference=="Orders","DELETE uses its target, never an earlier SELECT source");
        Check(Columns("-- SELECT * FROM Customers\nSELECT o.Na| FROM Orders o WHERE o.Id=").Single().SearchName=="Name","comment keywords cannot change recovery scope");
        Check(Columns("SELECT 'SELECT * FROM Customers' AS Text; SELECT o.Na| FROM Orders o WHERE o.Id=").Single().SearchName=="Name","literal keywords cannot change recovery scope");
        Check(Columns("WITH a AS (SELECT CustomerId FROM Orders), b AS (SELECT CustomerId AS Buyer FROM a) SELECT b.Bu| FROM b WHERE b.Buyer=").Single().SearchName=="Buyer","CTE chain survives tail recovery");
        foreach(string suffix in new[]{"WHERE o.Id=1","WHERE o.Id=","WHERE o.","GROUP BY o.Id HAVING COUNT(*)>0 ORDER BY o.Id","ORDER BY o.","WHERE EXISTS(SELECT 1 FROM Items i WHERE i.OrderId=o.Id)","WHERE o.Id=1 -- keep this comment\nORDER BY o.Id"})
        {
            string sample="SELECT * FROM Orders o INNER JOIN |\n"+suffix;
            var trigger=Trigger(sample);Check(trigger.Mode==CompletionMode.Join,"FK mode before tail: "+suffix);
            new ShapeResolver(Catalog(),CancellationToken.None).ResolveAsync(trigger.Context,true).GetAwaiter().GetResult();
            var proposal=JoinBuilder.Build(trigger.Context,new[]{Key()}).Single();
            int caret=sample.IndexOf('|');string sql=sample.Remove(caret,1);
            string result=sql.Remove(trigger.Context.ReplaceStart,trigger.Context.ReplaceLength).Insert(trigger.Context.ReplaceStart,trigger.Insertion(proposal));
            Check(result.EndsWith("\n"+suffix,StringComparison.Ordinal),"JOIN insertion preserves exact suffix and comments");
            Check(result.Contains("INNER JOIN Customers ON o.CustomerId = Customers.Id"),"real FK inserted before suffix");
        }
        foreach(string sample in new[]{"SELECT o.Id, FROM Orders o INNER JOIN Cu| WHERE o.Id=1","SELECT * FROM Orders o JOIN Customers c ON | WHERE o.Id=","SELECT * FROM Orders o LEFT JOIN |WHERE o.Id=1"})
        {
            var trigger=Trigger(sample);Check(trigger.Mode==CompletionMode.Join||trigger.Mode==CompletionMode.Predicate,"JOIN/ON with unfinished projection or suffix");
            new ShapeResolver(Catalog(),CancellationToken.None).ResolveAsync(trigger.Context,true).GetAwaiter().GetResult();
            Check(JoinBuilder.Build(trigger.Context,new[]{Key()}).Count==1,"relation available with existing tail");
        }
        Check(AutomaticTrigger.TryCreate("SELECT * FROM Orders WHERE 'Name'",32)==null,"strings remain excluded");
        Console.WriteLine("PASS: "+count+" query-context regressions (large/unfinished scripts, expression columns, JOIN/ON before existing clauses, exact suffix preservation).");
    }
}
