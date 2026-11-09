using System;
using System.Linq;
using System.Web.Script.Serialization;
using JoinAssist;

static class ExecutionSafetyTests
{
    static int checks;
    static void Check(bool ok,string text){checks++;if(!ok)throw new Exception("Execution safety: "+text);}
    internal static void Run()
    {
        foreach(string sql in new[]{"UPDATE dbo.Orders SET Flag=1;","DELETE FROM dbo.Orders;","DELETE TOP(10) FROM dbo.Orders;","UPDATE TOP(1) dbo.Orders SET Flag=1;","UPDATE o SET Flag=1 FROM Orders o JOIN Items i ON o.Id=i.OrderId;","DELETE o FROM Orders o JOIN Items i ON o.Id=i.OrderId;","UPDATE Orders SET Flag=(SELECT TOP 1 Flag FROM Items WHERE Id=1);","WITH c AS(SELECT * FROM Orders WHERE Id=1) DELETE FROM c;","BEGIN TRAN; DELETE FROM Orders; ROLLBACK;","IF 1=0 DELETE FROM Orders;"})
        {var r=ExecutionSafety.Analyze(sql);Check(r.Findings.Count==1 && r.Uncertainty==null,"missing outer WHERE: "+sql);Check(r.Findings[0].Offset>=0 && r.Findings[0].Length>0,"navigable finding");}
        foreach(string sql in new[]{"SELECT 'DELETE FROM Orders'; -- UPDATE Orders SET Flag=1","/* DELETE FROM Orders */ SELECT 1;","DELETE FROM Orders WHERE Id=1;","UPDATE Orders SET Flag=1 WHERE 1=1;","CREATE PROCEDURE p AS DELETE FROM Orders;","ALTER PROCEDURE p AS UPDATE Orders SET Flag=1;","CREATE OR ALTER PROCEDURE p AS DELETE FROM Orders;","CREATE TRIGGER t ON Orders AFTER INSERT AS DELETE FROM Items;","EXEC p; EXEC(N'DELETE FROM Orders');"})
        {var r=ExecutionSafety.Analyze(sql);Check(r.Findings.Count==0 && r.Uncertainty==null,"no immediate unfiltered DML: "+sql);}
        var batch=ExecutionSafety.Analyze("SELECT 1;\nUPDATE Orders SET Flag=1;\nGO\nDELETE FROM Items;",40,8);
        Check(batch.Findings.Count==2 && batch.Findings[0].Line==9 && batch.Findings[1].Line==11 && batch.Findings[0].Offset>40,"GO, multiple kinds and selected-text location mapping");
        Check(ExecutionSafety.Analyze("DELETE FROM Orders WHERE").Uncertainty!=null,"parse failure never means verified safe");
        Check(ExecutionSafety.Analyze(new string(' ',500001)).Uncertainty!=null,"bounded analysis reports incomplete check");
        var review=ExecutionSafety.Analyze("DELETE FROM Orders;");int prompts=0;
        Check(!ExecutionSafety.Allow(review,r=>{prompts++;return false;},()=>true) && prompts==1,"cancel prevents execution");
        Check(ExecutionSafety.Allow(review,r=>{prompts++;return true;},()=>true) && prompts==2,"explicit approval covers only one attempt");
        Check(!ExecutionSafety.Allow(review,r=>true,()=>false),"stale text/editor/selection/connection rejects approval");
        Check(ExecutionSafety.Allow(ExecutionSafety.Analyze("SELECT 1;"),r=>{throw new Exception("unnecessary dialog");},()=>true),"clean read skips dialog");
        var serializer=new JavaScriptSerializer();Check(serializer.Deserialize<Preferences>("{}").ExecutionSafety,"old settings receive enabled default");
        Check(!serializer.Deserialize<Preferences>(serializer.Serialize(new Preferences{ExecutionSafety=false})).ExecutionSafety,"explicit opt-out persists");
        Console.WriteLine("PASS: "+checks+" execution-safety checks (outer WHERE, JOIN/TOP/transactions/CTEs, definitions/text, GO/selection offsets, uncertainty and one-attempt approval). No SQL executed.");
    }
}
