using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using JoinAssist;

static class PersonalRuleTests
{
    static int count;
    static void Check(bool ok,string label){count++;if(!ok)throw new Exception("Personal rules: "+label);}
    static PersonalRule Table(){return new PersonalRule{Name="Recent rows",Schema="dbo",Table="Orders",Top=100,Order=RuleOrder.FirstColumn};}
    static PersonalRule Snippet(){return new PersonalRule{Kind=PersonalRuleKind.Snippet,Name="Order lookup",Shortcut="selOrders",Sql="SELECT * FROM Orders WHERE Id=$cursor$;"};}
    static void Set(params PersonalRule[] rules){PreferenceStore.Apply(new Preferences{Rules=rules.ToList()});}
    static List<JoinProposal> Complete(string sample)
    {
        int caret=sample.IndexOf('|');if(caret<0)caret=sample.Length;else sample=sample.Remove(caret,1);
        var t=AutomaticTrigger.TryCreate(sample,caret);Check(t!=null,"trigger "+sample);
        using(var db=new FakeDatabase())return t.OrderMatches(CompletionEngine.LoadAsync(db,t,CancellationToken.None).GetAwaiter().GetResult());
    }
    internal static void Run()
    {
        var saved=PreferenceStore.Current;try{Transforms();Snippets();Scopes();Persistence();}finally{PreferenceStore.Apply(saved);}
        Console.WriteLine("PASS: "+count+" personal-rule checks (scopes, simple SELECT transforms, shortcuts, exact spans, persistence/import and no execution).");
    }
    static void Transforms()
    {
        var rule=Table();Set(rule);
        var p=Complete("SELECT * FROM Ord|").First();
        Check(p.IsPersonalRule && p.CompletionText=="SELECT TOP 100 * FROM Orders ORDER BY 1 DESC","one table acceptance prepares TOP and order");
        Check(p.RuleReplaceStart==0 && p.RuleReplaceLength=="SELECT * FROM Ord".Length,"whole SELECT replaces only original statement span");
        Check(Complete("SELECT * FROM dbo.Or|").First().CompletionText=="SELECT TOP 100 * FROM dbo.Orders ORDER BY 1 DESC","explicit schema retained");
        Check(Complete("SELECT * FROM Orders |").Single().CompletionText=="SELECT TOP 100 * FROM Orders ORDER BY 1 DESC","rule after exact table and space");
        Check(Complete("select * from orders |").Single().CompletionText=="select TOP 100 * from orders ORDER BY 1 DESC","complete table casing resolves against catalog without altering typed spelling");
        Check(Complete("SELECT TOP 7 * FROM Or| WHERE Id=2").First().CompletionText=="SELECT TOP 7 * FROM Orders WHERE Id=2 ORDER BY 1 DESC","existing TOP and filter preserved");
        Check(Complete("SELECT * FROM Or| ORDER BY Id ASC").First().CompletionText=="SELECT TOP 100 * FROM Orders ORDER BY Id ASC","existing order not overwritten");
        Check(Complete("SELECT TOP 7 * FROM Or| ORDER BY Id ASC").All(x=>!x.IsPersonalRule),"no no-op proposal when TOP and order already exist");
        string sample="SELECT * FROM Items;\n  SELECT * FROM Or| o WHERE Id=2; -- keep\nSELECT * FROM Customers;";
        p=Complete(sample).First(x=>x.IsPersonalRule);string original=sample.Replace("|","");string result=original.Remove(p.RuleReplaceStart,p.RuleReplaceLength).Insert(p.RuleReplaceStart,p.CompletionText);
        Check(result=="SELECT * FROM Items;\n  SELECT TOP 100 * FROM Orders o WHERE Id=2 ORDER BY 1 DESC; -- keep\nSELECT * FROM Customers;","only current statement edited; alias, comments and later SQL retained");
        foreach(string query in new[]{"SELECT DISTINCT * FROM Or|","SELECT Id FROM Or|","SELECT * INTO #x FROM Or|","SELECT * FROM Or| o JOIN Items i ON 1=1","SELECT * FROM Or| UNION SELECT * FROM Items","WITH c AS (SELECT * FROM Orders) SELECT * FROM Or|","SELECT * FROM (SELECT * FROM Or|) d"})
            Check(Complete(query).All(x=>!x.IsPersonalRule),"unsupported query gets no transformation: "+query);
        rule.Order=RuleOrder.NamedColumn;rule.OrderColumn="CustomerId";Set(rule);
        Check(Complete("SELECT * FROM Or|").First().CompletionText.EndsWith("ORDER BY CustomerId DESC",StringComparison.Ordinal),"named order column validated against metadata");
        rule.OrderColumn="Missing";Set(rule);Check(Complete("SELECT * FROM Or|").All(x=>!x.IsPersonalRule),"missing order column never invented");
        rule.OrderColumn="CustomerId";rule.Descending=false;rule.Top=0;Set(rule);Check(Complete("SELECT * FROM Or|").First().CompletionText=="SELECT * FROM Orders ORDER BY CustomerId ASC","optional TOP and ascending order");
        PreferenceStore.Apply(new Preferences{UppercaseKeywords=false,Rules=new List<PersonalRule>{Table()}});
        Check(Complete("select * from Or|").First().CompletionText=="select top 100 * from Orders order by 1 desc","generated keywords honor preferences; existing SQL retained");
        rule=Table();Set(rule);string sql="SELECT * FROM Ord";var t=AutomaticTrigger.TryCreate(sql,sql.Length);p=Complete(sql).First();
        Check(PersonalRules.Revalidate(t,p,"TestServer","TestDb"),"fresh proposal revalidates exact text/span");
        rule.Top=10;Set(rule);Check(!PersonalRules.Revalidate(t,p,"TestServer","TestDb"),"changed configuration refuses old proposal");
        rule=Table();rule.Table="Order Details";rule.Schema="log store";
        Check(PersonalRules.Preview(rule)=="SELECT TOP 100 * FROM [log store].[Order Details] ORDER BY 1 DESC","preview quotes unusual identifiers without splitting names");
    }
    static void Snippets()
    {
        var rule=Snippet();Set(rule);
        var p=Complete("selOr|").Single();Check(p.IsPersonalRule && p.RuleOffline && p.CompletionText=="SELECT * FROM Orders WHERE Id=;" && p.CaretOffset==30,"snippet text and cursor placeholder");
        Check(Complete("SELECT * FROM Items;\nselOrders|").Single().RuleReplaceStart==21,"shortcut begins at current instruction");
        Check(AutomaticTrigger.TryCreate("SELECT selOrders",16)==null || !Complete("SELECT selOrders|").Any(x=>x.IsPersonalRule),"shortcut cannot replace a SELECT expression");
        foreach(string text in new[]{"-- selOrders","SELECT 'selOrders'"})
            Check(AutomaticTrigger.TryCreate(text,text.Length)==null,"shortcut silent in comment/string");
        rule.AutoOnSpace=true;Set(rule);Check(Complete("selOrders |").Single().RuleAuto,"space is eligible only on enabled configured rule");
        rule.Enabled=false;Set(rule);Check(AutomaticTrigger.TryCreate("selOrders",9)==null,"disabled rule not offered");
        Set(Snippet());var t=AutomaticTrigger.TryCreate("selOrders",9);Check(!PersonalRules.NeedsConnection(t) && CompletionEngine.LoadAsync(null,t,CancellationToken.None).GetAwaiter().GetResult().Count==1,"global shortcut works offline");
        var alternatives=Complete("sel|");Check(alternatives.Any(x=>x.IsPersonalRule) && alternatives.Any(x=>x.Keyword=="SELECT"),"shortcut prefix retains ordinary SELECT keyword alternative");
    }
    static void Scopes()
    {
        var global=Table();var scoped=Table();scoped.Name="Specific";scoped.Server="TestServer";scoped.Database="TestDb";scoped.Top=5;Set(global,scoped);
        Check(Complete("SELECT * FROM Or|").Count(x=>x.IsPersonalRule)==1 && Complete("SELECT * FROM Or|").First().CompletionText.Contains("TOP 5"),"specific server/database rule overrides global for same target");
        scoped.Database="OtherDb";Set(global,scoped);Check(Complete("SELECT * FROM Or|").First().RuleId==global.Id,"nonmatching scope ignored");
        scoped.Database="testdb";Set(global,scoped);Check(Complete("SELECT * FROM Or|").First().RuleId==global.Id,"database scope does not conflate case-distinct database identities");
        scoped.Database="TestDb";scoped.Table="orders";scoped.Schema="DBO";scoped.Server="testserver";Set(global,scoped);
        Check(Complete("SELECT * FROM Or|").Count(x=>x.IsPersonalRule)==1 && Complete("SELECT * FROM Or|").First().RuleId==scoped.Id,"catalog-resolved rules share priority across case variants; server scope ignores case");
        var tie=Table();Set(global,tie);Check(Complete("SELECT * FROM Or|").Count(x=>x.IsPersonalRule)==2,"equal-priority alternatives remain separate");
        var snippet=Snippet();snippet.Database="OtherDb";Set(snippet);Check(Complete("selOrders|").Count==0,"scoped shortcut unavailable in another database");
        snippet.Database="TestDb";Set(snippet);Check(Complete("selOrders|").Single().RuleOffline==false,"scoped shortcut requires live connection identity");
    }
    static void Persistence()
    {
        var rule=Snippet();Set(rule);var copy=PreferenceStore.Current;copy.Rules[0].Name="Changed";Check(PreferenceStore.Current.Rules[0].Name==rule.Name,"preferences deep-copy rule drafts");
        string path=Path.Combine(Path.GetTempPath(),"JoinAssist-rules-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            PreferenceStore.Write(path,new Preferences{Rules=new List<PersonalRule>{rule}});Check(PreferenceStore.Load(path).Rules.Single().Sql==rule.Sql,"atomic preference round-trip preserves custom SQL");
            File.WriteAllText(path,"{}");Check(PreferenceStore.Load(path).Rules.Count==0,"existing users start with no active rules");
        }
        finally{if(File.Exists(path))File.Delete(path);}
        rule.AutoOnSpace=true;var imported=PersonalRules.Import(PersonalRules.Export(new[]{rule})).Single();Check(imported.Sql==rule.Sql && imported.Id!=rule.Id && !imported.Enabled && !imported.AutoOnSpace,"import preserves SQL but creates disabled fresh identities");
        bool failed=false;try{PersonalRules.Validate(new[]{rule,rule.Copy()});}catch(ArgumentException){failed=true;}Check(failed,"duplicate identity rejected");
        failed=false;try{PersonalRules.Import("null");}catch(ArgumentException){failed=true;}Check(failed,"null import rejected");
        rule.Shortcut="SELECT";failed=false;try{rule.Validate();}catch(ArgumentException){failed=true;}Check(failed,"reserved SQL keyword cannot become an unreachable shortcut");
    }
}
