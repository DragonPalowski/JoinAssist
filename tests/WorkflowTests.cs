using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using JoinAssist;
using Microsoft.SqlServer.TransactSql.ScriptDom;

static class WorkflowTests
{
    static int checks;
    static void Check(bool value, string name)
    {
        checks++;
        if (!value)
            throw new Exception("Workflow: " + name);
    }

    static void Reject(Action action, string name)
    {
        bool failed = false;
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            failed = true;
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }

        Check(failed, name);
    }

    static void Sql(string text)
    {
        IList<ParseError> errors;
        new TSql160Parser(true).Parse(new StringReader(text), out errors);
        Check(errors.Count == 0, "generated SQL parses: " + text);
    }

    internal static void Run()
    {
        var saved = PreferenceStore.Current;
        try
        {
            Literals();
            Templates();
            Relations();
            Metadata();
            Refactoring();
            Rules();
            Library();
        }
        finally
        {
            PreferenceStore.Apply(saved);
        }

        Console.WriteLine("PASS: " + checks + " workflow checks (clipboard literals, dates, grouped queries, FK graph/cardinality/navigation, templates, alias scopes, rule reuse/probes and favorite files). No real database or user preferences.");
    }

    static void Literals()
    {
        Check(WorkflowSql.InList("001\r\nO'Brien\r\nNULL\r\n001", "nvarchar", true, false) == "IN (N'001', N'O''Brien', N'NULL')", "text preserves zeros, escapes apostrophe and does not guess NULL");
        Check(WorkflowSql.InList("1\n01\n-2", "int", true, true) == "NOT IN (1, -2)", "numeric normalization and deduplication");
        Check(WorkflowSql.InList("a\na", "varchar", false, false) == "IN (N'a', N'a')", "duplicates optional");
        foreach (string value in new[]
        {
            "1); DROP TABLE Orders;--",
            "1,23",
            "NaN",
            "1 2"
        }

        )
            Reject(() => WorkflowSql.Literal(value, "int"), "invalid numeric never inserted " + value);
        Reject(() => WorkflowSql.InList("a\tb", "varchar", true, false), "multi-column clipboard rejected");
        Reject(() => WorkflowSql.InList("", "varchar", true, false), "empty list rejected");
        Reject(() => WorkflowSql.InList(string.Join("\n", Enumerable.Repeat("x", 1001)), "varchar", true, false), "list count bounded");
        Reject(() => WorkflowSql.Literal("maybe", "bit"), "bit validation");
        Reject(() => WorkflowSql.Literal("27/10/2026", "date"), "locale-ambiguous date rejected");
        Check(WorkflowSql.Literal("2026-10-27", "date") == "N'2026-10-27'", "ISO date retained");
        Reject(() => WorkflowSql.Literal("x", "varbinary"), "unsupported binary explicit");
        Reject(() => WorkflowSql.Literal("1.5", "int"), "integer fractions rejected");
        Reject(() => WorkflowSql.Literal("256", "tinyint"), "integer range enforced");
        foreach (string period in new[]
        {
            "Oggi",
            "Ieri",
            "Ultimi 7 giorni",
            "Mese corrente",
            "Intervallo"
        }

        )
            foreach (bool utc in new[]
            {
                true,
                false
            }

            )
            {
                string filter = WorkflowSql.DateFilter("o.CreatedAt", period, utc);
                Sql("SELECT * FROM Orders o WHERE " + filter);
                Check(!filter.Contains("BETWEEN") && filter.Contains("o.CreatedAt < "), "exclusive range without converting column");
                if (period != "Intervallo")
                    Check(filter.Contains(utc ? "SYSUTCDATETIME" : "SYSDATETIME"), "clock choice explicit");
            }

        var table = new CatalogObject
        {
            Schema = "dbo",
            Name = "Orders",
            Kind = "U"
        };
        var col = new ColumnShape
        {
            Name = "CreatedAt",
            TypeName = "datetime2"
        };
        foreach (string operation in new[]
        {
            "Duplicati",
            "Valori NULL",
            "Conta per valore",
            "Per giorno"
        }

        )
            Sql(WorkflowSql.ControlQuery(table, new[] { col }, operation));
        Reject(() => WorkflowSql.ControlQuery(table, new[] { new ColumnShape { Name = "Id", TypeName = "int" } }, "Per giorno"), "day grouping checks date type");
        Reject(() => WorkflowSql.ControlQuery(table, new[] { new ColumnShape { Name = "Payload", TypeName = "xml" } }, "Duplicati"), "XML grouping rejected");
        PreferenceStore.Apply(new Preferences { UppercaseKeywords = false });
        Check(WorkflowSql.Generated("SELECT [FROM], N'NULL' FROM [Orders] WHERE [Id]=1") == "select [FROM], N'NULL' from [Orders] where [Id]=1", "generated draft casing preserves identifiers and literals");
        PreferenceStore.Apply(new Preferences());
    }

    static void Templates()
    {
        var expanded = SnippetExpansion.Create("SELECT {{id:42}}, {{name:Alice}}, {{id}};");
        Check(expanded.Sql == "SELECT 42, Alice, 42;", "defaults and linked fields");
        Check(expanded.Fields.Count == 3 && expanded.Fields.Select(f => expanded.Sql.Substring(f.Start, f.Length)).SequenceEqual(new[] { "42", "Alice", "42" }), "field offsets exact");
        Check(SnippetExpansion.Create("SELECT 1").Fields.Count == 0, "plain SQL unchanged");
        Check(SnippetExpansion.Create("{{a:}}x{{a}}").Sql == "x", "empty default preserved");
        Reject(() => SnippetExpansion.Create(string.Join(" ", Enumerable.Range(0, 21).Select(i => "{{field" + i + ":0}}"))), "field count bounded");
        Reject(() => SnippetExpansion.Create("{{missing"), "incomplete placeholder diagnosed");
        Reject(() => SnippetExpansion.Create(string.Concat(Enumerable.Repeat("{{a}}", 101))), "linked positions bounded");
        var r = new PersonalRule
        {
            Kind = PersonalRuleKind.Snippet,
            Shortcut = "findOrders",
            LinkedFields = true,
            Sql = "SELECT * FROM Orders WHERE Id={{id:0}} OR ParentId={{id}}"
        };
        PreferenceStore.Apply(new Preferences { Rules = new List<PersonalRule> { r } });
        var t = AutomaticTrigger.TryCreate("findOrders", 10);
        var p = PersonalRules.Snippets(t, null, null).Single();
        Check(p.Fields.Count == 2 && p.CompletionText.Contains("Id=0"), "personal shortcut supplies inline fields");
        Check(PersonalRules.Revalidate(t, p, null, null), "template proposal revalidates expanded SQL");
        r.Sql = "SELECT {{id:0}} $cursor$";
        Reject(r.Validate, "mixed cursor formats rejected");
        r.LinkedFields = false;
        r.Sql = "SELECT N'{{literal}}', N'{{unfinished'";
        PreferenceStore.Apply(new Preferences { Rules = new List<PersonalRule> { r } });
        p = PersonalRules.Snippets(t, null, null).Single();
        Check(p.CompletionText == r.Sql && p.Fields.Count == 0, "existing literal braces stay unchanged unless linked fields are explicitly enabled");
    }

    static ForeignKey Key(int id, int child, int parent, string childColumn = "ParentId", string parentColumn = "Id")
    {
        var k = new ForeignKey
        {
            Id = id,
            ChildId = child,
            ParentId = parent,
            Name = "FK_" + id,
            ChildSchema = "dbo",
            ChildTable = "T" + child,
            ParentSchema = "dbo",
            ParentTable = "T" + parent
        };
        k.Columns.Add(new KeyColumn { Child = childColumn, Parent = parentColumn, Ordinal = 1 });
        return k;
    }

    static void Relations()
    {
        var graph = new WorkflowGraph
        {
            Catalog = new CatalogSnapshot()
        };
        foreach (int i in new[]
        {
            1,
            2,
            3,
            4
        }

        )
            graph.Catalog.Objects.Add(new CatalogObject { Id = i, Schema = "dbo", Name = "T" + i, Kind = "U" });
        graph.Keys.Add(Key(10, 2, 1));
        graph.Keys.Add(Key(11, 3, 2));
        graph.Keys.Add(Key(12, 4, 3));
        graph.Keys.Add(Key(13, 1, 4));
        var direct = new RelationStep
        {
            Key = graph.Keys[0],
            ToParent = true
        };
        Check(graph.Cardinality(direct).StartsWith("0..1"), "child to parent keyed");
        direct.ToParent = false;
        Check(graph.Cardinality(direct).StartsWith("0..N"), "reverse FK may multiply");
        graph.Unique[2] = new List<HashSet<string>>
        {
            new HashSet<string>
            {
                "ParentId"
            }
        };
        Check(graph.Cardinality(direct).StartsWith("0..1"), "unique child index constrains cardinality");
        graph.Keys[0].Untrusted = true;
        Check(graph.Cardinality(direct).Contains("non verificata"), "untrusted key labelled");
        var paths = graph.Paths(1, 3, 3, CancellationToken.None);
        Check(paths.Count == 2 && paths.All(p => p.Steps.Count == 2), "both bounded paths around graph without cycles");
        foreach (var path in paths)
        {
            Sql(graph.JoinSql(1, path));
            var text = graph.RelatedSql(1, path, new ColumnShape { Name = "Id", TypeName = "int" }, "12");
            Sql(text);
            Check(text.Contains("WHERE EXISTS") && !text.Contains("DISTINCT"), "navigation avoids duplicate multiplication and unsupported DISTINCT types");
        }

        graph.Keys[3].Disabled = true;
        paths = graph.Paths(1, 4, 4, CancellationToken.None);
        Check(paths.Count == 1 && paths[0].Steps.Count == 3, "disabled shortcut excluded; longer path remains");
        Check(graph.Paths(1, 4, 2, CancellationToken.None).Count == 0, "depth enforced");
        Check(graph.Paths(1, 0, 2, CancellationToken.None).Count == 2, "batch navigator includes reachable intermediate nodes");
        Sql(graph.Orphans(2, 10));
        Check(graph.Orphans(2, 10).Contains("IS NOT NULL"), "nullable FK not misreported orphan");
        var composite = Key(20, 4, 1, "TenantId", "TenantId");
        composite.Columns.Add(new KeyColumn { Ordinal = 2, Child = "OrderId", Parent = "Id" });
        var step = new RelationStep
        {
            Key = composite,
            ToParent = true
        };
        Check(step.Predicate("c", "p") == "c.[TenantId] = p.[TenantId] AND c.[OrderId] = p.[Id]", "composite key retains all ordered pairs");
        var cancelled = new CancellationToken(true);
        bool stopped = false;
        try
        {
            graph.Paths(1, 4, 4, cancelled);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }

        Check(stopped, "path cancellation");
        var wide = new WorkflowGraph
        {
            Catalog = new CatalogSnapshot()
        };
        wide.Catalog.Objects.Add(new CatalogObject { Id = 1, Schema = "dbo", Name = "Root", Kind = "U" });
        foreach (int id in Enumerable.Range(2, 50))
        {
            wide.Catalog.Objects.Add(new CatalogObject { Id = id, Schema = "dbo", Name = "T" + id, Kind = "U" });
            wide.Keys.Add(Key(id, id, 1));
        }

        Check(wide.Paths(1, 0, 1, CancellationToken.None).Count == 30, "wide graph caps alternatives inside a single adjacency list");
    }

    static void Refactoring()
    {
        string query = "SELECT o.*, o.Id, 'o.Id' AS Note FROM Orders o WHERE o.Id=1 -- o.Id";
        string renamed = WorkflowSql.RenameAlias(query, query.IndexOf("o.Id", StringComparison.Ordinal), "o", "ord");
        Check(renamed == "SELECT ord.*, ord.Id, 'o.Id' AS Note FROM Orders ord WHERE ord.Id=1 -- o.Id", "rename references/star but not literals/comments");
        Sql(renamed);
        query = "SELECT o.Id FROM Orders o WHERE EXISTS (SELECT 1 FROM Items i WHERE i.OrderId=o.Id)";
        renamed = WorkflowSql.RenameAlias(query, 9, "o", "ord");
        Check(renamed.Contains("i.OrderId=ord.Id"), "correlated outer reference follows rename");
        query = "SELECT o.Id FROM Orders o WHERE EXISTS (SELECT o.Id FROM Items o WHERE o.Id=1)";
        renamed = WorkflowSql.RenameAlias(query, 9, "o", "ord");
        Check(renamed == "SELECT ord.Id FROM Orders ord WHERE EXISTS (SELECT o.Id FROM Items o WHERE o.Id=1)", "nested same-name alias stays local");
        Reject(() => WorkflowSql.RenameAlias(query, 9, "o", "SELECT"), "reserved alias rejected");
        Reject(() => WorkflowSql.RenameAlias("SELECT o.Id FROM Orders o JOIN Items i ON o.Id=i.Id", 9, "o", "i"), "colliding alias rejected");
        Reject(() => WorkflowSql.RenameAlias("SELECT o.Id FROM Orders o WHERE", 9, "o", "ord"), "unfinished refactor rejected");
        Reject(() => WorkflowSql.RenameAlias("UPDATE o SET Id=o.Id FROM Orders o WHERE o.Id=1", 30, "o", "ord"), "DML target alias is not partially renamed");
    }

    static void Metadata()
    {
        using (var db = new FakeDatabase())
        {
            var catalog = CatalogReader.ReadAsync(db, CancellationToken.None).Result;
            var graph = WorkflowGraph.ReadAsync(db, catalog, CancellationToken.None).Result;
            Check(graph.Keys.Count == 2 && graph.Keys[0].Columns.Count == 2, "reader groups composite FK rows");
            Check(graph.Unique[1].Count == 2, "reader separates unique indexes");
            Check(graph.Cardinality(new RelationStep { Key = graph.Keys[0], ToParent = false }).StartsWith("0..1"), "reader composite child uniqueness used");
            string term = "x'); DROP TABLE Orders;--";
            var hits = WorkflowSearch.Find(db, term, null, CancellationToken.None).Result;
            Check(hits.Count == 1, "metadata column search reads catalog hits");
            Check(!db.Commands.Last().CommandText.Contains(term) && (string)db.Commands.Last().Parameters["@term"].Value == term, "column search text is parameterized");
            hits = WorkflowSearch.Find(db, "", 1, CancellationToken.None).Result;
            Check(hits.Count == 1 && (int)db.Commands.Last().Parameters["@id"].Value == 1, "dependency search parameterizes catalog id");
            Check(db.Commands.All(c => c.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && c.CommandText.Contains("sys.")), "new tools only read system metadata");
        }
    }

    static void Rules()
    {
        var r = new PersonalRule
        {
            Table = "Orders"
        };
        PreferenceStore.Apply(new Preferences { Rules = new List<PersonalRule> { r } });
        var catalog = new CatalogSnapshot();
        catalog.Objects.Add(new CatalogObject { Id = 1, Schema = "dbo", Name = "Orders", Kind = "U", BareAllowed = true, Loaded = true });
        var a = AutomaticTrigger.TryCreate("SELECT * FROM Or", 16);
        var b = AutomaticTrigger.TryCreate("SELECT * FROM Orders", 20);
        Check(a.BaseKey == b.BaseKey && PersonalRules.RequiresReload(a, b, false), "same table session must rebuild personal text/span after continued typing");
        var p = PersonalRules.Tables(catalog, b, "server", "db", CancellationToken.None, CompletionEngine.TableItems(catalog, b, null)).GetAwaiter().GetResult().Single();
        Check(PersonalRules.Revalidate(b, p, "server", "db"), "rebuilt table rule accepted");
        var x = new PersonalRule
        {
            Kind = PersonalRuleKind.Snippet,
            Shortcut = "selOrders",
            Sql = "SELECT 1"
        };
        var y = new PersonalRule
        {
            Kind = PersonalRuleKind.Snippet,
            Shortcut = "selOther",
            Sql = "SELECT 2"
        };
        PreferenceStore.Apply(new Preferences { Rules = new List<PersonalRule> { x, y } });
        a = AutomaticTrigger.TryCreate("selOrders", 9);
        b = AutomaticTrigger.TryCreate("selO", 4);
        Check(PersonalRules.RequiresReload(a, b, false) && PersonalRules.Snippets(b, null, null).Count == 2, "backspace expands candidates");
        var middle = AutomaticTrigger.TryCreate("selOrders", 4);
        Check(middle.Mode == CompletionMode.PersonalRule && middle.Prefix.Single() == "selO" && middle.Context.ReplaceLength == 9, "mid-word prefix replaces whole shortcut");
        Check(!PersonalRules.RequiresReload(b, b, true), "metadata error does not start an infinite same-prefix retry");
        Check(PersonalRules.RequiresReload(a, b, true), "changed prefix can retry failed session");
        r.Database = "other";
        Check(PersonalRules.Probe(r, "SELECT * FROM Orders", catalog, "server", "db", CancellationToken.None).Result.Contains("ambito"), "probe explains scope");
        r.Database = "";
        Check(PersonalRules.Probe(r, "SELECT * FROM Orders", catalog, "server", "db", CancellationToken.None).Result.Contains("TOP 100"), "probe generates actual SQL");
        Check(PersonalRules.Probe(r, "SELECT Id FROM Orders", catalog, "server", "db", CancellationToken.None).Result.Contains("Non applicabile"), "probe explains unsupported shape");
        PreferenceStore.Apply(new Preferences { Rules = new List<PersonalRule> { r, new PersonalRule { Table = "Orders" } } });
        Check(PersonalRules.Probe(r, "SELECT * FROM Orders", catalog, "server", "db", CancellationToken.None).Result.Contains("scelta manuale"), "probe explains competing equal scopes");
        r.Enabled = false;
        Check(PersonalRules.Probe(r, "SELECT * FROM Orders", catalog, "server", "db", CancellationToken.None).Result.Contains("disattivata"), "probe explains disabled draft");
    }

    static void Library()
    {
        string path = Path.Combine(Path.GetTempPath(), "JoinAssist-library-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var q = new SavedQuery
            {
                Name = "Orders",
                Category = "Checks",
                Sql = "SELECT {{id:0}}",
                Server = "Server",
                Database = "Db"
            };
            QueryLibrary.Save(path, new List<SavedQuery> { q });
            var loaded = QueryLibrary.Load(path).Single();
            Check(loaded.Sql == q.Sql && loaded.Id == q.Id, "favorite atomic round trip");
            Check(QueryLibrary.InScope(q, "server", "Db") && !QueryLibrary.InScope(q, "server", "db"), "favorite server/db scope identity");
            Reject(() => QueryLibrary.Validate(new List<SavedQuery> { q, q }), "duplicate favorite identity rejected");
            string stamp = QueryLibrary.Stamp(path);
            QueryLibrary.Save(path, new List<SavedQuery> { new SavedQuery { Name = "Other", Sql = "SELECT 2" } }, stamp);
            Reject(() => QueryLibrary.Save(path, new List<SavedQuery> { q }, stamp), "stale favorite write cannot overwrite another window");
            Check(QueryLibrary.Load(path).Single().Name == "Other", "newer favorite preserved");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
