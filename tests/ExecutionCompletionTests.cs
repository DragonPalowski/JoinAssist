using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using JoinAssist;
using ProcedureParameter = JoinAssist.ProcedureParameter;

static class ExecutionCompletionTests
{
    static int checks;
    static void Check(bool ok,string name){checks++;if(!ok)throw new Exception("EXEC: "+name);}
    static AutomaticTrigger Trigger(string sample){int at=sample.IndexOf('|');return AutomaticTrigger.TryCreate(sample.Remove(at,1),at);}
    internal static void Run()
    {
        var saved=PreferenceStore.Current;
        try
        {
            Check(!new Preferences().ExecutionCompletion,"off by default");
            var settings=new Preferences{ExecutionCompletion=true};PreferenceStore.Apply(settings);
            foreach(string sql in new[]{"EXEC |","EXEC Get|","EXECUTE dbo.Get|","EXEC dbo.|","SELECT 1;\nexec Get|","EXEC [dbo].[Get Orders]|"})Check(Trigger(sql)?.Mode==CompletionMode.Execution,"name trigger "+sql);
            foreach(string sql in new[]{"EXEC GetOrders |","EXEC dbo.GetOrders @Cu|","EXEC GetOrders @Id=1, @Na|"})Check(Trigger(sql)?.Mode==CompletionMode.ExecutionParameter,"parameter trigger "+sql);
            foreach(string sql in new[]{"-- EXEC Get|","SELECT 'EXEC Get|'","EXEC(@sql)|","EXEC @sql|","EXEC @rc = Get|","EXEC db.dbo.Get|","EXEC GetOrders @Id=|","EXEC GetOrders @Id=1 WITH RECOMPILE|","EXEC GetOrders AT server|","EXEC GetOrders; SELECT Id| FROM Orders"})Check(Trigger(sql)?.Mode!=CompletionMode.Execution && Trigger(sql)?.Mode!=CompletionMode.ExecutionParameter,"non-exec context "+sql);
            var proc=new ProcedureInfo{Id=1,Schema="dbo",Name="GetOrders"};proc.Parameters.Add(new ProcedureParameter{Name="@CustomerId",Type="int"});proc.Parameters.Add(new ProcedureParameter{Name="@Name",Type="nvarchar(50)"});
            var all=new List<ProcedureInfo>{proc,new ProcedureInfo{Id=2,Schema="sys",Name="sp_help"}};
            var trigger=Trigger("EXEC Get|");var item=trigger.OrderMatches(ExecutionCompletion.Items(trigger,all)).Single();
            Check(item.CompletionText=="EXEC dbo.GetOrders\n    @CustomerId = NULL,\n    @Name = NULL","named parameter draft");
            Check(item.Fields.Count==2 && item.Fields.All(f=>item.CompletionText.Substring(f.Start,f.Length)=="NULL"),"linked field spans");
            Check(item.DisplayText.Contains("nvarchar(50)") && item.CaretOffset==item.Fields[0].Start,"signature and first value");
            IList<ParseError> errors;new TSql160Parser(true).Parse(new StringReader(item.CompletionText),out errors);Check(errors.Count==0,"generated syntax");
            trigger=Trigger("EXEC GetOrders @CustomerId=1, @Na|");var args=ExecutionCompletion.Items(trigger,all);Check(args.Count==1 && args[0].SearchName=="@Name" && args[0].CompletionText=="@Name = ","only unassigned named parameters");
            trigger=Trigger("EXEC GetOrders 7, @Na|");Check(ExecutionCompletion.Items(trigger,all).Single().SearchName=="@Name","positional arguments consume parameters in declaration order");
            trigger=Trigger("EXEC GetOrders @Na|me = N'x'");Check(ExecutionCompletion.Items(trigger,all).Single(p=>p.SearchName=="@Name").CompletionText=="@Name","existing equals and values preserved");
            trigger=Trigger("EXEC Get|Orders @CustomerId=7;");item=ExecutionCompletion.Items(trigger,all).First();Check(item.CompletionText=="EXEC dbo.GetOrders" && item.Fields==null,"existing argument preserved");
            proc.Parameters.Add(new ProcedureParameter{Name="@Total",Type="int",Output=true});proc.Parameters.Add(new ProcedureParameter{Name="@Rows",Type="[dbo].[Ids]",Table=true});
            trigger=Trigger("DECLARE @ja_Total int;\nEXEC Get|");item=ExecutionCompletion.Items(trigger,all).First();
            Check(item.CompletionText.Contains("DECLARE @ja_Total_2 int;") && item.CompletionText.Contains("@Total = @ja_Total_2 OUTPUT"),"collision-free OUTPUT variable");
            Check(item.CompletionText.Contains("DECLARE @ja_Rows [dbo].[Ids];") && item.CompletionText.Contains("@Rows = @ja_Rows"),"table-valued variable declared");
            new TSql160Parser(true).Parse(new StringReader(item.CompletionText),out errors);Check(errors.Count==0,"OUTPUT and TVP syntax");
            trigger=Trigger("IF 1=1\nEXEC Get|");Check(ExecutionCompletion.Items(trigger,all).First().Fields==null,"conditional context never receives declarations or model");
            all.Add(new ProcedureInfo{Id=3,Schema="other",Name="GetOrders"});trigger=Trigger("EXEC GetOrders @Na|");Check(ExecutionCompletion.Items(trigger,all).Count==0,"ambiguous procedure refuses parameter guess");
            trigger=Trigger("EXEC dbo.GetOrders @Na|");Check(ExecutionCompletion.Items(trigger,all).Any(p=>p.SearchName=="@Name"),"schema disambiguates");
            using(var db=new FakeDatabase())
            {
                trigger=Trigger("EXEC Get|");var loaded=CompletionEngine.LoadAsync(db,trigger,CancellationToken.None).GetAwaiter().GetResult();
                Check(loaded.Count>0 && db.Commands.Count==1 && db.Commands[0].CommandText.StartsWith("SELECT",StringComparison.Ordinal),"one metadata SELECT only; no procedure execution");
                Check(loaded.First().DisplayText.Contains("nvarchar(50)"),"catalog type byte length decoded");
                var scope=new MetadataScope();CompletionEngine.LoadAsync(db,trigger,CancellationToken.None,scope).GetAwaiter().GetResult();int reads=db.Commands.Count;CompletionEngine.LoadAsync(db,trigger,CancellationToken.None,scope).GetAwaiter().GetResult();Check(db.Commands.Count==reads,"procedure metadata cached in connection/database scope");
                MetadataCache.Invalidate(scope);CompletionEngine.LoadAsync(db,trigger,CancellationToken.None,scope).GetAwaiter().GetResult();Check(db.Commands.Count==reads+1,"refresh invalidates procedure cache too");
            }
            settings.ExecutionCompletion=false;PreferenceStore.Apply(settings);Check(Trigger("EXEC Get|")?.Mode!=CompletionMode.Execution,"toggle restores previous behavior");
        }
        finally{PreferenceStore.Apply(saved);}
        Console.WriteLine("PASS: "+checks+" EXEC completion checks (metadata-only names/parameters, drafts/fields/types, existing arguments, OUTPUT/TVP, ambiguity and toggles).");
    }
}
