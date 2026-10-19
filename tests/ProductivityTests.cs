using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using JoinAssist;
using Microsoft.SqlServer.TransactSql.ScriptDom;

static class ProductivityTests
{
    static int checks;
    static void Check(bool value,string label){checks++;if(!value)throw new Exception("0.9: "+label);}
    static AutomaticTrigger Trigger(string sample)
    {int caret=sample.IndexOf('|');var result=AutomaticTrigger.TryCreate(sample.Remove(caret,1),caret);Check(result!=null,"trigger: "+sample);return result;}
    static List<JoinProposal> Complete(string sample)
    {var trigger=Trigger(sample);using(var db=new FakeDatabase{RichColumns=true})return trigger.OrderMatches(CompletionEngine.LoadAsync(db,trigger,CancellationToken.None).GetAwaiter().GetResult());}
    static string Insert(string sample,JoinProposal item)
    {var trigger=Trigger(sample);string sql=sample.Replace("|","");return sql.Remove(trigger.Context.ReplaceStart,trigger.Context.ReplaceLength).Insert(trigger.Context.ReplaceStart,trigger.Insertion(item));}
    internal static void Run()
    {
        var old=PreferenceStore.Current;
        try{PreferenceStore.Apply(new Preferences());Predicates();Ranking();Expressions();Stars();Inspector();Incremental();}
        finally{PreferenceStore.Apply(old);SqlAnalysis.ClearCache();}
        Console.WriteLine("PASS: "+checks+" productivity checks (partial predicates, contextual ranking, expression types, explicit star expansion, metadata sheets, incremental lexer/parser equivalence). Synthetic database only.");
    }
    static void Predicates()
    {
        foreach(var sample in new[]{
            "SELECT * FROM Orders o JOIN Customers c ON o.Cu|",
            "SELECT * FROM Orders o JOIN Customers c ON o.Cu|stomerId WHERE c.Name='demo' ORDER BY o.Id",
            "SELECT * FROM Orders o JOIN Customers c ON o.CustomerId = c.I| WHERE o.Id=1",
            "SELECT * FROM Orders o JOIN Customers c ON o.Cu| -- keep this comment\nWHERE o.Id=",
            "SELECT * FROM Orders o JOIN Customers c ON c.I| WHERE o.Id=1",
            "SELECT * FROM Orders o JOIN Customers c ON [o].[Cu|] WHERE o.Id=1"})
        {
            var trigger=Trigger(sample);Check(trigger.Mode==CompletionMode.PartialPredicate,"partial ON recognized");
            var candidates=Complete(sample);var fk=candidates.First(p=>!p.IsName);
            string changed=Insert(sample,fk);
            Check(changed.Contains(sample.Contains("ON c.I")?"c.Id = o.CustomerId":"o.CustomerId = c.Id"),"typed side preserved; real key completes");
            Check(changed.Contains("o.TenantId = c.TenantId"),"composite FK remains complete");
            if(sample.Contains("WHERE"))Check(changed.Substring(changed.IndexOf("WHERE",StringComparison.Ordinal))==sample.Replace("|","").Substring(sample.IndexOf("WHERE",StringComparison.Ordinal)-1),"suffix remains byte-for-byte unchanged");
            Check(candidates[0]==fk,"FK precedes normal column fallback");
        }
        var normal=Complete("SELECT * FROM Orders o JOIN Customers c ON c.Na|").Single(p=>p.IsName && p.SearchName=="Name");
        Check(Insert("SELECT * FROM Orders o JOIN Customers c ON c.Na|",normal)=="SELECT * FROM Orders o JOIN Customers c ON c.Name","ordinary ON columns still work when no FK matches");
        Check(Trigger("SELECT * FROM Orders o JOIN Customers c ON o.Id=c.Id AND c.Na|").Mode==CompletionMode.Column,"does not replace existing AND predicates");
        Check(Trigger("SELECT * FROM Orders o JOIN Customers c ON o.Id=1 WHERE c.Na|").Mode==CompletionMode.Column,"does not borrow ON from earlier clause");
        Check(Complete("SELECT * FROM Orders o JOIN Customers c ON o.Cu| WHERE c.Id=2").All(p=>p.IsName || p.Steps==null),"ON never inserts intermediate tables");
        Check(Complete("SELECT * FROM Orders o JOIN Customers c ON o.[Na|").All(p=>p.IsName),"unclosed quoted identifier retains ordinary columns rather than guessing a FK");
        Check(Trigger("SELECT * FROM Orders o JOIN Customers c ON o.Cu|").BaseKey==Trigger("SELECT * FROM Orders o JOIN Customers c ON o.Cus|").BaseKey,"typing only the ON name prefix reuses its candidates");
        Check(Trigger("SELECT * FROM Orders o JOIN Customers c ON o.I|").BaseKey!=Trigger("SELECT * FROM Orders o JOIN Customers c ON c.I|").BaseKey,"changing ON qualifier reloads the correct column fallback");
        Check(Trigger("SELECT * FROM Orders o JOIN Customers c ON o.CustomerId=c.I|").BaseKey!=Trigger("SELECT * FROM Orders o JOIN Customers c ON o.TenantId=c.I|").BaseKey,"changing comparison side cannot reuse stale insertion text");
    }
    static void Ranking()
    {
        var trigger=Trigger("SELECT * FROM Orders o WHERE EXISTS(SELECT * FROM Customers c WHERE I|)");
        var columns=Complete("SELECT * FROM Orders o WHERE EXISTS(SELECT * FROM Customers c WHERE I|)");
        Check(columns.Where(c=>c.SearchName=="Id").First().SourceReference=="c","local scope before correlated outer column");
        var exact=new JoinProposal{CompletionText="I",SearchName="I",ContextRank=1000};
        Check(trigger.OrderMatches(columns.Concat(new[]{exact}))[0]==exact,"exact typed name beats contextual hints");
        trigger=Trigger("SELECT * FROM Orders o JOIN Customers c ON o.CustomerId=c.Id JOIN Items i ON i.OrderId=o.Id WHERE o.Id=1 AND N|");
        trigger.Context.Clause="ON";trigger.Context.FocusedReferences.Add("i");trigger.Context.FocusedReferences.Add("o");
        var key=new JoinProposal{CompletionText="NewKey",SearchName="NewKey",SourceReference="o",Column=new ColumnShape{TypeName="int",ForeignKey=true}};
        var name=new JoinProposal{CompletionText="Name",SearchName="Name",SourceReference="c",Column=new ColumnShape{TypeName="nvarchar"}};
        CompletionRanking.Apply(trigger,new List<JoinProposal>{name,key});Check(trigger.OrderMatches(new[]{name,key})[0]==key,"ON participants and key hints within matching prefix band");
        var qualified=Trigger("SELECT * FROM Orders o JOIN Customers c ON o.Id=c.Na| AND o.Id=1");
        Check(qualified.Context.Clause=="ON" && qualified.Context.FocusedReferences.Contains("o") && qualified.Context.FocusedReferences.Contains("c"),"real ON scope recorded");
        Check(Complete("SELECT * FROM Orders o JOIN Customers c ON o.Id=c.Na| AND o.Id=1").Single(p=>p.SearchName=="Name").ContextRank>=10,"comparison counterpart type is loaded and used in qualified column ranking");
    }
    static void Expressions()
    {
        string sql="WITH c AS (SELECT CAST(CustomerId AS bigint) AS Buyer, CONVERT(nvarchar(40),Id) AS Label, TRY_CAST(Id AS decimal(12,2)) AS Amount, TRY_CONVERT(date,Id) AS Day, CASE WHEN Id=1 THEN 1 ELSE 2 END AS Flag, COUNT(*) OVER() AS Total, ROW_NUMBER() OVER(ORDER BY Id) AS Position, COALESCE(CustomerId,Id) AS Fallback, CASE WHEN Id=1 THEN 1 ELSE N'xx' END AS Uncertain FROM Orders) SELECT c.| FROM c";
        var items=Complete(sql);
        foreach(var pair in new[]{new[]{"Buyer","bigint"},new[]{"Label","nvarchar(40)"},new[]{"Amount","decimal(12,2)"},new[]{"Day","date"},new[]{"Flag","int"},new[]{"Total","int"},new[]{"Position","bigint"},new[]{"Fallback","int"}})
        {var item=items.Single(p=>p.SearchName==pair[0]);Check(item.PopupDetail.Contains(pair[1]),"inferred output type: "+pair[0]);Check(item.Column.BaseObjectId==0 && item.Column.ForeignKey!=true,"computed expression never inherits FK lineage");}
        Check(items.Single(p=>p.SearchName=="Uncertain").PopupDetail.Contains("tipo non disponibile"),"mixed CASE type remains explicitly unknown");
        Check(Complete("WITH a AS(SELECT CAST(Id AS bigint) AS KeyId FROM Orders), b AS(SELECT KeyId AS Renamed FROM a) SELECT b.Re| FROM b").Single().PopupDetail.Contains("bigint"),"type survives CTE chain and rename");
        Check(Complete("SELECT q.La| FROM (SELECT CONVERT(nvarchar(40),Id) AS Label FROM Orders) q").Single().PopupDetail.Contains("nvarchar(40)"),"derived-table expression type");
        Check(Complete("WITH q AS(SELECT 0.01 AS Fraction FROM Orders) SELECT q.Fr| FROM q").Single().PopupDetail.Contains("decimal(2,2)"),"decimal literal uses minimum precision and scale");
    }
    static StarExpansion Star(string sample)
    {int caret=sample.IndexOf('|');return StarExpansion.Create(sample.Replace("|",""),caret);}
    static void Stars()
    {
        using(var db=new FakeDatabase{RichColumns=true})
        {
            var expansion=Star("SELECT o.*| FROM Orders o WHERE o.Id=1");
            var choices=expansion.LoadAsync(db,CancellationToken.None).GetAwaiter().GetResult();
            Check(choices.Select(c=>c.Sql).SequenceEqual(new[]{"o.Id","o.CustomerId","o.TenantId"}),"qualified star uses exact catalog order and existing alias");
            choices[1].Selected=false;Check(StarExpansion.Text(choices)=="o.Id, o.TenantId","user can choose columns");
            string original="SELECT o.* FROM Orders o WHERE o.Id=1";
            string changed=original.Remove(expansion.Context.ReplaceStart,expansion.Context.ReplaceLength).Insert(expansion.Context.ReplaceStart,StarExpansion.Text(choices));
            Check(changed=="SELECT o.Id, o.TenantId FROM Orders o WHERE o.Id=1","expansion replaces only selected star span");
        }
        using(var db=new FakeDatabase())
        {
            var all=Star("SELECT *| FROM Orders o JOIN Customers c ON c.Id=o.CustomerId").LoadAsync(db,CancellationToken.None).GetAwaiter().GetResult();
            Check(all.Count==6 && all[0].Sql=="o.Id" && all[3].Sql=="c.Id","bare star includes all sources; duplicate names qualified");
            var cte=Star("WITH a AS(SELECT CustomerId AS Buyer FROM Orders) SELECT a.*| FROM a").LoadAsync(db,CancellationToken.None).GetAwaiter().GetResult();
            Check(cte.Single().Sql=="a.Buyer","CTE star uses exposed output names");
        }
        bool rejected=false;try{Star("SELECT COUNT(*|) FROM Orders");}catch(InvalidOperationException){rejected=true;}Check(rejected,"COUNT star never expanded");
        rejected=false;try{using(var db=new FakeDatabase())Star("SELECT *| FROM #Unknown").LoadAsync(db,CancellationToken.None).GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}Check(rejected,"unknown source cannot silently produce partial expansion");
        rejected=false;try{StarExpansion.Text(new[]{new ColumnChoice{Sql="o.Id",Selected=false}});}catch(InvalidOperationException){rejected=true;}Check(rejected,"empty selection blocked");
    }
    static void Inspector()
    {
        foreach(var sample in new[]{"SELECT o.Customer|Id FROM Orders o","SELECT * FROM Ord|ers","SELECT * FROM dbo.Ord|ers"})
        {
            int caret=sample.IndexOf('|');string sql=sample.Replace("|","");
            using(var db=new FakeDatabase{RichColumns=true})
            {
                var info=SchemaInspector.LoadAsync(db,SqlAnalysis.ReadScope(sql,caret),SchemaInspector.ReferenceAt(sql,caret),CancellationToken.None).GetAwaiter().GetResult();
                Check(info.Count==1,"inspect table/schema/alias.column");
                Check(info[0].Columns.Count==(sample.Contains("Customer|")?1:3),"column focus or all table columns");
                Check(info[0].Relationships.Contains("FK_Orders_Customers") && info[0].Relationships.Contains("CustomerId"),"actual relationship details");
                Check(db.Commands.All(c=>c.CommandText.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)),"inspector executes only metadata SELECTs");
            }
        }
        using(var db=new FakeDatabase())
        {
            string trailing="SELECT * FROM Orders    ";var atEnd=SchemaInspector.LoadAsync(db,SqlAnalysis.ReadScope(trailing,trailing.Length),new string[0],CancellationToken.None).GetAwaiter().GetResult();
            Check(atEnd.Single().Name=="Orders","whitespace after a complete SELECT retains its real reference");
            string sql="WITH q AS(SELECT CAST(Id AS bigint) AS DerivedId FROM Orders) SELECT * FROM q";
            var info=SchemaInspector.LoadAsync(db,SqlAnalysis.ReadScope(sql,sql.Length-1),new[]{"q"},CancellationToken.None).GetAwaiter().GetResult().Single();
            Check(info.Columns.Single().Description.Contains("bigint") && !info.Relationships.Contains("FK_Orders_Customers"),"computed CTE schema has type and no invented relationship");
        }
    }
    static void Incremental()
    {
        SqlAnalysis.ClearCache();string sql=string.Join("\r\n",Enumerable.Range(0,35).Select(i=>"SELECT Id FROM Orders WHERE Id="+i+";"));
        var first=SqlAnalysis.ParseIndex(sql);long parsed=IncrementalSql.ParsedCharacters;
        var changed=sql.Replace("Id=34;","Id=35;");var second=SqlAnalysis.ParseIndex(changed);
        Check(first.Scopes.Count==35 && second.Scopes.Count==35,"statement scopes preserved across incremental parse");
        Check(IncrementalSql.ParsedCharacters-parsed<100,"unchanged independent SELECT statements are reused");
        var fresh=new TSql160Parser(true);IList<ParseError> errors;
        var ast=fresh.Parse(new StringReader(changed),out errors);var raw=new ScopeIndex();ast.Accept(raw);
        Check(second.Scopes.Select(s=>s.Query.StartOffset).SequenceEqual(raw.Scopes.Select(s=>s.Query.StartOffset)),"incremental AST offsets match full parse");
        sql="SELECT N'abc', [a b] FROM Orders;\r\n-- sample\r\nGO\r\nSELECT Id FROM Orders WHERE Id=2;";
        IncrementalSql.Clear();IncrementalSql.Tokens(sql);var random=new Random(93);string[] edits={"x"," ","'","\r\n","/","-","*","[", "]","GO",";"};
        for(int i=0;i<160;i++)
        {
            int pos=random.Next(sql.Length+1);
            if(i%3==0 && sql.Length>0){pos=Math.Min(pos,sql.Length-1);sql=sql.Remove(pos,1);}else sql=sql.Insert(pos,edits[random.Next(edits.Length)]);
            var actual=IncrementalSql.Tokens(sql);var expected=fresh.GetTokenStream(new StringReader(sql),out errors);
            Check(actual.Select(t=>t.TokenType+"|"+t.Offset+"|"+t.Line+"|"+t.Column+"|"+t.Text).SequenceEqual(expected.Select(t=>t.TokenType+"|"+t.Offset+"|"+t.Line+"|"+t.Column+"|"+t.Text)),"incremental lexer agrees with full lexer after edit "+i);
        }
        IncrementalSql.Clear();sql=new string(' ',10000)+"SELECT Id FROM Orders";IncrementalSql.Tokens(sql);long work=IncrementalSql.LexedCharacters;IncrementalSql.Tokens(sql+" ");
        Check(IncrementalSql.ReusedTokens>0 && IncrementalSql.LexedCharacters-work<100,"typing at end reuses lexical prefix");
        var batch=SqlAnalysis.ParseIndex("SELECT * FROM Orders;\r\nGO 2\r\nWITH q AS(SELECT Id FROM Customers) SELECT * FROM q;");Check(batch.Scopes.Count==3,"GO count and CTE preserve separate scopes");
        foreach(string stateful in new[]{"SELECT Id FROM Orders; BEGIN TRY SELECT Id FROM Customers; END TRY BEGIN CATCH SELECT Id FROM Items; END CATCH;", "SET QUOTED_IDENTIFIER OFF;\r\nGO\r\nSELECT \"text\" FROM Orders; SELECT Id FROM Customers;"})
        {
            var actual=SqlAnalysis.ParseIndex(stateful);var expected=new ScopeIndex();fresh.Parse(new StringReader(stateful),out errors).Accept(expected);
            Check(actual.Scopes.Select(s=>s.Query.StartOffset).SequenceEqual(expected.Scopes.Select(s=>s.Query.StartOffset)),"procedural/stateful fallback agrees with full parser");
        }
    }
}
