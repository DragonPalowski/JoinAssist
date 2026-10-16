using System;
using System.Linq;

namespace JoinAssist
{
    // Prefix matches always precede broader matches; no edit-distance guesses or usage history.
    public static class NameMatching
    {
        public static int Score(string name, string typed)
        {
            name = name ?? ""; typed = typed ?? "";
            if (typed.Length == 0) return 0;
            if (typed.Length > 128) return int.MaxValue;
            if (string.Equals(name, typed, StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) return 100000 + name.Length - typed.Length;
            if (typed.Length < 2) return int.MaxValue;
            // A local-object marker must match literally, never by fuzzy matching an ordinary name.
            if ((typed[0] == '#' || typed[0] == '@') && (name.Length == 0 || name[0] != typed[0])) return int.MaxValue;
            int start = name.IndexOf(typed, StringComparison.OrdinalIgnoreCase);
            if (start >= 0) return 200000 + start * 100 + name.Length - typed.Length;
            string compactName = Compact(name), compactTyped = Compact(typed);
            if (compactTyped.Length < 2) return int.MaxValue;
            start = compactName.IndexOf(compactTyped, StringComparison.OrdinalIgnoreCase);
            if (start >= 0) return 300000 + start * 100 + compactName.Length - compactTyped.Length;
            if (compactTyped.Length < 3) return int.MaxValue;
            int best = int.MaxValue;
            for (int i = 0; i < compactName.Length; i++)
            {
                if (char.ToUpperInvariant(compactName[i]) != char.ToUpperInvariant(compactTyped[0])) continue;
                int matched = 1, end = i + 1;
                while (end < compactName.Length && matched < compactTyped.Length)
                {
                    if (char.ToUpperInvariant(compactName[end]) == char.ToUpperInvariant(compactTyped[matched])) matched++;
                    end++;
                }
                int gaps = end - i - compactTyped.Length;
                if (matched == compactTyped.Length && gaps <= Math.Max(4, compactTyped.Length * 2))
                    best = Math.Min(best, 400000 + gaps * 100 + i);
            }
            return best;
        }
        static string Compact(string text) { return new string(text.Where(char.IsLetterOrDigit).ToArray()); }
    }
}
