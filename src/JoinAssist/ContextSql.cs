using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    // Recovery is an analysis-only copy. Original offsets and the editor text are never rewritten.
    internal static class ContextSql
    {
        sealed class Token
        {
            internal TSqlParserToken Value;
            internal int Depth;
        }

        internal static ScopeIndex Recover(string sql, int position, bool join, out ScopeNode scope)
        {
            scope = null;
            int depth = 0;
            var tokens = new List<Token>();
            foreach (var value in SqlAnalysis.Tokens(sql).Where(SqlAnalysis.Significant))
            {
                tokens.Add(new Token { Value = value, Depth = depth });
                if (value.TokenType == TSqlTokenType.LeftParenthesis) depth++;
                else if (value.TokenType == TSqlTokenType.RightParenthesis) depth = Math.Max(0, depth - 1);
            }
            int caretDepth = 0;
            foreach (var token in tokens.Where(t => t.Value.Offset < position))
            {
                if (token.Value.TokenType == TSqlTokenType.LeftParenthesis) caretDepth++;
                else if (token.Value.TokenType == TSqlTokenType.RightParenthesis) caretDepth = Math.Max(0, caretDepth - 1);
            }
            var selected = tokens.LastOrDefault(t => t.Value.TokenType == TSqlTokenType.Select && t.Value.Offset <= position && t.Depth <= caretDepth);
            if (selected == null) return null;
            int selectedIndex = tokens.IndexOf(selected), start = selected.Value.Offset, end = sql.Length;
            // A following SELECT is a separate statement/UNION branch even when a previous statement is unfinished.
            foreach (var token in tokens.Skip(selectedIndex + 1))
            {
                if (token.Depth < selected.Depth || token.Depth == selected.Depth &&
                    (token.Value.TokenType == TSqlTokenType.RightParenthesis || token.Value.TokenType == TSqlTokenType.Select ||
                     token.Value.TokenType == TSqlTokenType.Semicolon || token.Value.TokenType == TSqlTokenType.Go ||
                     new[] { "UNION", "EXCEPT", "INTERSECT", "UPDATE", "INSERT", "DELETE", "MERGE", "CREATE", "ALTER", "DROP", "DECLARE", "EXEC", "EXECUTE", "USE", "PRINT", "RETURN", "TRUNCATE", "IF", "WHILE", "BEGIN", "SET" }.Contains(token.Value.Text, StringComparer.OrdinalIgnoreCase)))
                { end = token.Value.Offset; break; }
            }
            if (position > end) return null;
            // Keep the CTE preamble belonging to this SELECT, never an earlier statement's CTEs.
            for (int i = selectedIndex - 1; i >= 0; i--)
            {
                var token = tokens[i];
                if (token.Depth < selected.Depth) break;
                if (token.Depth != selected.Depth) continue;
                if (token.Value.TokenType == TSqlTokenType.With && i + 1 < tokens.Count && tokens[i + 1].Value.TokenType != TSqlTokenType.LeftParenthesis)
                { start = token.Value.Offset; break; }
                if (token.Value.TokenType == TSqlTokenType.Select || token.Value.TokenType == TSqlTokenType.Semicolon || token.Value.TokenType == TSqlTokenType.Go) break;
            }
            var text = sql.ToCharArray();
            Blank(text, 0, start); Blank(text, end, text.Length);
            var same = tokens.Skip(selectedIndex + 1).Where(t => t.Depth == selected.Depth && t.Value.Offset < end).ToList();
            var from = same.FirstOrDefault(t => t.Value.TokenType == TSqlTokenType.From);
            if (from == null) return null;
            // SELECT expressions do not affect FROM bindings. Repair unfinished lists/CASE/functions as a unit.
            int projection = selected.Value.Offset + selected.Value.Text.Length;
            Blank(text, projection, from.Value.Offset);
            if (from.Value.Offset <= projection + 1) return null;
            text[projection] = ' '; text[projection + 1] = '*';
            // Filters/order clauses are irrelevant to visible sources. Their unfinished expressions must not hide FROM.
            var tail = same.FirstOrDefault(t => t.Value.Offset > from.Value.Offset &&
                new[] { "WHERE", "GROUP", "HAVING", "ORDER", "OPTION", "FOR", "OFFSET", "FETCH" }.Contains(t.Value.Text, StringComparer.OrdinalIgnoreCase));
            if (tail != null) Blank(text, tail.Value.Offset, end);
            if (!join)
            {
                // Recover aliases through syntactically unfinished ON conditions, but keep every JOIN/table source.
                var fromTokens = same.Where(t => t.Value.Offset > from.Value.Offset && (tail == null || t.Value.Offset < tail.Value.Offset)).ToList();
                for (int i = 0; i < fromTokens.Count; i++)
                {
                    if (fromTokens[i].Value.TokenType != TSqlTokenType.On) continue;
                    int a = fromTokens[i].Value.Offset + fromTokens[i].Value.Text.Length;
                    var next = fromTokens.Skip(i + 1).FirstOrDefault(t => new[] { "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "JOIN" }.Contains(t.Value.Text, StringComparer.OrdinalIgnoreCase));
                    int b = next == null ? tail == null ? end : tail.Value.Offset : next.Value.Offset;
                    if (b - a < 5) return null;
                    Blank(text, a, b); text[a + 1] = '1'; text[a + 2] = '='; text[a + 3] = '1';
                }
            }
            var index = SqlAnalysis.ParseIndex(new string(text));
            scope = index.Scopes.FirstOrDefault(s => s.Query.StartOffset == selected.Value.Offset);
            if (scope == null && start < selected.Value.Offset)
            {
                // An invalid CTE preamble must not hide ordinary tables in the final SELECT.
                // Discard the unresolved CTEs rather than fabricate their output columns.
                Blank(text, 0, selected.Value.Offset);
                index = SqlAnalysis.ParseIndex(new string(text));
                scope = index.Scopes.FirstOrDefault(s => s.Query.StartOffset == selected.Value.Offset);
            }
            return scope == null ? null : index;
        }

        static void Blank(char[] text, int start, int end)
        { for (int i = start; i < end; i++) if (text[i] != '\r' && text[i] != '\n') text[i] = ' '; }
    }
}
