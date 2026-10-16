using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JoinAssist;

static class CompletionEnhancementTests
{
    static int count;
    static void Check(bool ok, string label) { count++; if(!ok) throw new Exception("Completion enhancements: " + label); }
    static CatalogSnapshot Catalog()
    {
        var catalog = new CatalogSnapshot();
        var orders = new CatalogObject { Id=1, Name="Orders", Schema="dbo", Kind="U", BareAllowed=true, Loaded=true };
        orders.Columns.AddRange(new[] { "Id", "CreatedByUserId", "ModifiedByUserId" }.Select(n=>new ColumnShape { Name=n, TypeName="int" }));
        var users = new CatalogObject { Id=2, Name="Users", Schema="dbo", Kind="U", BareAllowed=true, Loaded=true };
        users.Columns.Add(new ColumnShape { Name="Id", TypeName="int" }); catalog.Objects.Add(orders); catalog.Objects.Add(users); return catalog;
    }
    static ForeignKey Key(string name, string column)
    {
        var key = new ForeignKey { Id=column=="CreatedByUserId"?1:2, Name=name, ChildId=1, ParentId=2,
            ChildSchema="dbo", ChildTable="Orders", ParentSchema="dbo", ParentTable="Users" };
        key.Columns.Add(new KeyColumn { Ordinal=1, Child=column, Parent="Id" }); return key;
    }
    static AutomaticTrigger Trigger(string sql)
    {
        int caret=sql.IndexOf('|'); if(caret<0) caret=sql.Length; else sql=sql.Remove(caret,1);
        var trigger=AutomaticTrigger.TryCreate(sql,caret); if(trigger==null)throw new Exception("No trigger for " + sql); return trigger;
    }
    static List<JoinProposal> Columns(string sql)
    {
        var trigger=Trigger(sql);
        new ShapeResolver(Catalog(),CancellationToken.None).ResolveAsync(trigger.Context,false).GetAwaiter().GetResult();
        var sources=trigger.Qualifier.Length==0 ? trigger.Context.Tables : ShapeResolver.MatchTables(trigger.Context.Tables.Concat(trigger.Context.OuterTables),trigger.Qualifier);
        var result=trigger.OrderMatches(CompletionEngine.ColumnItems(sources,true));
        if(Environment.GetEnvironmentVariable("JOINASSIST_ENHANCEMENT_TRACE")=="1") Console.WriteLine("Columns: "+sql+" => "+string.Join(",",result.Select(p=>p.SearchName)));
        return result;
    }
    static List<string> Names(string sql)
    {
        var trigger=Trigger(sql);
        return trigger.OrderMatches(CompletionEngine.TableItems(Catalog(),trigger,null)).Select(p=>p.TargetTable).ToList();
    }
    internal static void Run()
    {
        Ranking(); Relationships(); LocalShapes();
        Console.WriteLine("PASS: " + count + " flexible matching, JOIN presentation and local-table checks. Synthetic catalog only; no SQL executed.");
    }
    static void Ranking()
    {
        Check(NameMatching.Score("prescriptionItemDetails_tb","itemdet")!=int.MaxValue,"infix case-insensitive match");
        Check(NameMatching.Score("order_item_details_tb","itemdet")!=int.MaxValue,"underscore-separated words match");
        Check(NameMatching.Score("OrderItemDetails","orditdet")!=int.MaxValue,"compact ordered abbreviation");
        Check(NameMatching.Score("Unrelated","itemdet")==int.MaxValue,"irrelevant object excluded");
        Check(NameMatching.Score("aVeryLongUnrelatedNameWithTooManyGapsZ","avz")==int.MaxValue,"widely scattered letters excluded");
        Check(NameMatching.Score("OrderItems","i")==int.MaxValue,"single letter remains prefix only");
        Check(NameMatching.Score("#Orders","@ord")==int.MaxValue,"table-variable marker not confused with temp table");
        Check(NameMatching.Score("Orders",new string('x',129))==int.MaxValue,"oversized prefix rejected before broader matching");
        var names=new[]{"prescriptionItemDetails_tb","itemdet","itemDetails_tb","order_item_details_tb","OrdItDet"};
        var proposals=names.Select(n=>new JoinProposal{TargetTable=n,CompletionText=n,SearchName=n}).ToList();
        var trigger=Trigger("SELECT * FROM itemdet"); var result=trigger.OrderMatches(proposals);
        Check(result[0].TargetTable=="itemdet" && result[1].TargetTable=="itemDetails_tb","exact and prefix precede broader matches");
        Check(result.IndexOf(proposals[0])<result.IndexOf(proposals[3]),"contiguous match before separator-folded match");
        Check(result.Select(p=>p.Caption).SequenceEqual(trigger.OrderMatches(proposals.AsEnumerable().Reverse()).Select(p=>p.Caption)),"broader results deterministic");
        trigger=Trigger("SELECT * FROM dbo.itemdet");
        Check(trigger.OrderMatches(new[]{new JoinProposal{TargetTable="prescriptionItemDetails_tb",TargetSchema="dbo"},new JoinProposal{TargetTable="itemdet",TargetSchema="archive"}}).Single().TargetSchema=="dbo","explicit schema stays exact");
        Check(Columns("SELECT o.modby| FROM Orders o").Single().SearchName=="ModifiedByUserId","abbreviation works for columns");
    }
    static void Relationships()
    {
        var context=Trigger("SELECT * FROM Orders o JOIN ").Context;
        new ShapeResolver(Catalog(),CancellationToken.None).ResolveAsync(context,true).GetAwaiter().GetResult();
        var keys=new[]{Key("FK_Orders_Creator","CreatedByUserId"),Key("FK_Orders_Modifier","ModifiedByUserId")};
        var choices=JoinBuilder.Build(context,keys);
        Check(choices.Count==2 && choices.All(p=>p.TargetTable=="Users"),"two roles to same target preserved");
        Check(choices.Single(p=>p.ForeignKeyName==keys[0].Name).Caption.Contains("o.CreatedByUserId = Users.Id"),"creator columns visible without selecting item");
        Check(choices.Single(p=>p.ForeignKeyName==keys[1].Name).Caption.Contains("o.ModifiedByUserId = Users.Id"),"modifier columns visible without selecting item");
        Check(choices.All(p=>p.Caption.Contains(p.ForeignKeyName)),"real FK name retained");
        Check(choices[0].Sql("INNER").Contains(" ON ") && !choices[0].Sql("INNER").Contains("\n"),"display details never inserted into SQL");
        keys[0].Columns.Add(new KeyColumn { Ordinal=2, Child="Id", Parent="Id" });keys[0].Disabled=true;
        choices=JoinBuilder.Build(context,keys);
        Check(choices[0].Caption.Contains(" AND ") && choices[0].Caption.Contains("FK disabilitata"),"composite condition and warning visible");
        context=Trigger("SELECT * FROM Orders o JOIN Users u ON ").Context;
        new ShapeResolver(Catalog(),CancellationToken.None).ResolveAsync(context,true).GetAwaiter().GetResult();
        Check(JoinBuilder.Build(context,keys).All(p=>p.Caption.Contains("u.Id")),"ON details use existing aliases");
    }
    static void LocalShapes()
    {
        const string temp="CREATE TABLE #Orders (OrderId int, [Item Name] nvarchar(40)); ";
        const string variable="DECLARE @Items TABLE (ItemId int, ItemName nvarchar(40)); ";
        Check(Names(temp+"SELECT * FROM #Or").SequenceEqual(new[]{"#Orders"}),"temp table name completion");
        Check(Names(variable+"SELECT * FROM @It").SequenceEqual(new[]{"@Items"}),"table-variable name completion");
        Check(Columns(temp+"SELECT #Orders.Or| FROM #Orders").Single().CompletionText=="OrderId","temp columns before FROM");
        Check(Columns(temp+"SELECT t.Item| FROM #Orders t").Single().CompletionText=="[Item Name]","quoted column from temp alias");
        Check(Columns(variable+"SELECT i.ItemN| FROM @Items i").Single().CompletionText=="ItemName","table-variable alias columns");
        Check(Columns(variable+"SELECT @Items.Item| FROM @Items").Count==2,"table-variable qualifier without alias");
        Check(SqlName.Format("@Items")=="@Items" && SqlName.Format("#Orders")=="#Orders","local SQL names not unnecessarily quoted");
        Check(AutomaticTrigger.TryCreate(variable+"SELECT * FROM @Items WHERE @scalar",(variable+"SELECT * FROM @Items WHERE @scalar").Length)==null,"scalar variables do not trigger column suggestions");
        Check(Columns(temp+"SELECT * FROM #Orders WHERE Ord").Single().SearchName=="OrderId","unqualified temp columns in WHERE");
        Check(Columns("SELECT Id AS OrderId, CreatedByUserId INTO #Copy FROM Orders; SELECT c.Or| FROM #Copy c").Single().SearchName=="OrderId","SELECT INTO named direct projection");
        Check(Columns("SELECT * INTO #Copy FROM Orders; SELECT c.Mod| FROM #Copy c").Single().SearchName=="ModifiedByUserId","SELECT INTO star expansion from catalog");
        Check(Columns("WITH x AS (SELECT Id AS OrderId FROM Orders) SELECT * INTO #Copy FROM x; SELECT c.Or| FROM #Copy c").Single().SearchName=="OrderId","SELECT INTO from CTE");
        Check(Columns(temp+"SELECT OrderId AS CopiedId INTO #Copy FROM #Orders; DROP TABLE #Orders; CREATE TABLE #Orders (Other int); SELECT c.Cop| FROM #Copy c").Single().SearchName=="CopiedId","SELECT INTO retains source shape at creation");
        Check(Columns("SELECT COUNT(*) AS Total INTO #Counts FROM Orders; SELECT c.To| FROM #Counts c").Single().SearchName=="Total","named computed expression column");
        Check(Columns(temp+"WITH c AS (SELECT OrderId AS Id FROM #Orders) SELECT c.I| FROM c").Single().SearchName=="Id","CTE can project local columns");
        Check(Columns(variable+"SELECT d.Item| FROM (SELECT ItemId FROM @Items) d").Single().SearchName=="ItemId","derived query can project table-variable columns");
        Check(Columns(temp+"SELECT * FROM Orders o WHERE EXISTS(SELECT 1 FROM #Orders t WHERE t.Or|)").Single().SearchName=="OrderId","local source in nested scope");
        Check(!Names(temp+"DROP TABLE #Orders; SELECT * FROM #Or").Contains("#Orders"),"dropped table not proposed");
        Check(!Names(temp+"DROP TABLE dbo.#Orders; SELECT * FROM #Or").Contains("#Orders"),"qualified drop invalidates bare local name");
        Check(!Names(temp+"ALTER TABLE tempdb.dbo.#Orders ADD Added int; SELECT * FROM #Or").Contains("#Orders"),"qualified alter cannot leave stale local columns");
        Check(Columns(temp+"DROP TABLE #Orders; CREATE TABLE #Orders(NewColumn int); SELECT t.New| FROM #Orders t").Single().SearchName=="NewColumn","drop and recreate replaces shape");
        Check(Columns(temp+"\nGO\nSELECT t.Or| FROM #Orders t").Single().SearchName=="OrderId","temp survives GO within script");
        Check(!Names(variable+"\nGO\nSELECT * FROM @It").Contains("@Items"),"table variable does not cross GO");
        Check(!Names("SELECT * FROM #Fu|; CREATE TABLE #Future(Id int);").Contains("#Future"),"future declarations are invisible");
        Check(!Names("IF 1=1 CREATE TABLE #Maybe(Id int); SELECT * FROM #May").Contains("#Maybe"),"conditional creation not assumed");
        Check(!Names(temp+"IF 1=1 DROP TABLE #Orders; SELECT * FROM #Or").Contains("#Orders"),"conditional drop invalidates shape");
        Check(!Names(temp+"ALTER TABLE #Orders ADD Added int; SELECT * FROM #Or").Contains("#Orders"),"alter invalidates rather than suggesting stale columns");
        Check(!Names(temp+"IF 1=1 ALTER TABLE #Orders DROP COLUMN OrderId; SELECT * FROM #Or").Contains("#Orders"),"conditional alteration invalidates shape");
        Check(!Names(temp+"EXEC(N'DROP TABLE #Orders'); SELECT * FROM #Or").Contains("#Orders"),"dynamic execution invalidates temp assumptions");
        Check(!Names("-- CREATE TABLE #Fake(Id int);\nSELECT * FROM #Fa").Contains("#Fake"),"comment is not a declaration");
        Check(!Names("PRINT 'CREATE TABLE #Fake(Id int)'; SELECT * FROM #Fa").Contains("#Fake"),"string is not a declaration");
        Check(Columns("SELECT t.Id| FROM #Unknown t").Count==0,"unknown local table never falls back to real table");
        using(var db=new FakeDatabase())
        {
            var localTrigger=Trigger(temp+"SELECT #Orders.Or");
            var items=CompletionEngine.LoadAsync(db,localTrigger,CancellationToken.None).GetAwaiter().GetResult();
            Check(localTrigger.OrderMatches(items).Single().SearchName=="OrderId","full completion pipeline resolves local qualifier before FROM is written");
        }
        var trigger=Trigger("SELECT * INTO #Copy FROM Orders; SELECT * FROM #Copy JOIN ");
        new ShapeResolver(Catalog(),CancellationToken.None).ResolveAsync(trigger.Context,true).GetAwaiter().GetResult();
        Check(JoinBuilder.Build(trigger.Context,new[]{Key("FK_Creator","CreatedByUserId")}).Count==0,"SELECT INTO does not inherit foreign keys");
        trigger=Trigger(temp+"SELECT * FROM Orders o JOIN #Orders t ON o.Id=t.OrderId JOIN ");
        new ShapeResolver(Catalog(),CancellationToken.None).ResolveAsync(trigger.Context,true).GetAwaiter().GetResult();
        Check(JoinBuilder.Build(trigger.Context,new[]{Key("FK_Creator","CreatedByUserId")}).Count==1,"local table does not prevent real FK suggestions from other sources");
        trigger=Trigger(variable+"SELECT * FROM Orders JOIN @It");
        Check(trigger.Insertion(CompletionEngine.TableItems(Catalog(),trigger,null).Single(p=>p.TargetTable=="@Items"))=="INNER JOIN @Items ","JOIN local name completion does not invent ON");
    }
}
