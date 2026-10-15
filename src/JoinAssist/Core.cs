using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    public sealed class ColumnShape
    {
        public string Name, TypeName, BaseColumn, Instance;
        public int BaseObjectId;
        public ColumnShape Copy(string name = null) { return new ColumnShape { Name = name ?? Name, TypeName = TypeName, BaseColumn = BaseColumn, BaseObjectId = BaseObjectId, Instance = Instance }; }
    }
    public sealed class SourceMap
    {
        public int ObjectId;
        public bool Identity;
        public readonly Dictionary<string,string> Columns = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public string Column(string original) { string exposed; return Identity ? original : Columns.TryGetValue(original, out exposed) ? exposed : null; }
    }
    public sealed class TableUse
    {
        public string Schema, Name, Alias, ResolvedSchema;
        public int ObjectId;
        public bool BareAllowed = true, NeedsQualifiedReference, IsVirtual, IsFunction;
        public int Offset;
        public int ScopeLevel;
        public QueryExpression Definition;
        public IList<Identifier> ExplicitColumns;
        public Dictionary<string,CommonTableExpression> Ctes;
        public List<ColumnShape> Columns;
        public readonly List<SourceMap> Sources = new List<SourceMap>();
        public string QualifiedName { get { return Schema == null ? SqlName.Quote(Name) : SqlName.Quote(Schema) + "." + SqlName.Quote(Name); } }
        public string Reference { get { return Alias != null ? SqlName.Format(Alias) : NeedsQualifiedReference && (Schema ?? ResolvedSchema) != null ? SqlName.Format(Schema ?? ResolvedSchema) + "." + SqlName.Format(Name) : SqlName.Format(Name); } }
        public IEnumerable<SourceMap> Maps { get { return Sources.Count > 0 ? Sources : !IsVirtual && !IsFunction && ObjectId != 0 ? new[] { new SourceMap { ObjectId = ObjectId, Identity = true } } : Enumerable.Empty<SourceMap>(); } }
    }
    public static class SqlName
    {
        static readonly ConcurrentDictionary<string,string> formatted = new ConcurrentDictionary<string,string>(StringComparer.Ordinal);
        public static string Quote(string name) { return "[" + name.Replace("]", "]]") + "]"; }
        public static string Format(string name)
        {
            return formatted.GetOrAdd(name, n => {
                IList<ParseError> errors;
                var tokens = new TSql160Parser(true).GetTokenStream(new StringReader(n), out errors).Where(t => t.TokenType != TSqlTokenType.EndOfFile).ToList();
                return errors.Count == 0 && tokens.Count == 1 && tokens[0].TokenType == TSqlTokenType.Identifier && tokens[0].Text == n ? n : Quote(n);
            });
        }
        public static string Decode(string name)
        {
            if (name.StartsWith("[", StringComparison.Ordinal)) return name.Substring(1, name.Length - 1 - (name.EndsWith("]", StringComparison.Ordinal) ? 1 : 0)).Replace("]]", "]");
            if (name.StartsWith("\"", StringComparison.Ordinal)) return name.Substring(1, name.Length - 1 - (name.EndsWith("\"", StringComparison.Ordinal) ? 1 : 0)).Replace("\"\"", "\"");
            return name;
        }
    }
    public sealed class QueryContext
    {
        public readonly List<TableUse> Tables = new List<TableUse>();
        public readonly List<TableUse> OuterTables = new List<TableUse>();
        public readonly List<TableUse> CteTargets = new List<TableUse>();
        public Dictionary<string,CommonTableExpression> Ctes = new Dictionary<string,CommonTableExpression>(StringComparer.OrdinalIgnoreCase);
        public int ReplaceStart, ReplaceLength;
        public string JoinKind = "INNER";
        public bool ReplacesJoin, PredicateOnly;
        public TableUse LastJoinTarget;
    }
    public static class QueryAnalyzer
    {
        public static QueryContext Analyze(string sql, int caret) { return SqlAnalysis.AnalyzeJoin(sql, caret, false); }
    }
    public sealed class ForeignKey
    {
        public int Id, ChildId, ParentId;
        public string Name, ChildSchema, ChildTable, ParentSchema, ParentTable;
        public bool Disabled, Untrusted;
        public bool ChildBare = true, ParentBare = true;
        public readonly List<KeyColumn> Columns = new List<KeyColumn>();
    }
    public sealed class KeyColumn { public int Ordinal; public string Child, Parent; }
    public sealed class JoinProposal
    {
        public string ForeignKeyName, Target, TargetSchema, TargetTable, Predicate, Alias, Source, Warning;
        public string CompletionText, DisplayText, SearchName, Category;
        public bool IsName { get { return CompletionText != null; } }
        public string Sql(string kind) { return kind + " JOIN " + Target + (Alias == null ? "" : " AS " + SqlName.Format(Alias)) + " ON " + Predicate; }
        public string Caption { get { return DisplayText ?? Target + "  ←  " + Source + "  |  " + ForeignKeyName + Warning; } }
    }
    public static class JoinBuilder
    {
        public static List<JoinProposal> Build(QueryContext context, IEnumerable<ForeignKey> keySet)
        {
            var keys = keySet.ToList();
            var result = new List<JoinProposal>();
            var sources = context.PredicateOnly ? context.Tables.Where(t => !ReferenceEquals(t, context.LastJoinTarget)) : context.Tables.AsEnumerable();
            foreach (var source in sources)
                foreach (var map in source.Maps)
                    foreach (var fk in keys.Where(k => k.Columns.Count > 0))
                    {
                        if (map.ObjectId == fk.ChildId) Add(result, context, source, map, fk, true);
                        if (map.ObjectId == fk.ParentId) Add(result, context, source, map, fk, false);
                    }
            return result.GroupBy(p => p.Target + "\0" + p.Alias + "\0" + p.ForeignKeyName + "\0" + p.Predicate).Select(g => g.First())
                .OrderBy(p => p.Target, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.ForeignKeyName).ThenBy(p => p.Source).ToList();
        }
        static void Add(List<JoinProposal> result, QueryContext context, TableUse source, SourceMap sourceMap, ForeignKey fk, bool outgoing)
        {
            var columns = fk.Columns.OrderBy(c => c.Ordinal).ToList();
            if (columns.Any(c => sourceMap.Column(outgoing ? c.Child : c.Parent) == null)) return;
            int targetId = outgoing ? fk.ParentId : fk.ChildId;
            if (context.PredicateOnly)
            {
                var target = context.LastJoinTarget;
                if (target == null) return;
                foreach (var targetMap in target.Maps.Where(m => m.ObjectId == targetId))
                    if (columns.All(c => targetMap.Column(outgoing ? c.Parent : c.Child) != null))
                        result.Add(Make(source, sourceMap, target.Reference, target.Alias, target.Name, target.Schema, target.Reference, targetMap, fk, outgoing, ""));
                return;
            }
            string name = outgoing ? fk.ParentTable : fk.ChildTable;
            string schema = outgoing ? fk.ParentSchema : fk.ChildSchema;
            bool bare = outgoing ? fk.ParentBare : fk.ChildBare;
            if (context.Ctes.ContainsKey(name)) bare = false; // A CTE can shadow a real table.
            string tableSql = bare ? SqlName.Format(name) : SqlName.Format(schema) + "." + SqlName.Format(name);
            string alias = NecessaryAlias(context, name, targetId);
            string warning = (bare ? "" : " [schema necessario]") + (alias == null ? "" : " [alias necessario]");
            result.Add(Make(source, sourceMap, tableSql, alias, name, schema, alias == null ? tableSql : SqlName.Format(alias), new SourceMap { ObjectId = targetId, Identity = true }, fk, outgoing, warning));
            foreach (var cte in context.CteTargets)
                foreach (var targetMap in cte.Maps.Where(m => m.ObjectId == targetId))
                    if (columns.All(c => targetMap.Column(outgoing ? c.Parent : c.Child) != null))
                    {
                        string cteAlias = NecessaryAlias(context, cte.Name, 0);
                        result.Add(Make(source, sourceMap, SqlName.Format(cte.Name), cteAlias, cte.Name, null,
                            cteAlias == null ? SqlName.Format(cte.Name) : SqlName.Format(cteAlias), targetMap, fk, outgoing, cteAlias == null ? " [CTE]" : " [CTE, alias necessario]"));
                    }
        }
        static string NecessaryAlias(QueryContext context, string name, int objectId)
        {
            bool collision = context.Tables.Any(t => string.Equals(t.Alias ?? t.Name, name, StringComparison.OrdinalIgnoreCase) ||
                objectId != 0 && !t.IsVirtual && !t.IsFunction && t.ObjectId == objectId);
            if (!collision) return null;
            var occupied = new HashSet<string>(context.Tables.Select(t => t.Alias ?? t.Name), StringComparer.OrdinalIgnoreCase);
            string alias = "j"; for (int i = 2; occupied.Contains(alias); i++) alias = "j" + i;
            return alias;
        }
        static JoinProposal Make(TableUse source, SourceMap sourceMap, string targetSql, string alias, string name, string schema,
            string reference, SourceMap targetMap, ForeignKey fk, bool outgoing, string extra)
        {
            var pairs = fk.Columns.OrderBy(c => c.Ordinal).Select(c => source.Reference + "." + SqlName.Format(sourceMap.Column(outgoing ? c.Child : c.Parent)) +
                " = " + reference + "." + SqlName.Format(targetMap.Column(outgoing ? c.Parent : c.Child)));
            return new JoinProposal { Target = targetSql, TargetSchema = schema, TargetTable = name, Alias = alias, Source = source.Reference,
                ForeignKeyName = fk.Name, Predicate = string.Join(" AND ", pairs),
                Warning = (fk.Disabled ? " [FK disabilitata]" : fk.Untrusted ? " [FK non verificata]" : "") + extra };
        }
        public static string InsertText(QueryContext context, JoinProposal proposal, string kind, string sql)
        {
            return Environment.NewLine + proposal.Sql(kind) + Environment.NewLine;
        }
    }
}
