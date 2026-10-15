using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    sealed class ViewSelect : TSqlFragmentVisitor
    {
        public SelectStatement Select;
        public override void ExplicitVisit(CreateViewStatement node) { Select = node.SelectStatement; }
        public override void ExplicitVisit(AlterViewStatement node) { Select = node.SelectStatement; }
        public override void ExplicitVisit(CreateOrAlterViewStatement node) { Select = node.SelectStatement; }
    }
    public sealed class CatalogObject
    {
        public int Id;
        public string Schema, Name, Kind, Definition, SynonymTarget;
        public bool BareAllowed, Loaded;
        public List<int> DependencyIds;
        public readonly List<ColumnShape> Columns = new List<ColumnShape>();
        public string Sql { get { return BareAllowed ? SqlName.Format(Name) : SqlName.Format(Schema) + "." + SqlName.Format(Name); } }
        public string Caption { get { return Kind == "V" ? "Vista" : Kind == "SN" ? "Sinonimo" : Kind == "IF" || Kind == "TF" ? "Funzione tabellare" : "Tabella"; } }
    }
    public sealed class CatalogSnapshot
    {
        public readonly List<CatalogObject> Objects = new List<CatalogObject>();
        public Func<CatalogObject,CancellationToken,Task> Loader;
        public CatalogObject Find(string schema, string name)
        {
            var candidates = Objects.Where(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase) &&
                (schema == null ? o.BareAllowed : string.Equals(o.Schema, schema, StringComparison.OrdinalIgnoreCase))).ToList();
            var exact = candidates.Where(o => o.Name == name && (schema == null || o.Schema == schema)).ToList();
            return exact.Count == 1 ? exact[0] : candidates.Count == 1 ? candidates[0] : null;
        }
        public async Task EnsureAsync(CatalogObject obj, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if (!obj.Loaded && Loader != null) await Loader(obj, cancel).ConfigureAwait(false);
            obj.Loaded = true;
        }
    }
    public static class CatalogReader
    {
        public static async Task<CatalogSnapshot> ReadAsync(DbConnection connection, CancellationToken cancel)
        {
            if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancel).ConfigureAwait(false);
            var snapshot = new CatalogSnapshot();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandTimeout = 10;
                cmd.CommandText = @"SELECT o.object_id, s.name, o.name, RTRIM(o.type),
CONVERT(bit, CASE WHEN OBJECT_ID(QUOTENAME(o.name))=o.object_id THEN 1 ELSE 0 END), sn.base_object_name
FROM sys.objects AS o JOIN sys.schemas AS s ON s.schema_id=o.schema_id
LEFT JOIN sys.synonyms AS sn ON sn.object_id=o.object_id
WHERE o.type IN ('U','V','SN','IF','TF') AND o.is_ms_shipped=0 ORDER BY o.name,s.name;";
                using (var rows = await cmd.ExecuteReaderAsync(cancel).ConfigureAwait(false))
                    while (await rows.ReadAsync(cancel).ConfigureAwait(false))
                        snapshot.Objects.Add(new CatalogObject { Id = rows.GetInt32(0), Schema = rows.GetString(1), Name = rows.GetString(2), Kind = rows.GetString(3),
                            BareAllowed = rows.GetBoolean(4), SynonymTarget = rows.IsDBNull(5) ? null : rows.GetString(5) });
            }
            snapshot.Loader = async (obj, token) => {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandTimeout = 10;
                    cmd.CommandText = "SELECT c.name, ty.name FROM sys.columns AS c JOIN sys.types AS ty ON ty.user_type_id=c.user_type_id WHERE c.object_id=@objectId AND c.is_hidden=0 ORDER BY c.column_id;";
                    MetadataReader.Add(cmd, "@objectId", obj.Id);
                    using (var rows = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                        while (await rows.ReadAsync(token).ConfigureAwait(false)) obj.Columns.Add(new ColumnShape { Name = rows.GetString(0), TypeName = rows.GetString(1) });
                }
                if (obj.Kind == "V")
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandTimeout = 10;
                        cmd.CommandText = "SELECT definition FROM sys.sql_modules WHERE object_id=@objectId;";
                        MetadataReader.Add(cmd, "@objectId", obj.Id);
                        var definition = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                        obj.Definition = definition == null || definition == DBNull.Value ? null : (string)definition;
                    }
                if (obj.Kind == "V" && obj.Definition != null)
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandTimeout = 10;
                        cmd.CommandText = "SELECT DISTINCT referenced_id FROM sys.sql_expression_dependencies WHERE referencing_id=@objectId AND referenced_id IS NOT NULL AND referenced_database_name IS NULL AND referenced_server_name IS NULL;";
                        MetadataReader.Add(cmd, "@objectId", obj.Id);
                        obj.DependencyIds = new List<int>();
                        using (var rows = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                            while (await rows.ReadAsync(token).ConfigureAwait(false)) obj.DependencyIds.Add(rows.GetInt32(0));
                    }
            };
            return snapshot;
        }
    }
    public sealed class ShapeResolver
    {
        readonly CatalogSnapshot catalog;
        readonly CancellationToken cancel;
        readonly HashSet<QueryExpression> active = new HashSet<QueryExpression>();
        readonly HashSet<int> activeObjects = new HashSet<int>();
        int instance, depth;
        bool inView;
        List<int> viewDependencies;
        public ShapeResolver(CatalogSnapshot snapshot, CancellationToken token) { catalog = snapshot; cancel = token; }
        public async Task ResolveAsync(QueryContext context, bool includeCtes)
        {
            foreach (var use in context.Tables.Concat(context.OuterTables)) await ResolveUseAsync(use).ConfigureAwait(false);
            if (includeCtes) foreach (var use in context.CteTargets) await ResolveUseAsync(use).ConfigureAwait(false);
            if (context.Tables.Count > 0) SqlAnalysis.Validate(context.Tables);
        }
        public async Task ResolveUseAsync(TableUse use)
        {
            cancel.ThrowIfCancellationRequested();
            if (use.Columns != null) return;
            if (++depth > 24) { depth--; use.Columns = new List<ColumnShape>(); return; }
            try
            {
                if (use.Definition != null)
                {
                    use.Columns = await ProjectAsync(use.Definition, use.Ctes).ConfigureAwait(false);
                    ApplyNames(use.Columns, use.ExplicitColumns == null ? null : use.ExplicitColumns.Select(i => i.Value).ToList());
                }
                else
                {
                    CatalogObject obj;
                    if (inView)
                    {
                        // A view's unqualified table names were bound when the view was created,
                        // which may differ from the current user's default schema. Use server dependencies.
                        var candidates = catalog.Objects.Where(o => string.Equals(o.Name, use.Name, StringComparison.OrdinalIgnoreCase) &&
                            (use.Schema == null || string.Equals(o.Schema, use.Schema, StringComparison.OrdinalIgnoreCase)) &&
                            (viewDependencies != null ? viewDependencies.Contains(o.Id) : use.Schema != null)).ToList();
                        obj = candidates.Count == 1 ? candidates[0] : null;
                    }
                    else obj = catalog.Find(use.Schema, use.Name);
                    if (obj == null) { use.Columns = new List<ColumnShape>(); return; }
                    use.ObjectId = obj.Id; use.BareAllowed = obj.BareAllowed; use.ResolvedSchema = obj.Schema;
                    use.Columns = await ObjectColumnsAsync(obj).ConfigureAwait(false);
                }
                BuildMaps(use);
            }
            finally { depth--; }
        }
        async Task<List<ColumnShape>> ObjectColumnsAsync(CatalogObject obj)
        {
            if (!activeObjects.Add(obj.Id)) return new List<ColumnShape>();
            try
            {
                await catalog.EnsureAsync(obj, cancel).ConfigureAwait(false);
                if (obj.Kind == "SN")
                {
                    var parts = SqlAnalysis.Tokens(obj.SynonymTarget ?? "").Where(SqlAnalysis.Significant).ToList();
                    if (parts.Count == 3 && parts[1].TokenType == TSqlTokenType.Dot)
                    {
                        var target = catalog.Find(SqlName.Decode(parts[0].Text), SqlName.Decode(parts[2].Text));
                        if (target != null) return await ObjectColumnsAsync(target).ConfigureAwait(false);
                    }
                    return new List<ColumnShape>();
                }
                if (obj.Kind == "V" && !string.IsNullOrEmpty(obj.Definition))
                {
                    IList<ParseError> errors;
                    var script = new TSql160Parser(true).Parse(new StringReader(obj.Definition), out errors) as TSqlScript;
                    var view = new ViewSelect(); if (script != null) script.Accept(view);
                    if (view.Select != null && errors.Count == 0)
                    {
                        var ctes = Definitions(view.Select);
                        bool previousView = inView; var previousDependencies = viewDependencies;
                        List<ColumnShape> columns;
                        inView = true; viewDependencies = obj.DependencyIds;
                        try { columns = await ProjectAsync(view.Select.QueryExpression, ctes).ConfigureAwait(false); }
                        finally { inView = previousView; viewDependencies = previousDependencies; }
                        ApplyNames(columns, obj.Columns.Select(c => c.Name).ToList());
                        if (columns.Count == obj.Columns.Count) return columns;
                    }
                }
                string key = "base" + (++instance);
                return obj.Columns.Select(c => new ColumnShape { Name = c.Name, TypeName = c.TypeName, BaseColumn = obj.Kind == "U" ? c.Name : null,
                    BaseObjectId = obj.Kind == "U" ? obj.Id : 0, Instance = key }).ToList();
            }
            finally { activeObjects.Remove(obj.Id); }
        }
        public static Dictionary<string,CommonTableExpression> Definitions(SelectStatement select)
        {
            var result = new Dictionary<string,CommonTableExpression>(StringComparer.OrdinalIgnoreCase);
            if (select.WithCtesAndXmlNamespaces != null)
                foreach (var cte in select.WithCtesAndXmlNamespaces.CommonTableExpressions) result[cte.ExpressionName.Value] = cte;
            return result;
        }
        async Task<List<ColumnShape>> ProjectAsync(QueryExpression expression, Dictionary<string,CommonTableExpression> definitions)
        {
            if (expression == null || !active.Add(expression)) return new List<ColumnShape>();
            try
            {
                var parentheses = expression as QueryParenthesisExpression;
                if (parentheses != null) return await ProjectAsync(parentheses.QueryExpression, definitions).ConfigureAwait(false);
                var binary = expression as BinaryQueryExpression;
                if (binary != null)
                {
                    var first = await ProjectAsync(binary.FirstQueryExpression, definitions).ConfigureAwait(false);
                    return first.Select(c => new ColumnShape { Name = c.Name, TypeName = c.TypeName }).ToList(); // UNION/EXCEPT/INTERSECT: names known, provenance not assumed.
                }
                var query = expression as QuerySpecification;
                if (query == null) return new List<ColumnShape>();
                var tables = new List<TableUse>();
                if (query.FromClause != null)
                    foreach (var table in query.FromClause.TableReferences)
                        try { SqlAnalysis.Collect(table, tables, definitions ?? new Dictionary<string,CommonTableExpression>(StringComparer.OrdinalIgnoreCase)); }
                        catch (InvalidOperationException) { return NamedProjection(query); }
                foreach (var table in tables) await ResolveUseAsync(table).ConfigureAwait(false);
                var result = new List<ColumnShape>();
                foreach (var element in query.SelectElements)
                {
                    var star = element as SelectStarExpression;
                    if (star != null)
                    {
                        var matching = star.Qualifier == null ? tables : MatchTables(tables, star.Qualifier.Identifiers.Select(i => i.Value).ToArray());
                        foreach (var table in matching) result.AddRange((table.Columns ?? new List<ColumnShape>()).Select(c => c.Copy()));
                        continue;
                    }
                    var scalar = element as SelectScalarExpression;
                    if (scalar == null) continue;
                    var column = scalar.Expression as ColumnReferenceExpression;
                    string output = scalar.ColumnName == null ? column == null || column.MultiPartIdentifier == null ? null : column.MultiPartIdentifier.Identifiers.Last().Value : scalar.ColumnName.Value;
                    if (output == null) continue;
                    ColumnShape mapped = null;
                    if (column != null && column.MultiPartIdentifier != null)
                    {
                        var ids = column.MultiPartIdentifier.Identifiers.Select(i => i.Value).ToArray();
                        var sources = ids.Length == 1 ? tables : MatchTables(tables, ids.Take(ids.Length - 1).ToArray());
                        var candidates = sources.SelectMany(t => t.Columns ?? new List<ColumnShape>()).Where(c => string.Equals(c.Name, ids.Last(), StringComparison.OrdinalIgnoreCase)).ToList();
                        if (candidates.Count == 1) mapped = candidates[0].Copy(output);
                    }
                    result.Add(mapped ?? new ColumnShape { Name = output });
                }
                return result;
            }
            finally { active.Remove(expression); }
        }
        static List<ColumnShape> NamedProjection(QuerySpecification query)
        {
            return query.SelectElements.OfType<SelectScalarExpression>().Where(s => s.ColumnName != null).Select(s => new ColumnShape { Name = s.ColumnName.Value }).ToList();
        }
        static void ApplyNames(List<ColumnShape> columns, IList<string> names)
        {
            if (names == null || names.Count == 0) return;
            if (columns.Count == names.Count) { for (int i = 0; i < names.Count; i++) columns[i].Name = names[i]; }
            else { columns.Clear(); columns.AddRange(names.Select(n => new ColumnShape { Name = n })); }
        }
        static void BuildMaps(TableUse use)
        {
            var unique = use.Columns.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() == 1).Select(g => g.First()).ToList();
            foreach (var group in unique.Where(c => c.BaseObjectId != 0 && c.BaseColumn != null).GroupBy(c => c.Instance))
            {
                var map = new SourceMap { ObjectId = group.First().BaseObjectId };
                foreach (var column in group) if (!map.Columns.ContainsKey(column.BaseColumn)) map.Columns[column.BaseColumn] = column.Name;
                use.Sources.Add(map);
            }
            // A view/function/CTE with unknown provenance must never fall back to its object_id as a table FK.
            if (use.Definition != null || use.Sources.Count == 0) use.IsVirtual = true;
        }
        public static List<TableUse> MatchTables(IEnumerable<TableUse> sources, string[] qualifier)
        {
            if (qualifier.Length == 1) return sources.Where(t => string.Equals(t.Alias ?? t.Name, qualifier[0], StringComparison.OrdinalIgnoreCase)).ToList();
            if (qualifier.Length == 2) return sources.Where(t => t.Alias == null && string.Equals(t.Schema ?? t.ResolvedSchema, qualifier[0], StringComparison.OrdinalIgnoreCase) && string.Equals(t.Name, qualifier[1], StringComparison.OrdinalIgnoreCase)).ToList();
            return new List<TableUse>();
        }
    }
}
