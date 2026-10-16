using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    public enum CompletionMode { Join, Predicate, Table, Column, Expression }
    public sealed class AutomaticTrigger
    {
        public QueryContext Context;
        public string BaseKey;
        public string[] Prefix, Qualifier = new string[0];
        public int Caret;
        public CompletionMode Mode;
        public string Label { get { return Mode == CompletionMode.Predicate ? "Condizioni ON" : Mode == CompletionMode.Table ? "Tabelle e viste" : Mode == CompletionMode.Column || Mode == CompletionMode.Expression ? "Colonne" : Context.JoinKind + " JOIN"; } }
        public static AutomaticTrigger TryCreate(string sql, int caret)
        {
            try { return Create(sql, caret); }
            catch (InvalidOperationException) { return null; }
        }
        static AutomaticTrigger Create(string sql, int caret)
        {
            SqlAnalysis.CheckCaret(sql, caret);
            var tokens = SqlAnalysis.Tokens(sql.Substring(0, caret));
            var last = tokens.LastOrDefault(t => t.TokenType != TSqlTokenType.WhiteSpace && t.TokenType != TSqlTokenType.EndOfFile);
            if (last != null && last.TokenType == TSqlTokenType.On && last.Offset + last.Text.Length < caret &&
                sql.Substring(last.Offset + last.Text.Length, caret - last.Offset - last.Text.Length).All(char.IsWhiteSpace))
            {
                var context = SqlAnalysis.AnalyzeJoin(sql, caret, true);
                return new AutomaticTrigger { Context = context, Caret = caret, Prefix = new string[0], Mode = CompletionMode.Predicate,
                    BaseKey = "ON\0" + sql.Substring(0, caret) + "\0" + sql.Substring(caret) };
            }
            var join = tokens.LastOrDefault(t => t.TokenType == TSqlTokenType.Join);
            if (join != null)
            {
                int tail = join.Offset + join.Text.Length;
                if (tail < caret && char.IsWhiteSpace(sql[tail]))
                {
                    var prefix = ReadPrefix(sql.Substring(tail, caret - tail));
                    if (prefix != null)
                    {
                        string skeleton = sql.Substring(0, tail) + " " + sql.Substring(caret);
                        try
                        {
                            var context = QueryAnalyzer.Analyze(skeleton, tail + 1);
                            if (context.ReplacesJoin)
                            {
                                context.ReplaceLength = caret - context.ReplaceStart;
                                return new AutomaticTrigger { Context = context, Caret = caret, Prefix = prefix, Mode = CompletionMode.Join,
                                    BaseKey = context.JoinKind + "\0" + sql.Substring(0, context.ReplaceStart) + "\0" + sql.Substring(caret) };
                            }
                        }
                        catch (InvalidOperationException) { }
                    }
                }
            }
            return NameTrigger(sql, caret);
        }
        static AutomaticTrigger NameTrigger(string sql, int caret)
        {
            var before = SqlAnalysis.Tokens(sql.Substring(0, caret)).Where(SqlAnalysis.Significant).ToList();
            int start = caret, end = caret;
            string prefix = "";
            int lexEnd = before.Count == 0 ? 0 : before.Last().Offset + before.Last().Text.Length;
            string gap = sql.Substring(lexEnd, caret - lexEnd);
            string trimmed = gap.TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("\"", StringComparison.Ordinal))
            {
                start = lexEnd + gap.Length - trimmed.Length; prefix = SqlName.Decode(trimmed);
                char close = trimmed[0] == '[' ? ']' : '"';
                for (int i = caret; i < sql.Length && sql[i] != '\r' && sql[i] != '\n'; i++)
                    if (sql[i] == close)
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == close) { i++; continue; }
                        end = i + 1; break;
                    }
            }
            else if (before.Count > 0 && before.Last().Offset + before.Last().Text.Length == caret && IsName(before.Last().Text))
            {
                var token = before.Last(); start = token.Offset; prefix = SqlName.Decode(token.Text); before.RemoveAt(before.Count - 1);
                var full = SqlAnalysis.Tokens(sql).FirstOrDefault(t => t.Offset == start && IsName(t.Text));
                if (full != null) end = full.Offset + full.Text.Length;
            }
            var qualifier = new List<string>();
            int chainStart = start;
            while (before.Count >= 2 && before.Last().TokenType == TSqlTokenType.Dot && IsName(before[before.Count - 2].Text))
            {
                var part = before[before.Count - 2]; qualifier.Insert(0, SqlName.Decode(part.Text)); chainStart = part.Offset;
                before.RemoveRange(before.Count - 2, 2);
                if (qualifier.Count > 2) return null;
            }
            string previous = before.Count == 0 ? "" : before.Last().Text.ToUpperInvariant();
            bool tablePosition = new[] { "FROM", "JOIN", "UPDATE", "INTO", "APPLY" }.Contains(previous);
            if (previous == ",")
            {
                int depth = 0;
                for (int i = before.Count - 2; i >= 0; i--)
                {
                    if (before[i].TokenType == TSqlTokenType.RightParenthesis) depth++;
                    if (before[i].TokenType == TSqlTokenType.LeftParenthesis) { if (depth == 0) break; depth--; }
                    if (depth != 0) continue;
                    if (new[] { "SELECT", "FROM", "WHERE", "ON", "GROUP", "ORDER", "SET", ";", "GO" }.Contains(before[i].Text.ToUpperInvariant()))
                    { tablePosition = before[i].TokenType == TSqlTokenType.From; break; }
                }
            }
            if (tablePosition && qualifier.Count > 1) return null;
            if (LocalTables.IsLocal(prefix) && !tablePosition) return null;
            CompletionMode mode = tablePosition ? CompletionMode.Table : qualifier.Count > 0 ? CompletionMode.Column : CompletionMode.Expression;
            if (mode == CompletionMode.Expression && (prefix.Length == 0 || before.Count == 0)) return null;
            if (mode == CompletionMode.Expression && !new[] { "SELECT", "WHERE", "ON", "AND", "OR", "BY", "HAVING", "SET", ",", "(", "=", ">", "<", "+", "-" }.Contains(previous)) return null;
            if (tablePosition) start = chainStart;
            var context = SqlAnalysis.ExpressionScope(sql, start, end - start);
            context.ReplaceStart = start; context.ReplaceLength = end - start;
            return new AutomaticTrigger { Context = context, Caret = caret, Mode = mode, Qualifier = qualifier.ToArray(),
                Prefix = tablePosition && qualifier.Count == 1 ? new[] { qualifier[0], prefix } : new[] { prefix },
                BaseKey = mode + "\0" + string.Join(".", qualifier) + "\0" + sql.Substring(0, start) + "\0" + sql.Substring(end) };
        }
        static bool IsName(string text)
        {
            return !string.IsNullOrEmpty(text) && (text[0] == '[' || text[0] == '"' || text.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '@' || c == '#'));
        }
        internal static string[] ReadPrefix(string text)
        {
            int i = 0; while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i == text.Length) return new string[0];
            var parts = new List<string>();
            while (i < text.Length)
            {
                if (parts.Count == 2) return null;
                var part = new System.Text.StringBuilder();
                if (text[i] == '[' || text[i] == '"')
                {
                    char close = text[i++] == '[' ? ']' : '"'; bool ended = false;
                    while (i < text.Length)
                    {
                        char c = text[i++]; if (c == '\r' || c == '\n') return null;
                        if (c == close)
                        {
                            if (i < text.Length && text[i] == close) { part.Append(close); i++; }
                            else { ended = true; break; }
                        }
                        else part.Append(c);
                    }
                    parts.Add(part.ToString()); if (!ended) return parts.ToArray();
                }
                else
                {
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '@' || text[i] == '$' || text[i] == '#')) part.Append(text[i++]);
                    if (part.Length == 0) return null;
                    string word = part.ToString();
                    if (new[] { "ON", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "AS", "SELECT" }.Contains(word, StringComparer.OrdinalIgnoreCase)) return null;
                    parts.Add(word);
                }
                if (i == text.Length) return parts.ToArray();
                if (text[i] != '.') return null;
                i++; if (i == text.Length) { if (parts.Count == 2) return null; parts.Add(""); return parts.ToArray(); }
            }
            return null;
        }
        public bool Matches(JoinProposal proposal)
        {
            if (Mode == CompletionMode.Predicate) return true;
            if (proposal.IsName && (Mode == CompletionMode.Column || Mode == CompletionMode.Expression))
                return NameMatching.Score(proposal.SearchName, Prefix.LastOrDefault()) != int.MaxValue;
            if (Prefix.Length == 0) return true;
            if (Prefix.Length == 1) return NameMatching.Score(proposal.TargetTable, Prefix[0]) != int.MaxValue ||
                !LocalTables.IsLocal(Prefix[0]) && (proposal.TargetSchema ?? "").StartsWith(Prefix[0], StringComparison.OrdinalIgnoreCase);
            return string.Equals(proposal.TargetSchema, Prefix[0], StringComparison.OrdinalIgnoreCase) &&
                NameMatching.Score(proposal.TargetTable, Prefix[1]) != int.MaxValue;
        }
        public List<JoinProposal> OrderMatches(IEnumerable<JoinProposal> proposals)
        {
            var matches = proposals.Where(Matches);
            string typed = Prefix.LastOrDefault() ?? "";
            // With no name prefix, retain the contextual order (FK proposals before ordinary names).
            if (typed.Length == 0 || Mode == CompletionMode.Predicate) return matches.ToList();
            return matches.OrderBy(p => NameMatching.Score(MatchName(p), typed))
                .ThenBy(p => Math.Max(0, MatchName(p).Length - typed.Length))
                .ThenBy(p => MatchName(p), StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.IsName ? 1 : 0)
                .ThenBy(p => p.TargetSchema, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.ForeignKeyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Caption, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Caption, StringComparer.Ordinal).ToList();
        }
        string MatchName(JoinProposal proposal)
        {
            return (Mode == CompletionMode.Column || Mode == CompletionMode.Expression ? proposal.SearchName : proposal.TargetTable) ?? "";
        }
        public string Preview(JoinProposal proposal) { return proposal.CompletionText ?? (Context.PredicateOnly ? proposal.Predicate : proposal.Sql(Context.JoinKind)); }
        public string Insertion(JoinProposal proposal)
        {
            if (proposal.IsName) return proposal.CompletionText;
            return Preview(proposal) + " ";
        }
    }
}
