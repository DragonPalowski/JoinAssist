using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    public sealed class LocalTable
    {
        public string Name;
        public int Offset;
        public List<ColumnShape> Columns;
        public QueryExpression Query;
        public Dictionary<string,CommonTableExpression> Ctes;
        public Dictionary<string,LocalTable> Inputs;
    }

    public static class LocalTables
    {
        static readonly AnalysisCache<Dictionary<string,LocalTable>> cache=new AnalysisCache<Dictionary<string,LocalTable>>();
        internal static void ClearCache(){cache.Clear();}
        public static bool IsLocal(string name) { return !string.IsNullOrEmpty(name) && (name[0] == '#' || name[0] == '@'); }
        internal static string TemporaryName(SchemaObjectName name)
        {
            return name != null && name.Identifiers.Count == 1 && name.BaseIdentifier.Value.StartsWith("#", StringComparison.Ordinal) &&
                !name.BaseIdentifier.Value.StartsWith("##", StringComparison.Ordinal) ? name.BaseIdentifier.Value : null;
        }
        static string TemporaryBase(SchemaObjectName name)
        {
            return name != null && name.BaseIdentifier != null && name.BaseIdentifier.Value.StartsWith("#", StringComparison.Ordinal) ? name.BaseIdentifier.Value : null;
        }
        public static Dictionary<string,LocalTable> Read(string sql, int position)
        {
            string prefix=sql.Substring(0,position);
            var stored=cache.Get(prefix,()=>ReadRaw(prefix,prefix.Length));
            var copies=new Dictionary<LocalTable,LocalTable>();
            return Copy(stored,copies);
        }
        static Dictionary<string,LocalTable> Copy(Dictionary<string,LocalTable> source,Dictionary<LocalTable,LocalTable> copies)
        {
            var result=new Dictionary<string,LocalTable>(StringComparer.OrdinalIgnoreCase);
            foreach(var pair in source)
            {
                LocalTable value;
                if(!copies.TryGetValue(pair.Value,out value))
                {
                    var old=pair.Value;value=new LocalTable{Name=old.Name,Offset=old.Offset,Query=old.Query,Columns=old.Columns==null?null:old.Columns.Select(c=>c.Copy()).ToList(),
                        Ctes=old.Ctes==null?null:new Dictionary<string,CommonTableExpression>(old.Ctes,StringComparer.OrdinalIgnoreCase)};copies[old]=value;
                    value.Inputs=old.Inputs==null?null:Copy(old.Inputs,copies);
                }
                result[pair.Key]=value;
            }
            return result;
        }
        static Dictionary<string,LocalTable> ReadRaw(string sql,int position)
        {
            var known = new Dictionary<string,LocalTable>(StringComparer.OrdinalIgnoreCase);
            IList<ParseError> errors;
            var script = new TSql160Parser(true).Parse(new StringReader(sql.Substring(0, position)), out errors) as TSqlScript;
            if (script == null) return known;
            foreach (var batch in script.Batches)
            {
                foreach (string name in known.Keys.Where(n => n.StartsWith("@", StringComparison.Ordinal)).ToList()) known.Remove(name);
                foreach (var statement in batch.Statements)
                {
                    if (statement.StartOffset + statement.FragmentLength > position) continue;
                    // Never trust a statement which contains a parser error.
                    if (errors.Any(e => statement.StartOffset <= e.Offset && e.Offset <= statement.StartOffset + statement.FragmentLength)) continue;
                    Apply(statement, known);
                    if (known.Count > 200) throw new InvalidOperationException("Lo script contiene più di 200 tabelle locali visibili.");
                }
            }
            // An unfinished final batch may be omitted by ScriptDOM, but GO still ends variable scope.
            int lastGo = SqlAnalysis.Tokens(sql.Substring(0, position)).Where(t => t.TokenType == TSqlTokenType.Go).Select(t => t.Offset).DefaultIfEmpty(-1).Max();
            foreach (string name in known.Where(p => p.Key.StartsWith("@", StringComparison.Ordinal) && p.Value.Offset < lastGo).Select(p => p.Key).ToList()) known.Remove(name);
            return known;
        }
        static void Apply(TSqlStatement statement, Dictionary<string,LocalTable> known)
        {
            var create = statement as CreateTableStatement;
            if (create != null)
            {
                string name = TemporaryName(create.SchemaObjectName);
                if (name != null) Add(known, new LocalTable { Name = name, Offset = statement.StartOffset, Columns = Columns(create.Definition) });
                return;
            }
            var declare = statement as DeclareTableVariableStatement;
            if (declare != null)
            {
                Add(known, new LocalTable { Name = declare.Body.VariableName.Value, Offset = statement.StartOffset, Columns = Columns(declare.Body.Definition) });
                return;
            }
            var select = statement as SelectStatement;
            if (select != null && TemporaryName(select.Into) != null)
            {
                Add(known, new LocalTable { Name = TemporaryName(select.Into), Offset = statement.StartOffset, Query = select.QueryExpression,
                    Ctes = ShapeResolver.Definitions(select), Inputs = new Dictionary<string,LocalTable>(known, StringComparer.OrdinalIgnoreCase) });
                return;
            }
            var drop = statement as DropTableStatement;
            if (drop != null) { foreach (var obj in drop.Objects) { string name = TemporaryBase(obj); if (name != null) known.Remove(name); } return; }
            var alter = statement as AlterTableStatement;
            if (alter != null) { string name = TemporaryBase(alter.SchemaObjectName); if (name != null) known.Remove(name); return; }
            if (statement is UseStatement) { known.Clear(); return; }
            // Conditional DDL and dynamic/procedure execution cannot establish a reliable static shape.
            if (statement is IfStatement || statement is WhileStatement || statement is TryCatchStatement || statement is BeginEndBlockStatement || statement is ExecuteStatement)
            {
                var changes = new LocalChanges(); statement.Accept(changes);
                foreach (string name in changes.Names) known.Remove(name);
                if (changes.Execution) foreach (string name in known.Keys.Where(n => n.StartsWith("#", StringComparison.Ordinal)).ToList()) known.Remove(name);
            }
        }
        static void Add(Dictionary<string,LocalTable> known, LocalTable table)
        {
            if (known.ContainsKey(table.Name)) { known.Remove(table.Name); return; } // Duplicate declaration is not a valid new shape.
            known[table.Name] = table;
        }
        static List<ColumnShape> Columns(TableDefinition definition)
        {
            var primary=new HashSet<string>(definition.TableConstraints.OfType<UniqueConstraintDefinition>().Where(c=>c.IsPrimaryKey).SelectMany(c=>c.Columns).Select(c=>c.Column.MultiPartIdentifier.Identifiers.Last().Value),StringComparer.OrdinalIgnoreCase);
            var generator=new Sql160ScriptGenerator();
            return definition.ColumnDefinitions.Select(c=>{
                string type=null;if(c.DataType!=null)generator.GenerateScript(c.DataType,out type);
                var nullable=c.Constraints.OfType<NullableConstraintDefinition>().FirstOrDefault();
                bool pk=primary.Contains(c.ColumnIdentifier.Value)||c.Constraints.OfType<UniqueConstraintDefinition>().Any(k=>k.IsPrimaryKey);
                return new ColumnShape{Name=c.ColumnIdentifier.Value,TypeName=type,Nullable=pk?(bool?)false:nullable==null?null:(bool?)nullable.Nullable,
                    PrimaryKey=pk,ForeignKey=c.Constraints.OfType<ForeignKeyConstraintDefinition>().Any(),Identity=c.IdentityOptions!=null,Computed=c.ComputedColumnExpression!=null};
            }).ToList();
        }
        sealed class LocalChanges : TSqlFragmentVisitor
        {
            internal readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal bool Execution;
            void Touch(SchemaObjectName name) { string local = TemporaryBase(name); if (local != null) Names.Add(local); }
            public override void ExplicitVisit(CreateTableStatement node) { Touch(node.SchemaObjectName); }
            public override void ExplicitVisit(DeclareTableVariableStatement node) { Names.Add(node.Body.VariableName.Value); }
            public override void ExplicitVisit(DropTableStatement node) { foreach (var obj in node.Objects) Touch(obj); }
            public override void ExplicitVisit(SelectStatement node) { Touch(node.Into); base.ExplicitVisit(node); }
            public override void Visit(AlterTableStatement node) { Touch(node.SchemaObjectName); }
            public override void ExplicitVisit(ExecuteStatement node) { Execution = true; }
        }
    }
}
