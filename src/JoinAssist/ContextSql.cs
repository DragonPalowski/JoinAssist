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
        { return Recover(sql,position,join,out scope,0); }
        static ScopeIndex Recover(string sql,int position,bool join,out ScopeNode scope,int recursion)
        {
            scope = null;
            if(recursion>=12)return null;
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
            int branch=selectedIndex;
            for (int i = selectedIndex - 1; i >= 0; i--)
            {
                var token = tokens[i];
                if (token.Depth < selected.Depth) break;
                if (token.Depth != selected.Depth) continue;
                if (token.Value.TokenType == TSqlTokenType.With && i + 1 < tokens.Count && tokens[i + 1].Value.TokenType != TSqlTokenType.LeftParenthesis)
                { start = token.Value.Offset; break; }
                if(token.Value.TokenType==TSqlTokenType.Select)
                {
                    if(tokens.Skip(i+1).Take(branch-i-1).Any(t=>t.Depth==selected.Depth && new[]{"UNION","EXCEPT","INTERSECT"}.Contains(t.Value.Text,StringComparer.OrdinalIgnoreCase))) {branch=i;continue;}
                    break;
                }
                if (token.Value.TokenType == TSqlTokenType.Semicolon || token.Value.TokenType == TSqlTokenType.Go) break;
            }
            // While editing a CTE body, keep the earlier declarations from the same WITH list.
            // The parser can close the unfinished current body in its analysis-only copy.
            if(selected.Depth>0)
            {
                var opening=tokens.Take(selectedIndex).LastOrDefault(t=>t.Depth==selected.Depth-1 && t.Value.TokenType==TSqlTokenType.LeftParenthesis);
                int oi=opening==null?-1:tokens.IndexOf(opening);
                if(oi>0 && tokens[oi-1].Value.TokenType==TSqlTokenType.As)
                {
                    var with=tokens.Take(oi).LastOrDefault(t=>t.Depth==selected.Depth-1 && t.Value.TokenType==TSqlTokenType.With);
                    if(with!=null && !tokens.Where(t=>t.Value.Offset>with.Value.Offset && t.Value.Offset<opening.Value.Offset && t.Depth<=with.Depth)
                        .Any(t=>t.Value.TokenType==TSqlTokenType.Select || t.Value.TokenType==TSqlTokenType.Semicolon || t.Value.TokenType==TSqlTokenType.Go))start=with.Value.Offset;
                }
            }
            var text = sql.ToCharArray();
            Blank(text, 0, start); Blank(text, end, text.Length);
            // A later UNION branch needs the owning WITH definitions, not the earlier branch's expressions.
            if(branch!=selectedIndex && start<tokens[branch].Value.Offset)
                Blank(text,tokens[branch].Value.Offset,selected.Value.Offset);
            // Repair only irrelevant filters in preceding CTEs, retaining their output lists and lineage.
            foreach(var query in tokens.Where(t=>t.Value.Offset>=start && t.Value.Offset<end && !ReferenceEquals(t,selected) && t.Value.TokenType==TSqlTokenType.Select &&
                (t.Depth>selected.Depth || t.Value.Offset<selected.Value.Offset)).Take(200))
            {
                var filter=tokens.FirstOrDefault(t=>t.Value.Offset>query.Value.Offset && t.Depth==query.Depth && t.Value.TokenType==TSqlTokenType.Where);
                var close=tokens.FirstOrDefault(t=>t.Value.Offset>query.Value.Offset && t.Depth==query.Depth && t.Value.TokenType==TSqlTokenType.RightParenthesis);
                if(filter!=null && close!=null && filter.Value.Offset<close.Value.Offset)Blank(text,filter.Value.Offset,close.Value.Offset);
            }
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
            if(scope!=null && selected.Depth>0)
            {
                var parent=tokens.Take(selectedIndex).LastOrDefault(t=>t.Value.TokenType==TSqlTokenType.Select && t.Depth<selected.Depth);
                if(parent!=null && !tokens.Where(t=>t.Value.Offset>parent.Value.Offset && t.Value.Offset<selected.Value.Offset && t.Depth<=parent.Depth)
                    .Any(t=>t.Value.TokenType==TSqlTokenType.Semicolon || t.Value.TokenType==TSqlTokenType.Go))
                {
                    ScopeNode outer;
                    Recover(sql,parent.Value.Offset+parent.Value.Text.Length,false,out outer,recursion+1);
                    if(outer!=null)
                    {
                        var ctes=new Dictionary<string,CommonTableExpression>(outer.Ctes,StringComparer.OrdinalIgnoreCase);
                        foreach(var pair in scope.Ctes)ctes[pair.Key]=pair.Value;
                        scope.Ctes=ctes;
                        // FROM/JOIN derived tables cannot correlate; APPLY and scalar subqueries can.
                        bool correlates=true;
                        for(int d=selected.Depth-1;d>=parent.Depth;d--)
                        {
                            var opening=tokens.Take(selectedIndex).LastOrDefault(t=>t.Value.TokenType==TSqlTokenType.LeftParenthesis && t.Depth==d);
                            int openingIndex=opening==null?-1:tokens.IndexOf(opening);
                            string preceding=openingIndex>0?tokens[openingIndex-1].Value.Text:"";
                            if(new[]{"FROM","JOIN",","}.Contains(preceding,StringComparer.OrdinalIgnoreCase))correlates=false;
                            if(string.Equals(preceding,"AS",StringComparison.OrdinalIgnoreCase) && tokens.Take(Math.Max(0,openingIndex)).Any(t=>t.Depth==d && t.Value.TokenType==TSqlTokenType.With))correlates=false;
                        }
                        if(correlates)scope.Parent=outer;
                    }
                }
            }
            return scope == null ? null : index;
        }

        static void Blank(char[] text, int start, int end)
        { for (int i = start; i < end; i++) if (text[i] != '\r' && text[i] != '\n') text[i] = ' '; }
    }
}
