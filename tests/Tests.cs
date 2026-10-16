using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using JoinAssist;

class Tests
{
    static int count;
    static void Check(bool ok,string label) { count++; if(Environment.GetEnvironmentVariable("JOINASSIST_TEST_TRACE")=="1")Console.WriteLine(count+" "+label); if(!ok)throw new Exception(label); }
    static AutomaticTrigger Auto(string sql) { return AutomaticTrigger.TryCreate(sql,sql.Length); }
    static QueryContext Join(string sql) { return QueryAnalyzer.Analyze(sql,sql.Length); }
    static void Reject(string sql) { bool rejected=false;try{Join(sql);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Rifiuto: "+sql); }
    static CatalogObject Obj(int id,string schema,string name,string kind,bool bare,params string[] columns)
    {
        var obj=new CatalogObject{Id=id,Schema=schema,Name=name,Kind=kind,BareAllowed=bare,Loaded=true};
        obj.Columns.AddRange(columns.Select(c=>new ColumnShape{Name=c,TypeName="int"}));return obj;
    }
    static CatalogSnapshot Catalog()
    {
        var c=new CatalogSnapshot();
        c.Objects.Add(Obj(1,"dbo","Orders","U",true,"Id","CustomerId","TenantId","select","Order Date"));
        c.Objects.Add(Obj(2,"crm","Customers","U",true,"Id","TenantId","Name"));
        c.Objects.Add(Obj(3,"dbo","Items","U",true,"Id","OrderId"));
        c.Objects.Add(Obj(4,"archive","Orders","U",false,"ArchivedId"));
        return c;
    }
    static ForeignKey Key(int id,int child,int parent,string cs,string ct,string ps,string pt,string childCol,string parentCol)
    {
        var key=new ForeignKey{Id=id,ChildId=child,ParentId=parent,Name="FK_"+ct+"_"+pt,ChildSchema=cs,ChildTable=ct,ParentSchema=ps,ParentTable=pt};
        key.Columns.Add(new KeyColumn{Ordinal=1,Child=childCol,Parent=parentCol});return key;
    }
    static ForeignKey OrdersCustomers() { return Key(100,1,2,"dbo","Orders","crm","Customers","CustomerId","Id"); }
    static ForeignKey ItemsOrders() { return Key(101,3,1,"dbo","Items","dbo","Orders","OrderId","Id"); }
    static void Resolve(QueryContext context,CatalogSnapshot catalog=null) { new ShapeResolver(catalog??Catalog(),CancellationToken.None).ResolveAsync(context,true).GetAwaiter().GetResult(); }
    static List<JoinProposal> Proposals(string sql,params ForeignKey[] keys) { var t=Auto(sql);Check(t!=null,"Trigger: "+sql);Resolve(t.Context);return JoinBuilder.Build(t.Context,keys); }
    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--updates-live")
        { try { UpdateTests.Live().GetAwaiter().GetResult(); return 0; } catch(Exception ex) { Console.Error.WriteLine("FAIL " + ex); return 1; } }
        string ide=args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{foreach(var d in new[]{ide,ide+@"\Extensions\Application",ide+@"\CommonExtensions\Microsoft\Editor",ide+@"\PublicAssemblies",ide+@"\PrivateAssemblies"}){string f=Path.Combine(d,new AssemblyName(e.Name).Name+".dll");if(File.Exists(f))return Assembly.LoadFrom(f);}return null;};
        try { Formatting();Scopes();Shapes();Triggers();ExtendedCases();Ranking();Database(); Console.WriteLine("PASS: "+count+" verifiche su formato, scope, CTE/viste/sottoquery, FK, completamento nomi/colonne, ordinamento e catalogo simulato. Nessun server contattato."); CompletionEnhancementTests.Run(); QueryContextRegressionTests.Run(); UpdateTests.Run().GetAwaiter().GetResult(); return 0; }
        catch(Exception ex){Console.Error.WriteLine("FAIL "+ex);return 1;}
    }
    static void Formatting()
    {
        Check(SqlName.Format("Orders")=="Orders","Nome ordinario senza parentesi");
        Check(SqlName.Format("select")=="[select]","Parola riservata delimitata");
        Check(SqlName.Format("Order Date")=="[Order Date]","Spazi delimitati");
        Check(SqlName.Format("a]b")=="[a]]b]","Chiusura parentesi escapata");
        Check(SqlName.Format("a; DROP TABLE t")=="[a; DROP TABLE t]","Identificatore non diventa SQL");
        var fk=OrdersCustomers();var ctx=Join("SELECT * FROM Orders");Resolve(ctx);
        var p=JoinBuilder.Build(ctx,new[]{fk}).Single();
        Check(p.Sql("INNER")=="INNER JOIN Customers ON Orders.CustomerId = Customers.Id","Default senza schema, alias o parentesi");
        Check(p.Sql("LEFT").StartsWith("LEFT JOIN Customers ON "),"LEFT rispettato");
        ctx=Join("SELECT * FROM dbo.Orders AS o");Resolve(ctx);
        Check(JoinBuilder.Build(ctx,new[]{fk}).Single().Predicate=="o.CustomerId = Customers.Id","Alias manuale mantenuto");
        fk.ParentBare=false;
        Check(JoinBuilder.Build(ctx,new[]{fk}).Single().Sql("INNER").StartsWith("INNER JOIN crm.Customers ON o.CustomerId = crm.Customers.Id"),"Schema solo se necessario al binding");
        Check(JoinBuilder.Build(ctx,new[]{fk}).Single().Warning.Contains("schema necessario"),"Motivo schema in elenco");
        fk.ParentBare=true;fk.Columns.Insert(0,new KeyColumn{Ordinal=2,Child="TenantId",Parent="TenantId"});
        Check(JoinBuilder.Build(ctx,new[]{fk}).Single().Predicate=="o.CustomerId = Customers.Id AND o.TenantId = Customers.TenantId","FK composta e ordine colonne");
        fk.Disabled=true;Check(JoinBuilder.Build(ctx,new[]{fk}).Single().Warning.Contains("disabilitata"),"Vincolo disabilitato segnalato");
        fk.Disabled=false;fk.Untrusted=true;Check(JoinBuilder.Build(ctx,new[]{fk}).Single().Warning.Contains("non verificata"),"Vincolo non verificato segnalato");
        var other=OrdersCustomers();other.Name="FK_Alternative";
        Check(JoinBuilder.Build(ctx,new[]{fk,other}).Count==2,"FK multiple distinte");
        Check(JoinBuilder.Build(ctx,new ForeignKey[0]).Count==0,"Nessuna FK non inventa uguaglianze");
        var self=Key(104,1,1,"dbo","Orders","dbo","Orders","CustomerId","Id");
        var selfRows=JoinBuilder.Build(ctx,new[]{self});
        Check(selfRows.Count==2 && selfRows.All(x=>x.Alias=="j"),"Self join: alias necessario nelle due direzioni");
        Check(selfRows.All(x=>x.Warning.Contains("alias necessario")),"Eccezione alias spiegata");
        var used=Join("SELECT * FROM Orders j");Resolve(used);
        Check(JoinBuilder.Build(used,new[]{self}).All(x=>x.Alias=="j2"),"Alias necessario non collide");
        var incoming=Join("SELECT * FROM Customers");Resolve(incoming);
        Check(JoinBuilder.Build(incoming,new[]{OrdersCustomers()}).Single().Predicate=="Customers.Id = Orders.CustomerId","FK in entrata");
        var unusual=Key(105,1,6,"dbo","Orders","strange schema","Order Date","select","key value");unusual.ParentBare=false;
        Check(JoinBuilder.Build(ctx,new[]{unusual}).Single().Predicate=="o.[select] = [strange schema].[Order Date].[key value]","Delimitatori solo per nomi particolari");
        var sample=new CatalogSnapshot();sample.Objects.Add(Obj(10,"dbo","orders_tb","U",true,"OrderId"));sample.Objects.Add(Obj(11,"dbo","orderItems_tb","U",true,"OrderItemId","OrderId"));sample.Objects.Add(Obj(12,"dbo","orderItemDetails_tb","U",true,"OrderItemId"));
        var a=Key(201,11,10,"dbo","orderItems_tb","dbo","orders_tb","OrderId","OrderId");
        var b=Key(202,12,11,"dbo","orderItemDetails_tb","dbo","orderItems_tb","OrderItemId","OrderItemId");
        var q=Join("select * from orders_tb");Resolve(q,sample);var first=JoinBuilder.Build(q,new[]{a,b}).Single();
        Check(first.Sql("INNER")=="INNER JOIN orderItems_tb ON orders_tb.OrderId = orderItems_tb.OrderId","Esempio richiesto: primo JOIN esatto");
        q=Join("select * from orders_tb "+first.Sql("INNER"));Resolve(q,sample);var second=JoinBuilder.Build(q,new[]{a,b}).Single(x=>x.TargetTable=="orderItemDetails_tb");
        Check(second.Sql("INNER")=="INNER JOIN orderItemDetails_tb ON orderItems_tb.OrderItemId = orderItemDetails_tb.OrderItemId","Esempio richiesto: secondo JOIN esatto");
    }
    static void Scopes()
    {
        Check(Join("SELECT * FROM Orders").Tables.Single().Name=="Orders","Tabella semplice");
        Check(Join("SELECT * FROM Orders o LEFT OUTER JOIN ").JoinKind=="LEFT","LEFT OUTER conservato");
        Check(Join("SELECT * FROM Orders o RIGHT JOIN ").JoinKind=="RIGHT","RIGHT aggiunto");
        Check(Join("SELECT * FROM Orders o FULL OUTER JOIN ").JoinKind=="FULL","FULL aggiunto");
        Check(Join("SELECT * FROM Orders; SELECT * FROM Items").Tables.Single().Name=="Items","Separazione istruzioni");
        Check(Join("SELECT * FROM Orders\nGO\nSELECT * FROM Items").Tables.Single().Name=="Items","Separazione batch");
        Check(Join("SELECT (SELECT MAX(Id) FROM Customers) FROM Orders").Tables.Single().Name=="Orders","Sottoquery scalare non confonde i JOIN esterni");
        Check(Join("SELECT * FROM Customers c, Orders o JOIN ").Tables.Single().Alias=="o","FROM con virgole: ON vede solo il ramo JOIN corretto");
        Check(Join("SELECT * FROM Orders WITH (NOLOCK) JOIN ").Tables.Count==1,"Hint compatibile con colonne reali");
        Check(Join("SELECT * FROM Orders o CROSS JOIN Customers c JOIN ").Tables.Count==2,"CROSS JOIN già completo");
        Reject("SELECT * FROM Orders o JOIN Customers o ON o.Id=o.Id");
        Reject("USE OtherDb; SELECT * FROM Orders");Reject("SELECT * FROM OtherDb.dbo.Orders");Check(Join("SELECT * FROM #Orders").Tables.Single().IsLocal,"Tabella temporanea riconosciuta senza inventarne le colonne");Reject("DELETE FROM Orders");Reject("SELECT * FROM Orders WHERE Id=1");Reject("SELECT * FROM Orders -- comment");Reject("SELECT * FROM Orders CROSS JOIN ");
        var nested="SELECT * FROM Orders o WHERE EXISTS (SELECT * FROM Items i JOIN  )";int caret=nested.IndexOf("  )")+1;
        var trigger=AutomaticTrigger.TryCreate(nested,caret);Check(trigger!=null && trigger.Context.Tables.Single().Alias=="i","JOIN nella sottoquery usa il suo scope");
        string cte="WITH c AS (SELECT * FROM Orders JOIN  ) SELECT * FROM c";caret=cte.IndexOf("  )")+1;
        trigger=AutomaticTrigger.TryCreate(cte,caret);Check(trigger!=null && trigger.Context.Tables.Single().Name=="Orders","JOIN nel corpo CTE con testo successivo");
        Check(Auto("WITH c AS (SELECT * FROM Orders JOIN ")!=null,"JOIN dentro CTE ancora aperta");
        var on=Auto("SELECT * FROM Orders o JOIN Customers c ON ");Check(on!=null && on.Context.PredicateOnly,"ON attivo");Resolve(on.Context);
        Check(JoinBuilder.Build(on.Context,new[]{OrdersCustomers()}).Single().Predicate=="o.CustomerId = c.Id","ON usa alias esistenti senza aggiungere JOIN");
        var noAlias=Proposals("SELECT * FROM Orders\nJOIN Items ON \n",ItemsOrders());Check(noAlias.Single().Predicate=="Orders.Id = Items.OrderId","ON senza alias e a capo");
        Check(Auto("SELECT * FROM Orders a JOIN (Customers b JOIN Items c ON b.Id=c.Id) ON ")==null,"Target gruppo parentesi non scambiato per una tabella");
        var inside="SELECT * FROM Orders a JOIN (Customers b JOIN Items c ON  ) ON a.Id=b.Id";caret=inside.IndexOf("  )")+1;
        var inner=AutomaticTrigger.TryCreate(inside,caret);Check(inner!=null && inner.Context.Tables.All(t=>t.Alias!="a"),"ON interno non vede il ramo esterno");
    }
    static void Shapes()
    {
        var fk=OrdersCustomers();
        var p=Proposals("WITH c AS (SELECT Id, CustomerId FROM Orders) SELECT * FROM c JOIN ",fk);
        Check(p.Any(x=>x.Predicate=="c.CustomerId = Customers.Id" && x.Alias==null),"FK da CTE con colonne dirette");
        p=Proposals("WITH c AS (SELECT CustomerId AS Buyer FROM Orders) SELECT * FROM c x JOIN ",fk);
        Check(p.Any(x=>x.Predicate=="x.Buyer = Customers.Id"),"FK da CTE con colonna rinominata");
        p=Proposals("WITH c(Buyer) AS (SELECT CustomerId FROM Orders) SELECT * FROM c JOIN ",fk);
        Check(p.Any(x=>x.Predicate=="c.Buyer = Customers.Id"),"Elenco colonne CTE esplicito");
        p=Proposals("WITH c AS (SELECT * FROM Orders), d AS (SELECT CustomerId AS Buyer FROM c) SELECT * FROM d JOIN ",fk);
        Check(p.Any(x=>x.Predicate=="d.Buyer = Customers.Id"),"Catena di CTE e SELECT star");
        p=Proposals("WITH c AS (SELECT CustomerId+1 AS Buyer FROM Orders) SELECT * FROM c JOIN ",fk);
        Check(p.Count==0,"Colonna calcolata non diventa FK");
        p=Proposals("WITH c AS (SELECT Id FROM Orders) SELECT * FROM c JOIN ",fk);
        Check(p.Count==0,"Colonna FK non esposta: nessuna proposta");
        var composite=OrdersCustomers();composite.Columns.Add(new KeyColumn{Ordinal=2,Child="TenantId",Parent="TenantId"});
        p=Proposals("WITH c AS (SELECT CustomerId FROM Orders) SELECT * FROM c JOIN ",composite);Check(p.Count==0,"FK composta richiede tutte le colonne esposte");
        p=Proposals("SELECT * FROM (SELECT CustomerId AS Buyer FROM Orders) q JOIN ",fk);Check(p.Any(x=>x.Predicate=="q.Buyer = Customers.Id"),"Sottoquery derivata con origine colonna");
        p=Proposals("WITH c AS (SELECT Id AS BuyerId FROM Customers) SELECT * FROM Orders o JOIN c ON ",fk);Check(p.Single().Predicate=="o.CustomerId = c.BuyerId","ON verso una CTE");
        p=Proposals("WITH a AS (SELECT CustomerId AS Buyer FROM Orders), b AS (SELECT Id AS CustomerKey FROM Customers) SELECT * FROM a JOIN b ON ",fk);Check(p.Single().Predicate=="a.Buyer = b.CustomerKey","ON tra due CTE");
        p=Proposals("WITH buyers AS (SELECT Id FROM Customers) SELECT * FROM Orders JOIN ",fk);Check(p.Any(x=>x.TargetTable=="buyers" && x.Predicate=="Orders.CustomerId = buyers.Id"),"CTE correlata offerta anche come target del JOIN");
        var union=Auto("WITH c AS (SELECT CustomerId FROM Orders UNION ALL SELECT Id FROM Customers) SELECT * FROM c JOIN ");Resolve(union.Context);Check(JoinBuilder.Build(union.Context,new[]{fk}).Count==0 && union.Context.Tables[0].Columns.Single().Name=="CustomerId","UNION espone nomi ma non inventa provenienza unica");
        var recursive=Auto("WITH c(Id) AS (SELECT Id FROM Orders UNION ALL SELECT c.Id FROM c) SELECT * FROM c JOIN ");Resolve(recursive.Context);Check(recursive.Context.Tables[0].Columns.Single().Name=="Id" && JoinBuilder.Build(recursive.Context,new[]{fk}).Count==0,"CTE ricorsiva termina e non inventa FK");
        var catalog=Catalog();var view=Obj(8,"dbo","OrderView","V",true,"OrderKey","Buyer");view.Definition="CREATE VIEW dbo.OrderView AS SELECT Id, CustomerId FROM dbo.Orders";catalog.Objects.Add(view);
        var context=Join("SELECT * FROM OrderView JOIN ");Resolve(context,catalog);Check(JoinBuilder.Build(context,new[]{fk}).Single().Predicate=="OrderView.Buyer = Customers.Id","Vista: nomi del catalogo e provenienza dalla definizione");
        view.Definition=null;context=Join("SELECT * FROM OrderView JOIN ");Resolve(context,catalog);Check(context.Tables[0].Columns.Count==2 && JoinBuilder.Build(context,new[]{fk}).Count==0,"Vista senza definizione: colonne reali, nessuna FK indovinata");
        var synonym=Obj(9,"dbo","OrderAlias","SN",true);synonym.SynonymTarget="[dbo].[Orders]";catalog.Objects.Add(synonym);
        context=Join("SELECT * FROM OrderAlias JOIN ");Resolve(context,catalog);Check(JoinBuilder.Build(context,new[]{fk}).Single().Predicate=="OrderAlias.CustomerId = Customers.Id","Sinonimo locale risolto alla tabella");
        synonym.SynonymTarget="[OtherDb].[dbo].[Orders]";context=Join("SELECT * FROM OrderAlias JOIN ");Resolve(context,catalog);Check(context.Tables[0].Columns.Count==0 && JoinBuilder.Build(context,new[]{fk}).Count==0,"Sinonimo cross database non indovinato");
        var fn=Obj(10,"dbo","GetOrders","IF",true,"Id","CustomerId");catalog.Objects.Add(fn);context=Join("SELECT * FROM dbo.GetOrders(1) f JOIN ");Resolve(context,catalog);Check(context.Tables[0].Columns.Count==2 && JoinBuilder.Build(context,new[]{fk}).Count==0,"Funzione tabellare: colonne senza FK inventate");
        context=Join("SELECT * FROM Orders o OUTER APPLY (SELECT Id FROM Items WHERE OrderId=o.Id) i JOIN ");Resolve(context,catalog);Check(context.Tables.Count==2,"APPLY: sorgenti dirette e derivate riconosciute");
        var shadow=Auto("WITH Customers AS (SELECT Id FROM Orders) SELECT * FROM Orders JOIN ");Resolve(shadow.Context);Check(JoinBuilder.Build(shadow.Context,new[]{fk}).Any(x=>x.Target=="crm.Customers"),"Schema conservato se una CTE oscura il nome fisico");
    }
    static void Triggers()
    {
        Check(Auto("SELECT * FROM Orders JOIN")==null,"Aspetta spazio dopo JOIN");
        Check(Auto("SELECT * FROM Orders JOIN Cu").Mode==CompletionMode.Join,"Prefisso JOIN");
        Check(Auto("SELECT * FROM Orders LEFT OUTER JOIN crm.Cu").Prefix.SequenceEqual(new[]{"crm","Cu"}),"Schema e prefisso JOIN");
        Check(Auto("SELECT * FROM Orders JOIN [crm].[Cus").Prefix.SequenceEqual(new[]{"crm","Cus"}),"Nome delimitato incompleto nel JOIN");
        Check(Auto("SELECT * FROM Orders JOIN Customers ")==null,"Non interferisce con alias dopo tabella");
        Check(Auto("SELECT * FROM Orders JOIN Customers ON c.").Mode==CompletionMode.Column,"Colonna in ON iniziato");
        Check(Auto("SELECT * FROM Orders JOIN -- comment")==null && Auto("SELECT 'JOIN ")==null,"Commenti e stringhe esclusi");
        Check(Auto("SELECT * FROM ").Mode==CompletionMode.Table,"FROM spazio propone tabelle");
        var a=Auto("SELECT * FROM Ord");Check(a.Mode==CompletionMode.Table && a.Prefix.Single()=="Ord","Nome tabella fuori JOIN");
        Check(a.BaseKey==Auto("SELECT * FROM Order").BaseKey,"Filtrare nome non ricarica catalogo");
        Check(Auto("SELECT * FROM dbo.Or").Prefix.SequenceEqual(new[]{"dbo","Or"}),"Schema fuori JOIN");
        Check(Auto("UPDATE Ord").Mode==CompletionMode.Table && Auto("INSERT INTO Ord").Mode==CompletionMode.Table,"Tabelle dopo UPDATE/INSERT INTO senza eseguire DML");
        Check(Auto("SELECT * FROM Orders o, Cus").Mode==CompletionMode.Table,"Seconda tabella dopo virgola FROM");
        var cols=Auto("SELECT * FROM Orders WHERE Orders.Cu");Check(cols.Mode==CompletionMode.Column && cols.Qualifier.Single()=="Orders","Colonna dopo nome tabella");
        Check(cols.BaseKey==Auto("SELECT * FROM Orders WHERE Orders.Cus").BaseKey,"Filtrare colonne non ricarica metadati");
        Check(Auto("SELECT * FROM Orders o WHERE o.").Mode==CompletionMode.Column,"Colonne dopo alias punto senza prefisso");
        Check(Auto("SELECT * FROM Orders WHERE dbo.Orders.Cu").Qualifier.SequenceEqual(new[]{"dbo","Orders"}),"Qualificatore schema.tabella.colonna");
        string select="SELECT o.Cu FROM Orders o";int caret=select.IndexOf("Cu")+2;var t=AutomaticTrigger.TryCreate(select,caret);Check(t!=null && t.Mode==CompletionMode.Column && t.Context.Tables.Single().Alias=="o","Colonna nel SELECT vede FROM successivo");
        string middle="SELECT o.CustomerId FROM Orders o";caret=middle.IndexOf("CustomerId")+3;t=AutomaticTrigger.TryCreate(middle,caret);Check(t.Context.ReplaceLength=="CustomerId".Length,"Completamento nel mezzo del nome sostituisce anche il resto");
        string correlated="SELECT * FROM Orders o WHERE EXISTS (SELECT 1 FROM Items i WHERE o.Cu)";caret=correlated.IndexOf("o.Cu")+4;t=AutomaticTrigger.TryCreate(correlated,caret);Check(t.Context.Tables.Single().Alias=="i" && t.Context.OuterTables.Any(x=>x.Alias=="o"),"Sottoquery correlata: alias locale ed esterno distinti");
        Check(Auto("SELECT * FROM Orders WHERE @variable")==null,"Variabili non trattate come nomi di colonne");
        var catalog=Catalog();var names=CompletionEngine.TableItems(catalog,a,null);Check(names.Any(x=>x.CompletionText=="Orders"),"Tab completa tabella senza alias/schema");
        Check(names.Any(x=>x.CompletionText=="archive.Orders"),"Omonimo in altro schema resta qualificato");
        Check(a.Matches(names.Single(x=>x.CompletionText=="Orders")),"Filtro nomi tabelle");
        var context=Join("SELECT * FROM Orders o JOIN Customers c ON o.CustomerId=c.Id");Resolve(context);var columns=CompletionEngine.ColumnItems(context.Tables,true);
        Check(columns.Count(x=>x.SearchName=="Id")==2 && columns.Where(x=>x.SearchName=="Id").All(x=>x.CompletionText.Contains(".")),"Colonne omonime in espressione qualificate");
        Check(columns.Single(x=>x.SearchName=="CustomerId").CompletionText=="CustomerId","Colonna univoca semplice");
        Check(columns.Single(x=>x.SearchName=="Order Date").CompletionText=="[Order Date]","Colonna con spazio delimitata");
        var plain=CompletionEngine.ColumnItems(new[]{context.Tables[0]},false);Check(plain.Single(x=>x.SearchName=="CustomerId").CompletionText=="CustomerId","Dopo punto inserisce soltanto il nome colonna");
        var item=new JoinProposal{CompletionText="CustomerId",SearchName="CustomerId"};Check(cols.Matches(item) && cols.Insertion(item)=="CustomerId","Tab colonna non aggiunge JOIN o spazi");
        var cteTrigger=Auto("WITH c AS (SELECT Id FROM Orders) SELECT * FROM c");Check(cteTrigger.Context.Ctes.ContainsKey("c") && CompletionEngine.TableItems(catalog,cteTrigger,null).Any(x=>x.CompletionText=="c"),"CTE nel completamento nomi");
        var fkTrigger=Auto("SELECT * FROM Orders JOIN ");Resolve(fkTrigger.Context);var rows=CompletionEngine.BuildJoinItems(catalog,fkTrigger,new[]{OrdersCustomers()});Check(rows.Any(x=>!x.IsName)&&rows.Any(x=>x.IsName&&x.TargetTable=="Items"),"JOIN: FK prima, altri nomi disponibili come fallback");
    }
    static void Database()
    {
        using(var db=new FakeDatabase())
        {
            var context=Join("SELECT * FROM Orders o");var keys=MetadataReader.ReadAsync(db,context,CancellationToken.None).GetAwaiter().GetResult();
            Check(context.Tables[0].ObjectId==1 && context.Tables[0].Columns.Count==3,"Catalogo reale simulato: tabella e colonne");
            Check(keys.Count==2 && keys.Single(k=>k.Id==100).Columns.Count==2,"FK composte raggruppate per vincolo");
            Check(JoinBuilder.Build(context,keys).Count==2,"Catalogo a generazione JOIN nelle due direzioni");
            Check(db.Commands.Count==3 && db.Commands.All(c=>c.CommandTimeout==10),"Solo tre SELECT mirate con timeout");
            Check(db.Commands[1].Parameters["@objectId"].Value.Equals(1),"ID colonne parametrizzato");
            Check(db.Commands[2].Parameters["@id0"].Value.Equals(1),"ID FK parametrizzato");
            Check(db.Commands.All(c=>!c.CommandText.Contains("Orders")),"Nomi utente non interpolati nelle query metadati");
        }
        using(var db=new FakeDatabase())
        {
            var items=CompletionEngine.LoadAsync(db,Auto("SELECT * FROM Ord"),CancellationToken.None).GetAwaiter().GetResult();Check(items.Any(x=>x.CompletionText=="Orders") && db.Commands.Count==1,"Completamento tabelle legge solo i nomi degli oggetti");
        }
        using(var db=new FakeDatabase())
        {
            var items=CompletionEngine.LoadAsync(db,Auto("SELECT * FROM Orders o WHERE o.Cu"),CancellationToken.None).GetAwaiter().GetResult();Check(items.Any(x=>x.CompletionText=="CustomerId") && db.Commands.Count==2,"Completamento colonne legge nomi e sole colonne necessarie");
        }
        using(var db=new FakeDatabase{MissingTable=true})
        {
            var context=Join("SELECT * FROM Orders");var keys=MetadataReader.ReadAsync(db,context,CancellationToken.None).GetAwaiter().GetResult();Check(keys.Count==0 && JoinBuilder.Build(context,keys).Count==0,"Sorgente assente non genera FK per somiglianza");
        }
        using(var db=new FakeDatabase())using(var cts=new CancellationTokenSource())
        {
            cts.Cancel();bool canceled=false;try{MetadataReader.ReadAsync(db,Join("SELECT * FROM Orders"),cts.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}Check(canceled,"Richieste annullabili");
        }
    }
    static JoinProposal Named(string name,string schema="dbo")
    {
        return new JoinProposal { TargetTable=name,TargetSchema=schema,SearchName=name,CompletionText=name,DisplayText=name+" | "+schema };
    }
    static void Ranking()
    {
        var trigger=Auto("SELECT * FROM custo");
        var history=Named("customeractivityhistory_vw");var customers=Named("customers_tb");var revisions=Named("customersrev_tb");
        var input=new[]{history,revisions,customers};
        Check(trigger.OrderMatches(input).SequenceEqual(new[]{customers,revisions,history}),"Ordine richiesto: customers_tb, customersrev_tb, customeractivityhistory_vw");
        Check(Auto("SELECT * FROM CUSTO").OrderMatches(input).SequenceEqual(new[]{customers,revisions,history}),"Ordinamento indipendente da maiuscole");
        var exact=Named("customers_tb");var longer=Named("customers_tb_history");
        Check(Auto("SELECT * FROM customers_tb").OrderMatches(new[]{longer,exact}).First()==exact,"Nome esatto prima delle estensioni del nome");
        var unrelated=Named("Orders");Check(trigger.OrderMatches(input.Concat(new[]{unrelated})).Count==3,"Nessun nome estraneo al prefisso aggiunto");
        var a=Named("customers_vw","very_long_schema_name");var b=Named("customers_tb","x");
        Check(trigger.OrderMatches(new[]{a,b}).First()==b,"A pari lunghezza ordinamento alfabetico, nessun vantaggio al nome schema corto");
        var closestOtherSchema=Named("customer","a_very_long_schema");var longerDefault=Named("customers_tb","dbo");
        Check(trigger.OrderMatches(new[]{longerDefault,closestOtherSchema}).First()==closestOtherSchema,"Distanza sul solo nome, senza conteggiare lo schema");
        var schemaOnly=Named("a","custo_schema");Check(trigger.OrderMatches(new[]{schemaOnly,history}).First()==history,"Prefisso del nome prima di un match sul solo schema");
        Check(Auto("SELECT * FROM archive.custo").OrderMatches(new[]{Named("customers_tb","dbo"),Named("customers_tb","archive")}).Single().TargetSchema=="archive","Filtro schema qualificato mantenuto");
        Check(Auto("SELECT * FROM ").OrderMatches(input).SequenceEqual(input),"Nessun prefisso: ordine del contesto mantenuto");
        var fk1=new JoinProposal{Target="customers_tb",TargetTable="customers_tb",ForeignKeyName="FK_B",Predicate="a.Id=b.Id"};
        var fk2=new JoinProposal{Target="customers_tb",TargetTable="customers_tb",ForeignKeyName="FK_A",Predicate="a.Other=b.Id"};
        Check(Auto("SELECT * FROM Orders JOIN custo").OrderMatches(new[]{history,fk1,fk2,revisions}).SequenceEqual(new[]{fk2,fk1,revisions,history}),"JOIN: nomi più vicini e FK multiple conservate");
        Check(Auto("SELECT * FROM Orders JOIN ").OrderMatches(new[]{fk1,history}).SequenceEqual(new[]{fk1,history}),"JOIN senza prefisso mantiene FK prima dei nomi");
        var column=Auto("SELECT * FROM Orders o WHERE o.Cu");
        var shortColumn=new JoinProposal{CompletionText="CustomerId",SearchName="CustomerId"};var longColumn=new JoinProposal{CompletionText="CustomerHistoryId",SearchName="CustomerHistoryId"};
        Check(column.OrderMatches(new[]{longColumn,shortColumn}).First()==shortColumn,"Stessa vicinanza applicata alle colonne");
        Check(Auto("SELECT * FROM Orders o WHERE o.CustomerH").OrderMatches(new[]{longColumn,shortColumn}).Single()==longColumn,"Allungando il prefisso aggiorna il candidato migliore");
        var large=Enumerable.Range(0,220).Select(i=>Named("customeractivityhistory"+i)).Concat(new[]{customers}).ToList();
        Check(trigger.OrderMatches(large).Take(200).First()==customers,"Ordinamento prima del limite di 200 elementi visualizzati");
        Check(trigger.OrderMatches(input).SequenceEqual(trigger.OrderMatches(input.Reverse())),"Risultati deterministici indipendenti dall'ordine in ingresso");
        Check(Auto("SELECT * FROM [custo").OrderMatches(input).First()==customers,"Prefisso delimitato ancora aperto usa lo stesso ordinamento");
    }
    static void ExtendedCases()
    {
        var c=Catalog();var fk=OrdersCustomers();
        var quoted=Auto("SELECT * FROM Orders o WHERE o.[Cu");
        Check(quoted!=null && quoted.Prefix.Single()=="Cu" && quoted.Context.Tables.Single().Alias=="o","Parentesi quadra ancora aperta in colonna");
        Check(quoted.Context.ReplaceLength==3,"Completamento sostituisce parentesi aperta e prefisso");
        string sql="SELECT o.[Cu] FROM Orders o";int caret=sql.IndexOf("Cu")+2;var trigger=AutomaticTrigger.TryCreate(sql,caret);
        Check(trigger!=null && trigger.Context.ReplaceLength==4,"Completamento all'interno di un identificatore delimitato chiuso");
        Check(Auto("SELECT * FROM [Or").Prefix.Single()=="Or","Tabella con parentesi ancora aperta");
        Check(Auto("SELECT * FROM Orders o WHERE o.\"Cu").Prefix.Single()=="Cu","Colonna con doppi apici ancora aperti");
        Check(Auto("SELECT * FROM 'unfinished")==null,"Nessun popup in stringa non terminata dopo FROM");
        Check(Auto("SELECT * FROM /* unfinished")==null,"Nessun popup in commento multilinea non terminato");
        Check(Auto("SELECT * FROM Orders WHERE N'abc")==null,"Nessun popup in stringa Unicode non terminata");
        sql="SELECT * FROM Orders o OUTER APPLY (SELECT 1 FROM Items i WHERE o.Cu) q";caret=sql.IndexOf("o.Cu")+4;trigger=AutomaticTrigger.TryCreate(sql,caret);
        Check(trigger!=null && trigger.Context.OuterTables.Any(x=>x.Alias=="o"),"APPLY mantiene la correlazione consentita");
        sql="SELECT * FROM Orders o JOIN (SELECT 1 FROM Items i WHERE o.Cu) q ON 1=1";caret=sql.IndexOf("o.Cu")+4;trigger=AutomaticTrigger.TryCreate(sql,caret);
        Check(trigger!=null && trigger.Context.OuterTables.Count==0,"Tabella derivata ordinaria non vede gli alias esterni");
        sql="WITH a AS (SELECT * FROM Or), b AS (SELECT Id FROM Orders) SELECT * FROM a";caret=sql.IndexOf("Or)")+2;trigger=AutomaticTrigger.TryCreate(sql,caret);
        Check(trigger!=null && trigger.Context.Ctes.ContainsKey("a") && !trigger.Context.Ctes.ContainsKey("b"),"CTE successive non proposte dentro una definizione precedente");
        var view=Obj(20,"dbo","AlteredView","V",true,"Buyer");view.DependencyIds=new List<int>{1};view.Definition="ALTER VIEW dbo.AlteredView AS SELECT CustomerId FROM Orders";c.Objects.Add(view);
        var context=Join("SELECT * FROM AlteredView JOIN ");Resolve(context,c);Check(JoinBuilder.Build(context,new[]{fk}).Single().Predicate=="AlteredView.Buyer = Customers.Id","Provenienza anche da ALTER VIEW");
        view.Definition="CREATE OR ALTER VIEW dbo.AlteredView AS SELECT CustomerId FROM Orders";context=Join("SELECT * FROM AlteredView JOIN ");Resolve(context,c);Check(JoinBuilder.Build(context,new[]{fk}).Count==1,"Provenienza anche da CREATE OR ALTER VIEW");
        view.DependencyIds=new List<int>{4};context=Join("SELECT * FROM AlteredView JOIN ");Resolve(context,c);Check(JoinBuilder.Build(context,new[]{fk}).Count==0,"Vista con nome non qualificato: usa l'ID della dipendenza reale, non lo schema predefinito dell'utente");
        view.DependencyIds=null;context=Join("SELECT * FROM AlteredView JOIN ");Resolve(context,c);Check(context.Tables[0].Columns.Single().Name=="Buyer" && JoinBuilder.Build(context,new[]{fk}).Count==0,"Dipendenze della vista assenti: colonne pubblicate note, nessuna provenienza indovinata");
        context=Join("SELECT * FROM Orders JOIN archive.Orders ON dbo.Orders.Id=archive.Orders.ArchivedId JOIN ");Resolve(context,c);
        Check(context.Tables[0].Reference=="dbo.Orders" && context.Tables[1].Reference=="archive.Orders","Riferimenti schema.tabella solo per omonimi senza alias");
        trigger=Auto("SELECT * FROM Orders WHERE dbo.Orders.Cu");Resolve(trigger.Context,c);Check(ShapeResolver.MatchTables(trigger.Context.Tables,trigger.Qualifier).Count==1,"Schema esplicito riconosciuto anche se FROM lo omette");
        c.Objects.Add(Obj(21,"dbo","Limited","V",true,"Duplicate","Duplicate","Known"));context=Join("SELECT * FROM Limited JOIN ");Resolve(context,c);
        Check(CompletionEngine.ColumnItems(context.Tables,false).All(x=>x.SearchName!="Duplicate"),"Output duplicato non presentato come colonna univoca");
        var ctx=Auto("WITH c AS (SELECT CustomerId AS Buyer FROM Orders) SELECT c.Bu FROM c");
        // Put the caret in the SELECT, before the FROM which supplies the CTE binding.
        sql="WITH c AS (SELECT CustomerId AS Buyer FROM Orders) SELECT c.Bu FROM c";caret=sql.IndexOf("c.Bu")+4;ctx=AutomaticTrigger.TryCreate(sql,caret);Resolve(ctx.Context,c);
        var columns=CompletionEngine.ColumnItems(ctx.Context.Tables,false);Check(columns.Single().CompletionText=="Buyer" && ctx.Matches(columns.Single()),"Colonna rinominata CTE proposta nel SELECT prima del FROM");
        context=Join("WITH c AS (SELECT a.CustomerId AS A, b.CustomerId AS B FROM Orders a JOIN Orders b ON a.Id=b.Id) SELECT * FROM c JOIN ");Resolve(context,c);
        var proposals=JoinBuilder.Build(context,new[]{fk});Check(proposals.Count(x=>x.TargetTable=="Customers")==2 && proposals.Any(x=>x.Predicate=="c.A = Customers.Id") && proposals.Any(x=>x.Predicate=="c.B = Customers.Id"),"Due istanze base dentro CTE mantengono provenienze distinte");
        context=Join("WITH c AS (SELECT CustomerId AS Buyer FROM Orders GROUP BY CustomerId) SELECT * FROM c JOIN ");Resolve(context,c);Check(JoinBuilder.Build(context,new[]{fk}).Count==1,"GROUP BY con chiave esposta mantiene la provenienza");
        context=Join("WITH c AS (SELECT COUNT(*) AS Total FROM Orders) SELECT * FROM c JOIN ");Resolve(context,c);Check(context.Tables[0].Columns.Single().Name=="Total" && JoinBuilder.Build(context,new[]{fk}).Count==0,"Aggregato completabile ma senza FK inventata");
        foreach(var prefix in new[]{"SELECT * FROM Orders JOIN ","SELECT * FROM Orders LEFT JOIN ","SELECT * FROM Orders RIGHT OUTER JOIN ","SELECT * FROM Orders FULL JOIN "})
        {
            var t=Auto(prefix);Resolve(t.Context,c);var p=JoinBuilder.Build(t.Context,new[]{fk}).Single();
            string inserted=prefix.Remove(t.Context.ReplaceStart,t.Context.ReplaceLength).Insert(t.Context.ReplaceStart,t.Insertion(p));
            Check(Join(inserted).Tables.Count==2,"SQL generato valido per "+t.Context.JoinKind);
        }
        using(var db=new FakeDatabase())
        {
            sql="SELECT o.Cu FROM Orders o";caret=sql.IndexOf("o.Cu")+4;var t=AutomaticTrigger.TryCreate(sql,caret);
            var items=CompletionEngine.LoadAsync(db,t,CancellationToken.None).GetAwaiter().GetResult();Check(items.Where(t.Matches).Single().CompletionText=="CustomerId","Pipeline completa di colonna qualificata nel SELECT");
        }
        using(var db=new FakeDatabase())
        {
            sql="WITH c AS (SELECT CustomerId AS Buyer FROM Orders) SELECT * FROM c WHERE c.Bu";var t=Auto(sql);
            var items=CompletionEngine.LoadAsync(db,t,CancellationToken.None).GetAwaiter().GetResult();Check(items.Where(t.Matches).Single().CompletionText=="Buyer","Pipeline completa colonne CTE in WHERE incompleto");
        }
        using(var db=new FakeDatabase())
        {
            var t=Auto("SELECT * FROM Orders o WHERE Orders.Cu");var items=CompletionEngine.LoadAsync(db,t,CancellationToken.None).GetAwaiter().GetResult();Check(items.Count==0,"Non suggerisce il nome originale quando la tabella è nascosta da un alias");
        }
        using(var db=new FakeDatabase())
        {
            var t=Auto("SELECT * FROM Orders WHERE archive.Orders.Ar");var items=CompletionEngine.LoadAsync(db,t,CancellationToken.None).GetAwaiter().GetResult();Check(items.Count==0,"Qualificatore di altro schema non sostituito con colonne della tabella locale");
        }
    }
}
