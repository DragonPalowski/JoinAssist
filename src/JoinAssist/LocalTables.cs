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
            // Ordinary scripts have no local declarations: avoid a second full parse on every keystroke.
            if(!SqlAnalysis.Tokens(sql).Any(t=>t.Offset<position && SqlAnalysis.Significant(t) && (t.Text??"").Length>0 && (t.Text[0]=='#' || t.Text[0]=='@' || t.Text.StartsWith("[#",StringComparison.Ordinal) || t.Text.StartsWith("\"#",StringComparison.Ordinal))))
                return new Dictionary<string,LocalTable>(StringComparer.OrdinalIgnoreCase);
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
            if(errors.Count>0)return RecoverBatches(sql.Substring(0,position));
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
        static Dictionary<string,LocalTable> RecoverBatches(string sql)
        {
            var known=new Dictionary<string,LocalTable>(StringComparer.OrdinalIgnoreCase);
            var separators=SqlAnalysis.Tokens(sql).Where(t=>t.TokenType==TSqlTokenType.Go).ToList();
            int start=0;
            foreach(var end in separators.Select(t=>t.Offset).Concat(new[]{sql.Length}))
            {
                foreach(string name in known.Keys.Where(n=>n.StartsWith("@",StringComparison.Ordinal)).ToList())known.Remove(name);
                string batch=sql.Substring(start,end-start);
                IList<ParseError> errors;
                var tree=new TSql160Parser(true).Parse(new StringReader(batch),out errors) as TSqlScript;
                var tokens=SqlAnalysis.Tokens(batch).Where(SqlAnalysis.Significant).ToList();
                if(errors.Count>0 && UncertainControl(tokens))
                {
                    // Error recovery may detach a CREATE from its IF/procedure wrapper.
                    // Never promote such a fragment into an unconditional declaration.
                    foreach(var token in tokens)known.Remove(SqlName.Decode(token.Text));
                    if(tokens.Any(t=>t.TokenType==TSqlTokenType.Use))known.Clear();
                    if(tokens.Any(t=>t.TokenType==TSqlTokenType.Execute || string.Equals(t.Text,"EXEC",StringComparison.OrdinalIgnoreCase)))
                        foreach(string name in known.Keys.Where(n=>n.StartsWith("#",StringComparison.Ordinal)).ToList())known.Remove(name);
                }
                else if(errors.Count==0 || !FlatRecovery(tokens))
                {
                    if(tree!=null)foreach(var statement in tree.Batches.SelectMany(b=>b.Statements))
                        if(!errors.Any(e=>statement.StartOffset<=e.Offset && e.Offset<=statement.StartOffset+statement.FragmentLength))Apply(statement,known,start);
                }
                else
                {
                    // ScriptDOM can omit a complete batch after an earlier unfinished SELECT.
                    // Recover only independently valid top-level statements, never procedures,
                    // conditional/control-flow bodies, CTE preambles or nested SELECTs.
                    var boundaries=new List<int>{0};int depth=0;
                    foreach(var token in tokens)
                    {
                        if(token.TokenType==TSqlTokenType.RightParenthesis)depth--;
                        if(depth==0 && new[]{"SELECT","CREATE","DECLARE","DROP","ALTER","PRINT","SET","USE"}.Contains(token.Text,StringComparer.OrdinalIgnoreCase) && token.TokenType!=TSqlTokenType.QuotedIdentifier && token.TokenType!=TSqlTokenType.Identifier && token.Offset>boundaries.Last())boundaries.Add(token.Offset);
                        if(token.TokenType==TSqlTokenType.LeftParenthesis)depth++;
                    }
                    boundaries.Add(batch.Length);
                    if(boundaries.Count>2002)throw new InvalidOperationException("Troppe istruzioni per recuperare le definizioni locali: separa lo script in batch GO.");
                    for(int i=0;i<boundaries.Count-1;i++)
                    {
                        int offset=boundaries[i];string part=batch.Substring(offset,boundaries[i+1]-offset);
                        var parsed=new TSql160Parser(true).Parse(new StringReader(part),out errors) as TSqlScript;
                        var statements=parsed==null?new List<TSqlStatement>():parsed.Batches.SelectMany(b=>b.Statements).ToList();
                        if(errors.Count==0 && statements.Count==1)Apply(statements[0],known,start+offset);
                        else
                        {
                            var significant=SqlAnalysis.Tokens(part).Where(SqlAnalysis.Significant).ToList();
                            if(significant.Count>0 && new[]{"CREATE","ALTER","DROP","DECLARE"}.Contains(significant[0].Text,StringComparer.OrdinalIgnoreCase))
                                foreach(var token in significant.Where(t=>t.TokenType==TSqlTokenType.Identifier || t.TokenType==TSqlTokenType.QuotedIdentifier || t.TokenType==TSqlTokenType.Variable))known.Remove(SqlName.Decode(token.Text));
                        }
                        if(known.Count>200)throw new InvalidOperationException("Lo script contiene più di 200 tabelle locali visibili.");
                    }
                }
                if(known.Count>200)throw new InvalidOperationException("Lo script contiene più di 200 tabelle locali visibili.");
                var go=separators.FirstOrDefault(t=>t.Offset==end);start=go==null?end:go.Offset+go.Text.Length;
            }
            return known;
        }
        static bool UncertainControl(IList<TSqlParserToken> tokens)
        {
            for(int i=0;i<tokens.Count;i++)
            {
                var t=tokens[i];
                if(t.TokenType==TSqlTokenType.QuotedIdentifier || t.TokenType==TSqlTokenType.Identifier || t.TokenType==TSqlTokenType.AsciiStringLiteral || t.TokenType==TSqlTokenType.UnicodeStringLiteral)continue;
                if(new[]{"BEGIN","WHILE","TRY","CATCH","EXEC","EXECUTE","PROCEDURE","PROC","FUNCTION","TRIGGER","VIEW","RETURN","USE"}.Contains(t.Text,StringComparer.OrdinalIgnoreCase))return true;
                if(t.TokenType==TSqlTokenType.If && !(i>=2 && tokens[i-1].TokenType==TSqlTokenType.Table && tokens[i-2].TokenType==TSqlTokenType.Drop && i+1<tokens.Count && tokens[i+1].Text.Equals("EXISTS",StringComparison.OrdinalIgnoreCase)))return true;
            }
            return false;
        }
        static bool FlatRecovery(IList<TSqlParserToken> tokens)
        {
            int depth=0;
            for(int i=0;i<tokens.Count;i++)
            {
                var t=tokens[i];
                if(t.TokenType==TSqlTokenType.QuotedIdentifier || t.TokenType==TSqlTokenType.Identifier || t.TokenType==TSqlTokenType.AsciiStringLiteral || t.TokenType==TSqlTokenType.UnicodeStringLiteral)continue;
                if(t.TokenType==TSqlTokenType.LeftParenthesis)depth++;
                if(t.TokenType==TSqlTokenType.RightParenthesis && --depth<0)return false;
                if(new[]{"BEGIN","WHILE","TRY","CATCH","EXEC","EXECUTE","PROCEDURE","PROC","FUNCTION","TRIGGER","VIEW","WITH","INSERT","UPDATE","DELETE","MERGE","RETURN"}.Contains(t.Text,StringComparer.OrdinalIgnoreCase))return false;
                if(t.TokenType==TSqlTokenType.If && !(i>=2 && tokens[i-1].TokenType==TSqlTokenType.Table && tokens[i-2].TokenType==TSqlTokenType.Drop && i+1<tokens.Count && tokens[i+1].Text.Equals("EXISTS",StringComparison.OrdinalIgnoreCase)))return false;
            }
            return depth==0;
        }
        static void Apply(TSqlStatement statement, Dictionary<string,LocalTable> known,int offset=0)
        {
            var create = statement as CreateTableStatement;
            if (create != null)
            {
                string name = TemporaryName(create.SchemaObjectName);
                if (name != null) Add(known, new LocalTable { Name = name, Offset = offset+statement.StartOffset, Columns = Columns(create.Definition) });
                return;
            }
            var declare = statement as DeclareTableVariableStatement;
            if (declare != null)
            {
                Add(known, new LocalTable { Name = declare.Body.VariableName.Value, Offset = offset+statement.StartOffset, Columns = Columns(declare.Body.Definition) });
                return;
            }
            var select = statement as SelectStatement;
            if (select != null && TemporaryName(select.Into) != null)
            {
                Add(known, new LocalTable { Name = TemporaryName(select.Into), Offset = offset+statement.StartOffset, Query = select.QueryExpression,
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
                    PrimaryKey=pk,ForeignKey=c.Constraints.OfType<ForeignKeyConstraintDefinition>().Any(),Identity=c.IdentityOptions!=null,Computed=c.ComputedColumnExpression!=null,
                    HasDefault=c.DefaultConstraint!=null,Generated=c.GeneratedAlways!=null};
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
