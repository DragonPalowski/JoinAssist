using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using JoinAssist;

static class SmartCompletionTests
{
    static int checks;
    static void Check(bool value,string label){checks++;if(!value)throw new Exception("Smart completion: "+label);}
    static AutomaticTrigger Trigger(string sample)
    {int at=sample.IndexOf('|');if(at<0)at=sample.Length;else sample=sample.Remove(at,1);var t=AutomaticTrigger.TryCreate(sample,at);Check(t!=null,"trigger "+sample);return t;}
    static List<JoinProposal> Complete(string sample)
    {var t=Trigger(sample);using(var db=new FakeDatabase())return t.OrderMatches(CompletionEngine.LoadAsync(db,t,CancellationToken.None).GetAwaiter().GetResult());}
    static JoinProposal Name(string name){return new JoinProposal{SearchName=name,TargetTable=name,CompletionText=name,Category="Tabella"};}
    internal static void Run()
    {
        var original=PreferenceStore.Current;
        try{PreferenceStore.Apply(new Preferences());Matching();Context();History();Distance();Performance();}
        finally{PreferenceStore.Apply(original);}
        Console.WriteLine("PASS: "+checks+" smart-completion checks (typos/prefixes, word boundaries, suffix ranking, scope/aliases, disambiguation, local hashed history, tolerance and bounded matching). Synthetic metadata only.");
    }
    static void Matching()
    {
        foreach(var pair in new[]{new[]{"patients_tb","patietns"},new[]{"patients_tb","patietns_tb"},new[]{"patients_tb","patiants"},new[]{"patients_tb","pattients"},new[]{"idPrescription","idPrescripton"},new[]{"prescriptions_tb","prescir"}})
            Check(NameMatching.Match(pair[0],pair[1],SearchTolerance.Balanced).Score!=int.MaxValue,"insertion/deletion/substitution/transposition or partial typo "+pair[1]);
        Check(NameMatching.Match("patients_tb","patietns",SearchTolerance.Balanced).IsCorrection,"swapped letters labelled as correction");
        Check(NameMatching.Match("patients_tb","patietns",SearchTolerance.Precise).Score==int.MaxValue,"precise mode excludes typo guesses");
        Check(NameMatching.Match("Car","cat",SearchTolerance.Balanced).Score==int.MaxValue && NameMatching.Match("Car","cat",SearchTolerance.Tolerant).IsCorrection,"short typo requires tolerant mode");
        Check(NameMatching.Match("patients_tb","px",SearchTolerance.Tolerant).Score==int.MaxValue,"two characters cannot trigger typo guesses");
        Check(NameMatching.Match("prescriptionItemDetails_tb","presc_det",SearchTolerance.Precise).Score!=int.MaxValue,"prefixes of separate camelCase words");
        Check(NameMatching.Match("prescription_item_details_tb","presc det",SearchTolerance.Precise).Score!=int.MaxValue,"space-separated search terms handled as word prefixes");
        Check(NameMatching.Match("HTTPResponseCode","respco",SearchTolerance.Precise).Score!=int.MaxValue,"acronym and following words split correctly");
        Check(NameMatching.Score("prescriptionItemDetails_tb","zzzz")==int.MaxValue,"unrelated name absent");
        foreach(var pair in new[]{new[]{"#Orders","Orders"},new[]{"Orders","#Orders"},new[]{"##Orders","#Orders"},new[]{"@Orders","#Orders"}})
            Check(NameMatching.Match(pair[0],pair[1],SearchTolerance.Tolerant).Score==int.MaxValue,"local marker must agree "+pair[0]);
        Check(NameMatching.Match("#Orders","#Ordrs",SearchTolerance.Balanced).Score!=int.MaxValue,"typo correction inside same local namespace");
        var t=Trigger("SELECT * FROM patie");var shortName=Name("patients_tb");var longer=Name("patientsrev_tb");var history=Name("patientAppointmentHistory_vw");
        Check(t.OrderMatches(new[]{history,longer,shortName}).SequenceEqual(new[]{shortName,longer,history}),"shortest logical name first");
        Check(NameMatching.Score("patients_tb","patie")==NameMatching.Score("patients_vw","patie"),"table/view suffix not part of proximity length");
        var exact=Name("patietns");t=Trigger("SELECT * FROM patietns");
        Check(t.OrderMatches(new[]{shortName,exact}).First()==exact,"exact name beats corrected candidate");
        Check(t.OrderMatches(new[]{shortName,exact}).Count==1,"balanced mode hides guesses when a literal name is available");
        var prefs=PreferenceStore.Current;prefs.SearchTolerance=SearchTolerance.Tolerant;PreferenceStore.Apply(prefs);
        Check(t.OrderMatches(new[]{shortName,exact}).Count==2 && t.OrderMatches(new[]{shortName,exact}).First()==exact,"tolerant mode retains corrections below literal names");prefs.SearchTolerance=SearchTolerance.Balanced;PreferenceStore.Apply(prefs);
        t=Trigger("SELECT * FROM dbo.patietns");shortName.TargetSchema="dbo";longer.TargetSchema="archive";
        Check(t.OrderMatches(new[]{shortName,longer}).Single()==shortName,"schema is literal even when object has typo");
        var match=NameMatching.Match("patients_tb","patietns",SearchTolerance.Balanced);
        Check(match.Positions.Length>0 && match.Positions.All(i=>i>=0 && i<match.Name.Length),"correction highlight maps to actual name");
        Check(ReferenceEquals(match,NameMatching.Match("patients_tb","patietns",SearchTolerance.Balanced)),"bounded matching cache reused");
    }
    static void Context()
    {
        var refs=Complete("SELECT Ord| FROM Orders p JOIN Items i ON p.Id=i.OrderId");
        var alias=refs.Single(p=>p.Category=="Riferimento");
        Check(alias.CompletionText=="p" && alias.PopupTitle=="p" && alias.PopupDetail.Contains("dbo.Orders"),"physical name finds the existing alias and shows original table");
        Check(Complete("SELECT p.Custmoer| FROM Orders p JOIN Items i ON p.Id=i.OrderId").All(p=>p.SourceReference=="p"),"qualified typo searches only that source");
        Check(Complete("SELECT p.Custmoer| FROM Orders p").Single().CompletionText=="CustomerId","qualified column typo replacement");
        Check(Complete("SELECT Customre| FROM Orders").Any(p=>p.SearchName=="CustomerId"),"unfinished column prefix with swapped letters");
        Check(Complete("SELECT Cu| FROM Items").All(p=>p.SearchName!="CustomerId"),"expression names restricted to current query sources");
        var ids=Complete("SELECT Id| FROM Orders o JOIN Items i ON o.Id=i.OrderId").Where(p=>p.SearchName=="Id").ToList();
        Check(ids.Count==2 && ids.Select(p=>p.CompletionText).OrderBy(s=>s).SequenceEqual(new[]{"i.Id","o.Id"}),"homonymous columns remain separate and qualified");
        Check(ids.All(p=>p.PopupDetail.Contains(p.SourceReference+" (") && p.PopupDetail.Contains("dbo.")),"homonymous rows show alias and actual table/schema");
        var join=Complete("SELECT * FROM Orders JOIN Custmoers|");
        Check(join.Any(p=>!p.IsName) && !join.First().IsName,"actual FK remains preferred for corrected JOIN target");
        Check(Complete("SELECT * FROM Custmoers|").Any(p=>p.TargetTable=="Customers"),"table-position typo completion");
        var local=Complete("WITH orderRows AS(SELECT Id FROM Orders) SELECT ord| FROM orderRows q");
        Check(local.Any(p=>p.Category=="Riferimento" && p.CompletionText=="q"),"CTE name can find its existing alias");
        var noAliases=Complete("SELECT Or| FROM Orders o WHERE EXISTS(SELECT 1 FROM Items i)");
        Check(noAliases.All(p=>p.CompletionText!="i"),"references from nested queries stay private");
    }
    static void History()
    {
        string folder=Path.Combine(Path.GetTempPath(),"JoinAssist-choice-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string path=Path.Combine(folder,"choices.json");
        try
        {
            var history=new CompletionHistory(path);string scope=CompletionHistory.ScopeKey("SyntheticServer","SyntheticDb");
            var preferred=Name("patientsrev_tb");var other=Name("patients_tb");var t=Trigger("SELECT * FROM pat");
            for(int i=0;i<3;i++)history.Record(scope,"pat",preferred);history.WaitForSave();
            Check(t.OrderMatches(new[]{other,preferred},scope,history).First()==preferred,"accepted choice breaks a similar-prefix tie");
            Check(t.OrderMatches(new[]{other,preferred},CompletionHistory.ScopeKey("SyntheticServer","OtherDb"),history).First()==other,"history isolated by database");
            Check(t.OrderMatches(new[]{other,preferred},CompletionHistory.ScopeKey("OtherServer","SyntheticDb"),history).First()==other,"history isolated by server");
            Check(t.OrderMatches(new[]{other,preferred},scope,new CompletionHistory(path)).First()==preferred,"choices survive reload");
            string saved=File.ReadAllText(path);
            Check(!saved.Contains("patients") && !saved.Contains("pat\"") && !saved.Contains("Synthetic") && !saved.Contains("SELECT"),"only hashes/counts/timestamps persisted");
            var prefs=PreferenceStore.Current;prefs.RememberChoices=false;PreferenceStore.Apply(prefs);
            Check(t.OrderMatches(new[]{other,preferred},scope,history).First()==other,"disabled history does not influence order");prefs.RememberChoices=true;PreferenceStore.Apply(prefs);
            var exact=Name("pat");Check(t.OrderMatches(new[]{preferred,exact},scope,history).First()==exact,"history never beats an exact match");
            history.Record(scope,"pat",preferred);history.Clear();history.WaitForSave();
            Check(new CompletionHistory(path).Boost(scope,"pat",preferred)==0 && history.Boost(scope,"pat",preferred)==0,"reset cannot be undone by a pending writer");
            long queuedVersion=history.ResetVersion;history.Clear();history.Record(scope,"pat",preferred,queuedVersion);history.WaitForSave();
            Check(history.Boost(scope,"pat",preferred)==0,"a queued accepted-choice task cannot restore choices after reset");
            File.WriteAllText(path,"{broken");var repaired=new CompletionHistory(path);repaired.Record(scope,"pat",preferred);repaired.WaitForSave();
            Check(new CompletionHistory(path).Boost(scope,"pat",preferred)==1,"malformed history ignored and repaired");
            var second=new CompletionHistory(path);repaired.Clear();
            second.Record(scope,"pat",preferred);second.WaitForSave();
            Check(new CompletionHistory(path).Boost(scope,"pat",preferred)==0,"another SSMS history instance cannot restore choices after reset");
            second.Record(scope,"pat",preferred);second.WaitForSave();Check(new CompletionHistory(path).Boost(scope,"pat",preferred)==1,"new choices after observing reset can be learned again");
            var bounded=new CompletionHistory(null);for(int i=0;i<2200;i++)bounded.Record(scope,"name",Name("Object"+i));
            var entries=(System.Collections.IDictionary)typeof(CompletionHistory).GetField("entries",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(bounded);
            Check(entries.Count==2048,"history bounded to 2048 accepted identities");
            var blocked=new CompletionHistory(Path.Combine(path,"not-a-directory.json"));blocked.Record(scope,"pat",preferred);blocked.WaitForSave();
            Check(blocked.Boost(scope,"pat",preferred)==1,"storage failure does not stop in-memory completion");
        }
        finally{Directory.Delete(folder,true);}
    }
    static void Distance()
    {
        var method=typeof(NameMatching).GetMethod("PrefixDistance",BindingFlags.NonPublic|BindingFlags.Static);var random=new Random(531);
        for(int k=0;k<180;k++)
        {
            string a=new string(Enumerable.Range(0,random.Next(3,13)).Select(i=>(char)('a'+random.Next(4))).ToArray());
            string b=new string(Enumerable.Range(0,random.Next(2,17)).Select(i=>(char)('a'+random.Next(4))).ToArray());int budget=k%2+1;
            var d=new int[a.Length+1,b.Length+1];for(int i=0;i<=a.Length;i++)d[i,0]=i;for(int j=0;j<=b.Length;j++)d[0,j]=j;
            for(int i=1;i<=a.Length;i++)for(int j=1;j<=b.Length;j++)
            {d[i,j]=Math.Min(d[i-1,j]+1,Math.Min(d[i,j-1]+1,d[i-1,j-1]+(a[i-1]==b[j-1]?0:1)));if(i>1 && j>1 && a[i-1]==b[j-2] && a[i-2]==b[j-1])d[i,j]=Math.Min(d[i,j],d[i-2,j-2]+1);}
            int expected=Enumerable.Range(1,b.Length).Min(j=>d[a.Length,j]);object[] args={a,b,budget,0};int actual=(int)method.Invoke(null,args);
            Check(actual==Math.Min(expected,budget+1),"banded prefix/transposition distance agrees with full reference "+k);
        }
    }
    static void Performance()
    {
        using(var cancel=new CancellationTokenSource())
        {cancel.Cancel();bool stopped=false;try{Trigger("SELECT * FROM pat").OrderMatches(new[]{Name("patients_tb")},cancel:cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"obsolete ranking can be cancelled");}
        var timer=Stopwatch.StartNew();
        for(int i=0;i<10000;i++)NameMatching.Match("Object"+i+"WithSeveralDifferentWords_tb","patietns",SearchTolerance.Balanced);
        Check(timer.ElapsedMilliseconds<10000,"10k unrelated metadata names matched within 10s test budget; actual filtering runs off UI thread");
        foreach(string field in new[]{"profiles","matches"})
        {var cache=(System.Collections.IDictionary)typeof(NameMatching).GetField(field,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);Check(cache.Count<=(field=="profiles"?4096:8192),"bounded "+field+" cache");}
        string path=Path.Combine(Path.GetTempPath(),"JoinAssist-search-prefs-"+Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path,"{}");Check(PreferenceStore.Load(path).SearchTolerance==SearchTolerance.Balanced && PreferenceStore.Load(path).RememberChoices,"existing preferences migrate to balanced/local choices");
            var prefs=new Preferences{SearchTolerance=SearchTolerance.Tolerant,RememberChoices=false};PreferenceStore.Write(path,prefs);
            Check(PreferenceStore.Load(path).SearchTolerance==SearchTolerance.Tolerant && !PreferenceStore.Load(path).RememberChoices,"search preferences round trip");
            prefs.SearchTolerance=(SearchTolerance)99;bool rejected=false;try{prefs.Validate();}catch(ArgumentException){rejected=true;}Check(rejected,"invalid tolerance rejected");
        }
        finally{File.Delete(path);}
    }
}
