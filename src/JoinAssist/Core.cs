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
        public int? MaxLength, Precision, Scale;
        public bool? Nullable, PrimaryKey, ForeignKey, Identity, Computed, HasDefault, Generated;
        public ColumnShape Copy(string name = null) { var copy=(ColumnShape)MemberwiseClone(); copy.Name=name??Name; return copy; }
        public string Description
        {
            get
            {
                string type=TypeName??"tipo non disponibile";
                if(!type.Contains("("))
                {
                    if(MaxLength.HasValue && new[]{"nvarchar","nchar","varchar","char","varbinary","binary"}.Contains(type,StringComparer.OrdinalIgnoreCase))
                        type+="("+(MaxLength.Value<0?"max":(type.StartsWith("n",StringComparison.OrdinalIgnoreCase)?MaxLength.Value/2:MaxLength.Value).ToString(System.Globalization.CultureInfo.InvariantCulture))+")";
                    else if(Precision.HasValue && Scale.HasValue && new[]{"decimal","numeric"}.Contains(type,StringComparer.OrdinalIgnoreCase))type+="("+Precision+","+Scale+")";
                    else if(Scale.HasValue && new[]{"datetime2","datetimeoffset","time"}.Contains(type,StringComparer.OrdinalIgnoreCase))type+="("+Scale+")";
                }
                return type+(Nullable.HasValue?(Nullable.Value?" · NULL":" · NOT NULL"):"")+(PrimaryKey==true?" · PK":"")+(ForeignKey==true?" · FK":"")+(Identity==true?" · IDENTITY":"")+(Computed==true?" · calcolata":"");
            }
        }
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
        public bool BareAllowed = true, NeedsQualifiedReference, IsVirtual, IsFunction, IsLocal;
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
            if(PreferenceStore.Read.QuoteNames && !name.StartsWith("@",StringComparison.Ordinal))return Quote(name);
            return formatted.GetOrAdd(name, n => {
                IList<ParseError> errors;
                var tokens = new TSql160Parser(true).GetTokenStream(new StringReader(n), out errors).Where(t => t.TokenType != TSqlTokenType.EndOfFile).ToList();
                return errors.Count == 0 && tokens.Count == 1 && (tokens[0].TokenType == TSqlTokenType.Identifier || n.StartsWith("@", StringComparison.Ordinal) && tokens[0].TokenType == TSqlTokenType.Variable) && tokens[0].Text == n ? n : Quote(n);
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
        public Dictionary<string,LocalTable> Locals = new Dictionary<string,LocalTable>(StringComparer.OrdinalIgnoreCase);
        public int ReplaceStart, ReplaceLength;
        public string JoinKind = "INNER";
        public bool ReplacesJoin, PredicateOnly;
        public TableUse LastJoinTarget;
        public string Diagnostic, DiagnosticCode;
        public readonly HashSet<string> AssignedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> FocusedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string Clause, ComparisonQualifier, ComparisonColumn;
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
        public string AlternateSearchName, SearchIdentity;
        public string SharedKeyParent;
        public bool IsPersonalRule,RuleAuto,RuleOffline;
        public string RuleId,RuleStamp,RuleName,RuleTableSql;
        public int RuleReplaceStart,RuleReplaceLength;
        public string Keyword,TemplateOperator;
        public bool IsFilterTemplate;
        public NameMatch Match {get;set;}
        public int ContextRank;
        public bool IsInsertTemplate;
        public int? CaretOffset;
        public string SourceReference;
        public ColumnShape Column;
        public List<JoinProposal> Steps;
        public bool IsName { get { return CompletionText != null; } }
        public string ChoiceIdentity {get {return SearchIdentity??string.Join("\0",new[]{Category,TargetSchema,TargetTable,SourceReference,SearchName,ForeignKeyName});}}
        public string PopupTitle { get { return IsPersonalRule?(TargetTable??SearchName)+" · "+RuleName:IsFilterTemplate?CompletionText:IsName ? Category=="Riferimento"?CompletionText:SearchName??TargetTable??CompletionText : Target+(Alias==null?"":" AS "+SqlName.Format(Alias)); } }
        public string MatchNote {get {return Match!=null && Match.IsCorrection?" · possibile correzione":"";}}
        public string PopupDetail
        {
            get
            {
                if(!IsName)return SharedKeyParent!=null?"1 JOIN · FK comuni verso "+SharedKeyParent+" · ON "+Predicate+Warning+MatchNote:
                    (Steps==null?"FK":"2 JOIN · tramite "+Steps[0].Target)+" · "+ForeignKeyName+Warning+MatchNote;
                int divider=(DisplayText??"").IndexOf("  |  ",StringComparison.Ordinal);
                return (divider<0?Category:DisplayText.Substring(divider+5))+MatchNote;
            }
        }
        public string Sql(string kind) { return Steps!=null?string.Join(Environment.NewLine,Steps.Select(s=>s.Sql(kind))):PreferenceStore.Keyword(kind+" JOIN") + " " + Target + (Alias == null ? "" : " "+PreferenceStore.Keyword("AS")+" " + SqlName.Format(Alias)) + " "+PreferenceStore.Keyword("ON")+" " + Predicate; }
        public string Caption { get { return DisplayText ?? Target + (Alias == null ? "" : " AS " + SqlName.Format(Alias)) + "  ←  " + Source + "  |  " + ForeignKeyName + "\nON " + Predicate + Warning; } }
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
            if(PreferenceStore.Read.AlwaysSchema)bare=false;
            if (context.Ctes.ContainsKey(name)) bare = false; // A CTE can shadow a real table.
            string tableSql = bare ? SqlName.Format(name) : SqlName.Format(schema) + "." + SqlName.Format(name);
            string alias = NecessaryAlias(context, name, targetId);
            string warning = (bare ? "" : PreferenceStore.Read.AlwaysSchema ? " [schema dalle impostazioni]" : " [schema necessario]") + (alias == null ? "" : PreferenceStore.Read.AutomaticAliases ? " [alias dalle impostazioni]" : " [alias necessario]");
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
            if (!collision && !PreferenceStore.Read.AutomaticAliases) return null;
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
                ForeignKeyName = fk.Name, Predicate = string.Join(" "+PreferenceStore.Keyword("AND")+" ", pairs),
                Warning = (fk.Disabled ? " [FK disabilitata]" : fk.Untrusted ? " [FK non verificata]" : "") + extra };
        }
        public static string InsertText(QueryContext context, JoinProposal proposal, string kind, string sql)
        {
            return Environment.NewLine + proposal.Sql(kind) + Environment.NewLine;
        }
    }
}
