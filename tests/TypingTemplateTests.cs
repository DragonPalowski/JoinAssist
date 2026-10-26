using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using JoinAssist;

static class TypingTemplateTests
{
    static int count;
    static void Check(bool ok,string label){count++;if(!ok)throw new Exception("Typing templates: "+label);}
    static AutomaticTrigger Trigger(string sql)
    {int p=sql.IndexOf('|');if(p<0)p=sql.Length;else sql=sql.Remove(p,1);var t=AutomaticTrigger.TryCreate(sql,p);Check(t!=null,"trigger "+sql);return t;}
    static List<JoinProposal> Complete(string sql)
    {var t=Trigger(sql);using(var db=new FakeDatabase{RichColumns=true})return t.OrderMatches(CompletionEngine.LoadAsync(db,t,CancellationToken.None).GetAwaiter().GetResult());}
    internal static void Run()
    {
        var old=PreferenceStore.Current;
        try{PreferenceStore.Apply(new Preferences());Keywords();Filters();PartialFilters();Settings();}
        finally{PreferenceStore.Apply(old);}
        Console.WriteLine("PASS: "+count+" keyword/filter template checks (context, independent preferences, known types, ambiguity, aliases/CTEs/DML, existing values, exact replacement and caret). Metadata only; no edited SQL executed.");
    }
    static void Keywords()
    {
        foreach(var pair in new[]{new[]{"sel|","SELECT "},new[]{"SELECT * fr|","FROM "},new[]{"SELECT * FROM Orders wh|","WHERE "},new[]{"SELECT * FROM Orders ord|","ORDER BY "},new[]{"SELECT * FROM Orders WHERE Id=1 an|","AND "},new[]{"SELECT * FROM Orders ORDER BY Id de|","DESC "},new[]{"SELECT * FROM Orders GROUP by|","BY "},new[]{"SELECT * FROM Orders LEFT JOIN Customers on|","ON "},new[]{"SELECT * FROM Orders;\nsel|","SELECT "}})
        {
            var t=Trigger(pair[0]);Check(t.Mode==CompletionMode.Keyword,"keywords use no database: "+pair[0]);
            var result=t.OrderMatches(CompletionEngine.LoadAsync(null,t,CancellationToken.None).GetAwaiter().GetResult());
            Check(result.Any(p=>p.Keyword!=null && p.CompletionText==pair[1]),"keyword insertion "+pair[0]);
        }
        var mid=Trigger("SELECT * fro|m Orders WHERE Id=1");var selected=mid.OrderMatches(TypingTemplates.KeywordItems(mid)).Single();
        Check(mid.Context.ReplaceLength==4 && selected.CompletionText=="FROM","middle keyword replaces full token without duplicating following whitespace");
        var first=Trigger("SELECT * FROM Orders or|");var edited=Trigger("SELECT * FROM Orders ou|");
        Check(first.BaseKey==edited.BaseKey && edited.OrderMatches(TypingTemplates.KeywordItems(first)).Any(p=>p.Keyword=="OUTER APPLY"),"reused keyword session keeps all context candidates after prefix changes");
        Check(Complete("SELECT * FROM Orders\nsel|").Any(p=>p.Keyword=="SELECT"),"new SELECT at line start without a preceding semicolon");
        Check(Complete("SELECT * FROM Orders WHERE\nsel|").Any(p=>p.Keyword=="SELECT"),"keyword in a following statement after an unfinished filter");
        Check(Complete("SELECT\nfro|").Any(p=>p.Keyword=="FROM"),"FROM may be completed before the SELECT list is filled in");
        Check(Complete("SELECT * FROM Ord|").All(p=>p.Keyword==null),"table names not replaced by ORDER keyword");
        Check(Complete("SELECT o.Or| FROM Orders o").All(p=>p.Keyword==null),"qualified names never get keyword candidates");
        Check(AutomaticTrigger.TryCreate("-- sel",6)==null && AutomaticTrigger.TryCreate("SELECT 'sel'",10)==null,"comments and string literals remain silent");
        Check(AutomaticTrigger.TryCreate("SELECT [sel]",12)==null || !Complete("SELECT [sel]|").Any(p=>p.Keyword!=null),"quoted identifiers not corrected as keywords");
        var prefs=PreferenceStore.Current;prefs.UppercaseKeywords=false;PreferenceStore.Apply(prefs);
        Check(Complete("SeL|").Single().CompletionText=="select ","keyword case follows existing format setting");PreferenceStore.Apply(new Preferences());
    }
    static void Filters()
    {
        foreach(string op in new[]{"LIKE","NOT LIKE","IN","NOT IN"})
        {
            string sample="SELECT * FROM Customers c WHERE c.Name "+op+"| AND c.Id=1 ORDER BY c.Id DESC; SELECT * FROM Orders";
            var t=Trigger(sample);var item=Complete(sample).Single();
            string expected=op+(op.EndsWith("LIKE",StringComparison.Ordinal)?" '%%'":" ('', '')");
            Check(t.Mode==CompletionMode.FilterTemplate && item.CompletionText==expected,"string template "+op);
            string original=sample.Replace("|","");string result=original.Remove(t.Context.ReplaceStart,t.Context.ReplaceLength).Insert(t.Context.ReplaceStart,t.Insertion(item));
            Check(result==sample.Replace(op+"|",expected),"operator-only insertion preserves source, later predicates/statements "+op);
            int cursor=t.Context.ReplaceStart+t.InsertionCaret(item,item.CompletionText);
            Check(op.EndsWith("LIKE",StringComparison.Ordinal)?result[cursor-1]=='%' && result[cursor]=='%':result[cursor-1]=='\'' && result[cursor]=='\'',"caret inside first pattern/value "+op);
        }
        foreach(string op in new[]{"IN","NOT IN"})Check(Complete("SELECT * FROM Orders WHERE Id "+op+"|").Single().CompletionText==op+" ( ,)","nonstring IN uses unquoted placeholders");
        Check(Complete("SELECT * FROM Orders WHERE Id LIKE|").Count==0 && Complete("SELECT * FROM Orders WHERE Id NOT LIKE|").Count==0,"LIKE on nontext adds no template");
        Check(Complete("SELECT * FROM Customers WHERE Name LIKE |").Single().CompletionText=="LIKE '%%'","space after operator supported");
        foreach(string sample in new[]{"SELECT * FROM Customers WHERE Name LIKE| '%old%'","SELECT * FROM Customers WHERE Name IN| ('A','B')","SELECT * FROM Customers WHERE Name IN| (SELECT Name FROM Customers)","SELECT * FROM Customers WHERE Name LIKE| @pattern"})
            Check(Complete(sample).All(p=>!p.IsFilterTemplate),"existing operand is preserved: "+sample);
        Check(Complete("SELECT * FROM Customers WHERE Name NOT /* keep this */ LIKE|").All(p=>!p.IsFilterTemplate),"operator-spanning comments are never erased");
        foreach(string sample in new[]{"SELECT * FROM Orders o JOIN Items i ON o.Id=i.OrderId WHERE Id IN|","SELECT * FROM Orders WHERE Missing LIKE|","SELECT * FROM Missing WHERE Name LIKE|","SELECT * FROM Customers c WHERE x.Name LIKE|"})
            Check(Complete(sample).All(p=>!p.IsFilterTemplate),"ambiguous/unknown source or column is not guessed: "+sample);
        Check(Complete("SELECT * FROM Customers c JOIN Missing x ON 1=1 WHERE Name LIKE|").Count==0,"unqualified type not guessed across an unreadable joined source");
        Check(Complete("SELECT * FROM Customers c JOIN Missing x ON 1=1 WHERE c.Name LIKE|").Single().IsFilterTemplate,"qualified known source still usable beside unreadable source");
        Check(Complete("SELECT * FROM Customers c WHERE EXISTS(SELECT 1 FROM Orders c WHERE EXISTS(SELECT 1 FROM Items i WHERE c.Name LIKE|))").Count==0,"nearest outer alias shadows a farther text column");
        foreach(string sample in new[]{"WITH n AS(SELECT Name FROM Customers) SELECT * FROM n WHERE Name LIKE|","SELECT * FROM (SELECT Name FROM Customers) n WHERE n.Name NOT LIKE|","DELETE FROM Customers WHERE Name LIKE|","UPDATE Customers SET TenantId=1 WHERE Name IN|","CREATE TABLE #Text (Name varchar(20)); SELECT * FROM #Text WHERE Name LIKE|","SELECT * FROM Customers c WHERE EXISTS(SELECT 1 FROM Orders o WHERE c.Name LIKE|)"})
            Check(Complete(sample).Single().IsFilterTemplate,"known text via aliases/CTEs/local/DML/correlation: "+sample);
        foreach(string sample in new[]{"SELECT * FROM Customers WHERE LOWER(Name) LIKE|","SELECT * FROM Customers WHERE (Name+'x') LIKE|"})
        {var t=AutomaticTrigger.TryCreate(sample.Replace("|",""),sample.IndexOf('|'));Check(t==null || t.Mode!=CompletionMode.FilterTemplate,"non-simple scalar not guessed");}
        var prefs=PreferenceStore.Current;prefs.UppercaseKeywords=false;PreferenceStore.Apply(prefs);
        Check(Complete("SELECT * FROM Customers WHERE Name NOT LIKE|").Single().CompletionText=="not like '%%'","filter follows keyword case setting");PreferenceStore.Apply(new Preferences());
    }
    static void PartialFilters()
    {
        foreach(var pair in new[]{new[]{"li","LIKE"},new[]{"lik","LIKE"},new[]{"not li","NOT LIKE"},new[]{"not lik","NOT LIKE"},new[]{"in","IN"},new[]{"not in","NOT IN"},new[]{"no","NOT LIKE"},new[]{"not","NOT IN"}})
        {
            string sample="SELECT * FROM Customers WHERE Name "+pair[0]+"|\n\nSELECT * FROM Orders";
            var t=Trigger(sample);var chosen=Complete(sample).Single(p=>p.TemplateOperator==pair[1]);
            string expected=pair[1]+(pair[1].EndsWith("LIKE",StringComparison.Ordinal)?" '%%'":" ('', '')");
            Check(t.Mode==CompletionMode.FilterTemplate && t.Matches(chosen) && chosen.CompletionText==expected,"partial operator prepares a typed model with one acceptance: "+pair[0]);
            string original=sample.Replace("|","");string result=original.Remove(t.Context.ReplaceStart,t.Context.ReplaceLength).Insert(t.Context.ReplaceStart,t.Insertion(chosen));
            Check(result==sample.Replace(pair[0]+"|",expected),"partial filter replacement preserves the following query: "+pair[0]);
            Check(chosen.PopupTitle==expected,"popup explicitly shows pattern/list, not only keyword");
        }
        Check(Complete("SELECT * FROM Customers WHERE Name no|").Select(p=>p.TemplateOperator).SequenceEqual(new[]{"NOT LIKE","NOT IN"}),"NOT prefix offers both eligible models");
        Check(Complete("SELECT * FROM Orders WHERE Id no|").First().CompletionText=="NOT IN ( ,)","numeric NOT prefix offers IN before ordinary LIKE keyword");
        Check(Complete("SELECT * FROM Orders WHERE Id li|").Single().Keyword=="LIKE","partial LIKE keeps nontext safeguard while retaining ordinary keyword");
        Check(Complete("SELECT * FROM Customers c JOIN Missing x ON 1=1 WHERE Name li|").All(p=>!p.IsFilterTemplate),"partial operator does not guess ambiguous/unreadable types");
        var offline=Trigger("SELECT * FROM Customers WHERE Name li|");
        var words=CompletionEngine.LoadAsync(null,offline,CancellationToken.None).GetAwaiter().GetResult();
        Check(words.Single().Keyword=="LIKE" && offline.Matches(words[0]) && !words[0].IsFilterTemplate,"operator words can complete without a metadata connection, never as a typed template");
        Check(Complete("SELECT * FROM Customers WHERE Name li|ke").Single().CompletionText=="LIKE '%%'","midword completion replaces the complete operator");
        var first=Trigger("SELECT * FROM Customers WHERE Name li|");var second=Trigger("SELECT * FROM Customers WHERE Name lik|");
        Check(first.BaseKey==second.BaseKey && second.Matches(Complete("SELECT * FROM Customers WHERE Name li|").Single()),"operator-prefix edits safely reuse the type-resolved model");
        foreach(string sample in new[]{"SELECT * FROM Customers WHERE Name li| '%old%'","SELECT * FROM Customers WHERE Name no| @pattern","SELECT * FROM Customers WHERE Name NOT /* keep */ li|"})
            Check(Complete(sample).All(p=>!p.IsFilterTemplate),"partial model preserves existing values/comments: "+sample);
        PreferenceStore.Apply(new Preferences{KeywordCompletion=false});
        Check(Complete("SELECT * FROM Customers WHERE Name li|").Single().IsFilterTemplate,"partial templates remain independent of keyword completion");
        PreferenceStore.Apply(new Preferences{FilterTemplates=false});
        Check(Complete("SELECT * FROM Customers WHERE Name li|").Any(p=>p.Keyword=="LIKE") && Complete("SELECT * FROM Customers WHERE Name no|").All(p=>!p.IsFilterTemplate),"disabled models retain ordinary keyword proposals");
        PreferenceStore.Apply(new Preferences{UppercaseKeywords=false});
        Check(Complete("SELECT * FROM Customers WHERE Name not li|").Single().CompletionText=="not like '%%'","partial operator respects keyword casing");
        PreferenceStore.Apply(new Preferences());
    }
    static void Settings()
    {
        var p=new Preferences{KeywordCompletion=false,FilterTemplates=true};PreferenceStore.Apply(p);
        Check(AutomaticTrigger.TryCreate("sel",3)==null && Complete("SELECT * FROM Customers WHERE Name LIKE|").Single().IsFilterTemplate,"filter setting independent of keywords");
        p.KeywordCompletion=true;p.FilterTemplates=false;PreferenceStore.Apply(p);
        Check(Complete("sel|").Single().Keyword=="SELECT" && Complete("SELECT * FROM Customers WHERE Name LIKE|").All(i=>!i.IsFilterTemplate),"keywords independent of filter setting");
        string path=Path.Combine(Path.GetTempPath(),"JoinAssist-template-preferences-"+Guid.NewGuid().ToString("N"));
        try
        {
            PreferenceStore.Write(path,p);var read=PreferenceStore.Load(path);Check(read.KeywordCompletion && !read.FilterTemplates,"new settings persisted");
            File.WriteAllText(path,"{}");read=PreferenceStore.Load(path);Check(read.KeywordCompletion && read.FilterTemplates,"existing users get enabled configurable defaults");
        }
        finally{File.Delete(path);}
    }
}
