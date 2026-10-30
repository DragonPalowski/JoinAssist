using System;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using JoinAssist;

static class TableSelectTests
{
    static int checks;
    static void Check(bool ok,string text){checks++;if(!ok)throw new Exception("Table SELECT: "+text);}
    static AutomaticTrigger Trigger(string sample)
    {int caret=sample.IndexOf('|');return AutomaticTrigger.TryCreate(sample.Remove(caret,1),caret);}
    internal static void Run()
    {
        var saved=PreferenceStore.Current;
        try
        {
            Check(!new Preferences().TableSelect && new Preferences().TableSelectTop==0 && !new Preferences().TableSelectSemicolon,"opt-in defaults");
            Check(Trigger("Ord|")==null,"disabled bare name remains unchanged");
            var settings=new Preferences{TableSelect=true};PreferenceStore.Apply(settings);
            foreach(string sample in new[]{"Ord|","Orders|","  Ord|","-- note\nOrd|","SELECT * FROM Items;\nOrd|","SELECT * FROM Items\nGO\nOrd|","dbo.Ord|","[Order Details]|"})
                Check(Trigger(sample)?.Mode==CompletionMode.TableSelect,"standalone source: "+sample);
            foreach(string sample in new[]{"O|","SELECT * FROM Orders\nOrd|","SELECT\nOrd|","SELECT * FROM Ord|","SELECT o.Cust| FROM Orders o","UPDATE Ord|","INSERT INTO Ord|","SELECT * FROM Orders JOIN Ord|","-- Ord|","SELECT 'Ord|'","BEGIN\nOrd|","CREATE PROCEDURE p AS SELECT 1;\nOrd|","Orders WHERE Id=|","Orders; DELETE FROM Items|"})
                Check(Trigger(sample)?.Mode!=CompletionMode.TableSelect,"no hijack: "+sample);
            Check(Trigger("sel|").Mode==CompletionMode.Keyword,"existing keyword takes priority");
            using(var db=new FakeDatabase())
            {
                var trigger=Trigger("Ord|");var items=CompletionEngine.LoadAsync(db,trigger,CancellationToken.None).GetAwaiter().GetResult();
                var choice=trigger.OrderMatches(items).First();
                Check(choice.TargetTable=="Orders" && choice.IsTableSelect && choice.CompletionText=="SELECT * FROM Orders","rank and prepare physical source");
                Check(trigger.Preview(choice)==choice.CompletionText && trigger.InsertionCaret(choice,choice.CompletionText)==choice.CompletionText.Length,"exact preview and final caret");
                Check(db.Commands.All(c=>c.CommandText.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)),"metadata only");
            }
            settings.TableSelectTop=100;settings.TableSelectSemicolon=true;settings.AlwaysSchema=true;settings.QuoteNames=true;settings.UppercaseKeywords=false;PreferenceStore.Apply(settings);
            var catalog=new CatalogSnapshot();catalog.Objects.Add(new CatalogObject{Id=1,Schema="logs",Name="logs_tb",Kind="U",BareAllowed=true});
            var t=Trigger("logs.logs_tb|");var item=TableSelect.Items(t,catalog).Single();
            Check(item.CompletionText=="select top 100 * from [logs].[logs_tb];","TOP, formatting and semicolon");
            t=Trigger("logs.logs_tb|;");Check(TableSelect.Items(t,catalog).Single().CompletionText=="select top 100 * from [logs].[logs_tb]","existing terminator preserved without duplication");
            Check(Trigger("logs.lo|gs_tb").AllowsAutomatic("logs.logs_tb",7)==false,"no automatic middle-token expansion");
            settings=new Preferences{TableSelect=true};PreferenceStore.Apply(settings);
            t=Trigger("CREATE TABLE #Orders(Id int);\n#Or|");
            var local=CompletionEngine.LoadAsync(null,t,CancellationToken.None).GetAwaiter().GetResult().Single();
            Check(local.ScriptOnly && local.IsTableSelect && local.CompletionText=="SELECT * FROM #Orders","declared temp works without catalog connection");
            Check(!TableSelect.Items(t,new CatalogSnapshot()).Any(p=>p.TargetTable=="#Future"),"no future/session-only temp guessing");
            var serialized=new JavaScriptSerializer();var round=serialized.Deserialize<Preferences>(serialized.Serialize(new Preferences{TableSelect=true,TableSelectTop=250,TableSelectSemicolon=true}));
            Check(round.TableSelect && round.TableSelectTop==250 && round.TableSelectSemicolon,"settings serialization");
            foreach(int top in new[]{-1,1000001}){bool rejected=false;try{new Preferences{TableSelectTop=top}.Validate();}catch(ArgumentException){rejected=true;}Check(rejected,"TOP bounds");}
            settings.TableSelect=false;PreferenceStore.Apply(settings);Check(Trigger("Ord|")==null,"toggle off restores previous behavior");
        }
        finally{PreferenceStore.Apply(saved);}
        Console.WriteLine("PASS: "+checks+" table-to-SELECT checks (opt-in, boundaries, catalog/local names, TOP/format/terminator, preview, settings and no SQL execution).");
    }
}
