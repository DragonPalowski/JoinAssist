using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    public sealed class ScopeNode
    {
        public QuerySpecification Query;
        public ScopeNode Parent;
        public Dictionary<string,CommonTableExpression> Ctes;
    }
    public sealed class ScopeIndex : TSqlFragmentVisitor
    {
        public readonly List<ScopeNode> Scopes = new List<ScopeNode>();
        public readonly List<QualifiedJoin> Joins = new List<QualifiedJoin>();
        ScopeNode current, barrier;
        bool lateral;
        IList<CommonTableExpression> declarations;
        Dictionary<string,CommonTableExpression> ctes = new Dictionary<string,CommonTableExpression>(StringComparer.OrdinalIgnoreCase);
        public override void ExplicitVisit(SelectStatement node)
        {
            var previous = ctes;
            var previousDeclarations = declarations;
            ctes = new Dictionary<string,CommonTableExpression>(ctes, StringComparer.OrdinalIgnoreCase);
            declarations = node.WithCtesAndXmlNamespaces == null ? null : node.WithCtesAndXmlNamespaces.CommonTableExpressions;
            if (node.WithCtesAndXmlNamespaces != null)
                foreach (var cte in node.WithCtesAndXmlNamespaces.CommonTableExpressions) ctes[cte.ExpressionName.Value] = cte;
            base.ExplicitVisit(node); ctes = previous; declarations = previousDeclarations;
        }
        public override void ExplicitVisit(CommonTableExpression node)
        {
            var previous = ctes;
            ctes = new Dictionary<string,CommonTableExpression>(ctes, StringComparer.OrdinalIgnoreCase);
            if (declarations != null)
            {
                bool reached = false;
                foreach (var other in declarations)
                {
                    if (reached) ctes.Remove(other.ExpressionName.Value);
                    if (ReferenceEquals(other, node)) reached = true;
                }
            }
            base.ExplicitVisit(node); ctes = previous;
        }
        public override void ExplicitVisit(QuerySpecification node)
        {
            var previous = current;
            current = new ScopeNode { Query = node, Parent = ReferenceEquals(previous, barrier) ? null : previous, Ctes = ctes };
            Scopes.Add(current); base.ExplicitVisit(node); current = previous;
        }
        public override void ExplicitVisit(QueryDerivedTable node)
        {
            var previous = barrier; bool previousLateral = lateral;
            if (!lateral) barrier = current;
            lateral = false; base.ExplicitVisit(node); barrier = previous; lateral = previousLateral;
        }
        public override void ExplicitVisit(UnqualifiedJoin node)
        {
            node.FirstTableReference.Accept(this);
            bool previous = lateral;
            lateral = node.UnqualifiedJoinType == UnqualifiedJoinType.CrossApply || node.UnqualifiedJoinType == UnqualifiedJoinType.OuterApply;
            node.SecondTableReference.Accept(this); lateral = previous;
        }
        public override void ExplicitVisit(QualifiedJoin node) { Joins.Add(node); base.ExplicitVisit(node); }
    }
    public static class SqlAnalysis
    {
        internal const string Marker = "__JoinAssist_scope_7c64d2";
        public static IList<TSqlParserToken> Tokens(string sql)
        {
            IList<ParseError> errors; return new TSql160Parser(true).GetTokenStream(new StringReader(sql), out errors);
        }
        public static bool Significant(TSqlParserToken t)
        {
            return t.TokenType != TSqlTokenType.WhiteSpace && t.TokenType != TSqlTokenType.SingleLineComment &&
                t.TokenType != TSqlTokenType.MultilineComment && t.TokenType != TSqlTokenType.EndOfFile;
        }
        public static void CheckCaret(string sql, int caret)
        {
            if (caret < 0 || caret > sql.Length) throw new ArgumentOutOfRangeException("caret");
            if (sql.Length > 500000) throw new InvalidOperationException("Query troppo grande per il completamento automatico: usa una scheda con meno di 500.000 caratteri.");
            var token = Tokens(sql).FirstOrDefault(t => t.Offset < caret && caret <= t.Offset + (t.Text ?? "").Length &&
                (t.TokenType == TSqlTokenType.SingleLineComment || t.TokenType == TSqlTokenType.MultilineComment ||
                 t.TokenType == TSqlTokenType.AsciiStringLiteral || t.TokenType == TSqlTokenType.UnicodeStringLiteral));
            if (token != null) throw new InvalidOperationException("Sposta il cursore fuori da commenti e stringhe.");
            var prefix = Tokens(sql.Substring(0, caret)).Where(t => t.TokenType != TSqlTokenType.EndOfFile).LastOrDefault();
            int end = prefix == null ? 0 : prefix.Offset + (prefix.Text ?? "").Length;
            string gap = sql.Substring(end, caret - end).TrimStart();
            if (gap.StartsWith("'", StringComparison.Ordinal) || gap.StartsWith("N'", StringComparison.OrdinalIgnoreCase) ||
                gap.StartsWith("/*", StringComparison.Ordinal) || gap.StartsWith("--", StringComparison.Ordinal))
                throw new InvalidOperationException("Sposta il cursore fuori da commenti e stringhe.");
        }
        static void CheckDatabaseContext(string sql, int caret)
        {
            var before = Tokens(sql).Where(t => t.Offset < caret).ToList();
            int batch = before.FindLastIndex(t => t.TokenType == TSqlTokenType.Go);
            if (before.Skip(batch + 1).Any(t => t.TokenType == TSqlTokenType.Use))
                throw new InvalidOperationException("Seleziona il database dalla barra della scheda e usa una query senza USE nello stesso batch.");
        }
        internal static ScopeIndex ParseIndex(string sql)
        {
            IList<ParseError> errors;
            var parser = new TSql160Parser(true);
            var script = parser.Parse(new StringReader(sql), out errors);
            // While writing inside an unfinished CTE or derived query, close the outstanding parentheses.
            if (errors.Count > 0)
            {
                int depth = 0;
                foreach (var t in Tokens(sql)) { if (t.TokenType == TSqlTokenType.LeftParenthesis) depth++; if (t.TokenType == TSqlTokenType.RightParenthesis) depth--; }
                if (depth > 0 && depth < 32)
                {
                    string balanced = sql + "\n" + new string(')', depth);
                    IList<ParseError> repairedErrors;
                    var repaired = parser.Parse(new StringReader(balanced), out repairedErrors);
                    var first = Tokens(sql).FirstOrDefault(Significant);
                    if (first != null && first.TokenType == TSqlTokenType.With && repairedErrors.Count > 0)
                        repaired = parser.Parse(new StringReader(balanced + " SELECT 1;"), out repairedErrors);
                    if (repairedErrors.Count < errors.Count) script = repaired;
                }
            }
            var index = new ScopeIndex(); if (script != null) script.Accept(index); return index;
        }
        static ScopeNode At(ScopeIndex index, int position)
        {
            return index.Scopes.Where(s => s.Query.StartOffset <= position && position < s.Query.StartOffset + s.Query.FragmentLength)
                .OrderBy(s => s.Query.FragmentLength).FirstOrDefault();
        }
        public static QueryContext AnalyzeJoin(string sql, int caret, bool predicate)
        {
            CheckCaret(sql, caret); CheckDatabaseContext(sql, caret);
            var before = Tokens(sql).Where(t => t.Offset < caret && Significant(t)).ToList();
            if (before.Count == 0) throw new InvalidOperationException("Posiziona il cursore dopo FROM o JOIN.");
            if (before.Any(t => t.Offset < caret && caret < t.Offset + t.Text.Length)) throw new InvalidOperationException("Sposta il cursore dopo il nome completo.");
            var suffix = Tokens(sql).FirstOrDefault(t => t.Offset >= caret && Significant(t));
            if (suffix != null && !(new[] { ";", "GO", ")", "WHERE", "GROUP", "HAVING", "ORDER", "OPTION", "JOIN", "INNER", "LEFT", "RIGHT", "FULL" }.Contains(suffix.Text, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Sposta il cursore dopo le tabelle e prima dei filtri.");
            int replaceStart = caret;
            string kind = "INNER", inserted;
            bool replaces = !predicate && before.Last().TokenType == TSqlTokenType.Join;
            if (predicate)
                inserted = " " + Marker + " = 1 ";
            else if (replaces)
            {
                int i = before.Count - 1;
                if (i > 0 && string.Equals(before[i - 1].Text, "OUTER", StringComparison.OrdinalIgnoreCase)) i--;
                if (i > 0 && new[] { "INNER", "LEFT", "RIGHT", "FULL", "CROSS" }.Contains(before[i - 1].Text, StringComparer.OrdinalIgnoreCase))
                {
                    i--; kind = before[i].Text.ToUpperInvariant();
                    if (kind == "CROSS") throw new InvalidOperationException("CROSS JOIN non prevede una condizione FK.");
                }
                replaceStart = before[i].Offset;
                if (Tokens(sql).Any(t => t.Offset >= replaceStart && t.Offset < caret &&
                    (t.TokenType == TSqlTokenType.SingleLineComment || t.TokenType == TSqlTokenType.MultilineComment)))
                    throw new InvalidOperationException("I commenti nel JOIN incompleto vengono mantenuti: sposta il cursore dopo il commento.");
                inserted = kind + " JOIN " + Marker + " ON 1 = 1 ";
            }
            else inserted = "\nINNER JOIN " + Marker + " ON 1 = 1\n";
            string changed = sql.Substring(0, replaceStart) + inserted + sql.Substring(caret);
            int marker = replaceStart + inserted.IndexOf(Marker, StringComparison.Ordinal);
            var index = ParseIndex(changed);
            var scope = At(index, marker);
            if (scope == null || scope.Query.FromClause == null)
            {
                ScopeNode recovered;
                var repaired = ContextSql.Recover(changed, marker, true, out recovered);
                if (repaired != null) { index = repaired; scope = recovered; }
            }
            if (scope == null || scope.Query.FromClause == null) throw new InvalidOperationException("Il punto corrente non appartiene a un FROM/JOIN riconoscibile.");
            var join = index.Joins.Where(j => j.StartOffset <= marker && marker < j.StartOffset + j.FragmentLength &&
                (predicate ? j.SearchCondition != null && j.SearchCondition.StartOffset <= marker && marker < j.SearchCondition.StartOffset + j.SearchCondition.FragmentLength :
                j.SecondTableReference is NamedTableReference && ((NamedTableReference)j.SecondTableReference).SchemaObject.BaseIdentifier.Value == Marker))
                .OrderBy(j => j.FragmentLength).FirstOrDefault();
            if (join == null) throw new InvalidOperationException("Completa prima la condizione del JOIN precedente.");
            var context = new QueryContext { ReplaceStart = replaceStart, ReplaceLength = caret - replaceStart, JoinKind = kind, ReplacesJoin = replaces, PredicateOnly = predicate, Ctes = scope.Ctes };
            Collect(join.FirstTableReference, context.Tables, scope.Ctes);
            if (predicate)
            {
                if (!(join.SecondTableReference is NamedTableReference) && !(join.SecondTableReference is QueryDerivedTable) && !(join.SecondTableReference is VariableTableReference))
                    throw new InvalidOperationException("Il target del JOIN deve essere una tabella, una CTE o una sottoquery con alias.");
                Collect(join.SecondTableReference, context.Tables, scope.Ctes);
                context.LastJoinTarget = context.Tables.LastOrDefault();
                context.ReplaceStart = caret; context.ReplaceLength = 0;
            }
            Validate(context.Tables);
            foreach (var cte in context.Ctes.Values)
                if (cte.QueryExpression.StartOffset + cte.QueryExpression.FragmentLength <= marker)
                    context.CteTargets.Add(CteUse(cte.ExpressionName.Value, null, cte, scope.Ctes));
            context.Locals = LocalTables.Read(sql, caret);
            return context;
        }
        public static QueryContext ExpressionScope(string sql, int start, int length)
        {
            CheckCaret(sql, start); CheckDatabaseContext(sql, start);
            string changed = sql.Remove(start, length).Insert(start, Marker);
            var index = ParseIndex(changed);
            var scope = At(index, start);
            if (scope == null)
            {
                // An unfinished scalar after WHERE/ON is not yet a Boolean expression.
                // A temporary comparison lets ScriptDOM retain the surrounding FROM/alias scopes.
                index = ParseIndex(changed.Insert(start + Marker.Length, " = 1 "));
                scope = At(index, start);
            }
            if (scope == null)
            {
                ScopeNode recovered;
                var repaired = ContextSql.Recover(changed, start, false, out recovered);
                if (repaired != null) { index = repaired; scope = recovered; }
            }
            var context = new QueryContext { ReplaceStart = start, ReplaceLength = length, Locals = LocalTables.Read(sql, start) };
            if (scope == null) return context;
            context.Ctes = scope.Ctes;
            if (scope.Query.FromClause != null)
                foreach (var table in scope.Query.FromClause.TableReferences) Collect(table, context.Tables, scope.Ctes);
            int level = 0;
            for (var parent = scope.Parent; parent != null; parent = parent.Parent)
            {
                level++;
                if (parent.Query.FromClause != null)
                    foreach (var table in parent.Query.FromClause.TableReferences)
                    {
                        int previous = context.OuterTables.Count;
                        Collect(table, context.OuterTables, parent.Ctes);
                        foreach (var use in context.OuterTables.Skip(previous)) use.ScopeLevel = level;
                    }
            }
            context.Tables.RemoveAll(t => t.Name == Marker);
            return context;
        }
        public static TableUse CteUse(string name, string alias, CommonTableExpression cte, Dictionary<string,CommonTableExpression> definitions)
        {
            return new TableUse { Name = name, Alias = alias, IsVirtual = true, Definition = cte.QueryExpression, ExplicitColumns = cte.Columns, Ctes = definitions, Offset = cte.StartOffset };
        }
        public static void Collect(TableReference table, List<TableUse> list, Dictionary<string,CommonTableExpression> ctes)
        {
            var named = table as NamedTableReference;
            if (named != null)
            {
                var ids = named.SchemaObject.Identifiers;
                if (ids.Count > 2 || ids.Count < 1 || ids.Any(i => string.IsNullOrEmpty(i.Value)))
                    throw new InvalidOperationException("Il completamento usa oggetti del database corrente; i riferimenti ad altri database non sono ancora risolti.");
                string name = ids.Last().Value, alias = named.Alias == null ? null : named.Alias.Value;
                CommonTableExpression cte;
                var use = ids.Count == 1 && ctes.TryGetValue(name, out cte) ? CteUse(name, alias, cte, ctes) :
                    new TableUse { Name = name, Schema = ids.Count == 2 ? ids[0].Value : null, Alias = alias, Ctes = ctes, IsLocal = LocalTables.IsLocal(name) };
                use.Offset = named.StartOffset; list.Add(use); return;
            }
            var variable = table as VariableTableReference;
            if (variable != null)
            {
                list.Add(new TableUse { Name = variable.Variable.Name, Alias = variable.Alias == null ? null : variable.Alias.Value,
                    IsLocal = true, IsVirtual = true, Ctes = ctes, Offset = variable.StartOffset }); return;
            }
            var derived = table as QueryDerivedTable;
            if (derived != null)
            {
                if (derived.Alias == null) throw new InvalidOperationException("Assegna un alias alla sottoquery nel FROM.");
                list.Add(new TableUse { Name = derived.Alias.Value, Alias = derived.Alias.Value, IsVirtual = true, Definition = derived.QueryExpression, ExplicitColumns = derived.Columns, Ctes = ctes, Offset = derived.StartOffset }); return;
            }
            var function = table as SchemaObjectFunctionTableReference;
            if (function != null)
            {
                var ids = function.SchemaObject.Identifiers;
                if (ids.Count > 2) throw new InvalidOperationException("Usa una funzione del database corrente.");
                list.Add(new TableUse { Name = ids.Last().Value, Schema = ids.Count == 2 ? ids[0].Value : null, Alias = function.Alias == null ? null : function.Alias.Value,
                    IsFunction = true, Ctes = ctes, Offset = function.StartOffset }); return;
            }
            var join = table as QualifiedJoin;
            if (join != null) { Collect(join.FirstTableReference, list, ctes); Collect(join.SecondTableReference, list, ctes); return; }
            var unqualified = table as UnqualifiedJoin;
            if (unqualified != null) { Collect(unqualified.FirstTableReference, list, ctes); Collect(unqualified.SecondTableReference, list, ctes); return; }
            var parenthesis = table as JoinParenthesisTableReference;
            if (parenthesis != null) { Collect(parenthesis.Join, list, ctes); return; }
            throw new InvalidOperationException("Questa espressione FROM non espone ancora colonne risolvibili dal componente.");
        }
        public static void Validate(List<TableUse> tables)
        {
            if (tables.Count == 0 || tables.Count > 40) throw new InvalidOperationException("Usa da 1 a 40 sorgenti nella query.");
            foreach (var group in tables.Where(t => t.Alias == null).GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var table in group) table.NeedsQualifiedReference = true;
            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in tables)
                if (!refs.Add(table.Reference)) throw new InvalidOperationException("Assegna alias distinti alle istanze della stessa tabella.");
        }
    }
}
