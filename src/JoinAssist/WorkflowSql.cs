using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    public sealed class SnippetField
    {
        public string Name;
        public int Start, Length;
    }

    public sealed class SnippetExpansion
    {
        public string Sql;
        public readonly List<SnippetField> Fields = new List<SnippetField>();
        public static SnippetExpansion Create(string source)
        {
            if (source == null || source.Length > 65536)
                throw new ArgumentException("Modello troppo lungo.");
            var result = new SnippetExpansion();
            var text = new StringBuilder();
            int end = 0;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(source, @"\{\{([A-Za-z][A-Za-z0-9_]{0,31})(?::([^{}\r\n]*))?\}\}"))
            {
                text.Append(source, end, match.Index - end);
                string name = match.Groups[1].Value, value;
                if (!values.TryGetValue(name, out value))
                {
                    if (values.Count >= 20)
                        throw new ArgumentException("Massimo 20 campi per modello.");
                    values[name] = value = match.Groups[2].Success ? match.Groups[2].Value : name;
                }

                if (result.Fields.Count >= 100)
                    throw new ArgumentException("Massimo 100 posizioni di campo per modello.");
                result.Fields.Add(new SnippetField { Name = name, Start = text.Length, Length = value.Length });
                text.Append(value);
                end = match.Index + match.Length;
            }

            text.Append(source, end, source.Length - end);
            result.Sql = text.ToString();
            if (result.Sql.Contains("{{"))
                throw new ArgumentException("Campo incompleto: usa {{nome:valore}} oppure {{nome}}.");
            return result;
        }
    }

    public static class WorkflowSql
    {
        public static string Generated(string sql)
        {
            if (PreferenceStore.Read.UppercaseKeywords)
                return sql;
            var words = new HashSet<string>(new[] { "SELECT", "TOP", "FROM", "INNER", "JOIN", "ON", "AS", "WHERE", "EXISTS", "AND", "OR", "NOT", "IN", "NULL", "IS", "GROUP", "BY", "HAVING", "ORDER", "DESC", "ASC" }, StringComparer.OrdinalIgnoreCase);
            foreach (var token in SqlAnalysis.Tokens(sql).Where(SqlAnalysis.Significant).Where(t => t.TokenType != TSqlTokenType.Identifier && t.TokenType != TSqlTokenType.QuotedIdentifier && words.Contains(t.Text)).OrderByDescending(t => t.Offset))
                sql = sql.Remove(token.Offset, token.Text.Length).Insert(token.Offset, token.Text.ToLowerInvariant());
            return sql;
        }

        public static string Literal(string value, string type)
        {
            if (value == null)
                return "NULL";
            type = (type ?? "nvarchar").Split('(')[0].ToLowerInvariant();
            if (new[]
            {
                "tinyint",
                "smallint",
                "int",
                "bigint",
                "decimal",
                "numeric",
                "money",
                "smallmoney",
                "float",
                "real",
                "bit"
            }.Contains(type))
            {
                decimal number;
                if (value.Length > 128 || !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out number))
                    throw new ArgumentException("Numero non valido: usa il punto decimale, senza separatori delle migliaia.");
                if (type == "bit" && number != 0 && number != 1)
                    throw new ArgumentException("Un bit richiede 0 oppure 1.");
                if (new[]
                {
                    "tinyint",
                    "smallint",
                    "int",
                    "bigint"
                }.Contains(type) && (decimal.Truncate(number) != number || type == "tinyint" && (number < 0 || number > 255) || type == "smallint" && (number < short.MinValue || number > short.MaxValue) || type == "int" && (number < int.MinValue || number > int.MaxValue) || type == "bigint" && (number < long.MinValue || number > long.MaxValue)))
                    throw new ArgumentException("Il valore non rientra nel tipo intero scelto.");
                return number.ToString(CultureInfo.InvariantCulture);
            }

            if (type == "uniqueidentifier")
            {
                Guid id;
                if (!Guid.TryParse(value, out id))
                    throw new ArgumentException("GUID non valido.");
                return "'" + id.ToString() + "'";
            }

            if (new[]
            {
                "binary",
                "varbinary",
                "timestamp",
                "rowversion",
                "image",
                "xml",
                "sql_variant",
                "geography",
                "geometry",
                "hierarchyid"
            }.Contains(type))
                throw new ArgumentException("Tipo non supportato per valori automatici: usa un parametro esplicito.");
            if (new[]
            {
                "date",
                "time",
                "datetime",
                "datetime2",
                "smalldatetime",
                "datetimeoffset"
            }.Contains(type))
            {
                DateTimeOffset date;
                if (type == "time")
                {
                    TimeSpan time;
                    if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
                        throw new ArgumentException("Ora non valida.");
                }
                else if (!Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}(?:$|[ T])") || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    throw new ArgumentException("Usa una data ISO: AAAA-MM-GG, con ora opzionale.");
            }

            return "N'" + value.Replace("'", "''") + "'";
        }

        public static string InList(string text, string type, bool distinct, bool notIn)
        {
            if (text == null || text.Length > 131072)
                throw new ArgumentException("Appunti troppo grandi (massimo 128 KiB).");
            var rows = text.Replace("\r\n", "\n").Split('\n').Where(x => x.Length > 0).ToList();
            if (rows.Count == 0 || rows.Count > 1000 || rows.Any(x => x.Contains("\t")))
                throw new ArgumentException("Copia una sola colonna, da 1 a 1.000 valori, senza intestazione.");
            // Clipboard text 'NULL' is literal text, never silently a SQL NULL.
            var values = rows.Select(x => Literal(x, type)).ToList();
            if (distinct)
                values = values.Distinct(StringComparer.Ordinal).ToList();
            return PreferenceStore.Keyword(notIn ? "NOT IN" : "IN") + " (" + string.Join(", ", values) + ")";
        }

        public static string DateFilter(string column, string period, bool utc)
        {
            if (string.IsNullOrWhiteSpace(column))
                throw new ArgumentException("Scegli una colonna data.");
            string clock = utc ? "SYSUTCDATETIME()" : "SYSDATETIME()", day = "CONVERT(date, " + clock + ")", start, end;
            switch (period)
            {
                case "Oggi":
                    start = day;
                    end = "DATEADD(day, 1, " + day + ")";
                    break;
                case "Ieri":
                    start = "DATEADD(day, -1, " + day + ")";
                    end = day;
                    break;
                case "Ultimi 7 giorni":
                    start = "DATEADD(day, -7, " + clock + ")";
                    end = clock;
                    break;
                case "Mese corrente":
                    start = "DATEFROMPARTS(YEAR(" + clock + "), MONTH(" + clock + "), 1)";
                    end = "DATEADD(month, 1, " + start + ")";
                    break;
                case "Intervallo":
                    start = "@DataDa";
                    end = "@DataAEsclusa";
                    break;
                default:
                    throw new ArgumentException("Intervallo non riconosciuto.");
            }

            return column + " >= " + start + " " + PreferenceStore.Keyword("AND") + " " + column + " < " + end;
        }

        public static string ControlQuery(CatalogObject table, IEnumerable<ColumnShape> selected, string operation)
        {
            if (table == null)
                throw new ArgumentException("Scegli una tabella o vista.");
            var columns = selected.ToList();
            if (columns.Count == 0)
                throw new ArgumentException("Scegli almeno una colonna.");
            string source = SqlName.Quote(table.Schema) + "." + SqlName.Quote(table.Name), names = string.Join(", ", columns.Select(c => SqlName.Format(c.Name)));
            if (operation == "Valori NULL")
                return "SELECT TOP 100 * FROM " + source + "\r\nWHERE " + string.Join(" OR ", columns.Select(c => SqlName.Format(c.Name) + " IS NULL")) + ";";
            if (operation == "Per giorno")
            {
                if (columns.Count != 1 || !DateType(columns[0]))
                    throw new ArgumentException("Scegli una sola colonna data per il conteggio giornaliero.");
                return "SELECT CONVERT(date, " + names + ") AS Giorno, COUNT_BIG(*) AS Totale\r\nFROM " + source + "\r\nGROUP BY CONVERT(date, " + names + ")\r\nORDER BY Giorno DESC;";
            }

            if (operation != "Duplicati" && operation != "Conta per valore")
                throw new ArgumentException("Controllo non riconosciuto.");
            if (columns.Any(c => new[] { "text", "ntext", "image", "xml", "geography", "geometry" }.Contains((c.TypeName ?? "").ToLowerInvariant())))
                throw new ArgumentException("Una colonna selezionata non supporta il raggruppamento automatico.");
            return "SELECT " + names + ", COUNT_BIG(*) AS Totale\r\nFROM " + source + "\r\nGROUP BY " + names + (operation == "Duplicati" ? "\r\nHAVING COUNT_BIG(*) > 1" : "") + "\r\nORDER BY Totale DESC;";
        }

        public static bool DateType(ColumnShape column)
        {
            return new[]
            {
                "date",
                "datetime",
                "datetime2",
                "smalldatetime",
                "datetimeoffset"
            }.Contains((column.TypeName ?? "").Split('(')[0].ToLowerInvariant());
        }

        public static string RenameAlias(string sql, int caret, string oldName, string newName)
        {
            SqlAnalysis.CheckCaret(sql, caret);
            IList<ParseError> errors;
            var tree = new TSql160Parser(true).Parse(new StringReader(sql), out errors);
            if (errors.Count > 0)
                throw new ArgumentException("Completa la query prima di rinominare un alias.");
            var names = SqlAnalysis.Tokens(newName).Where(SqlAnalysis.Significant).ToList();
            if (names.Count != 1 || names[0].TokenType != TSqlTokenType.Identifier || names[0].Text != newName)
                throw new ArgumentException("Usa un alias semplice, diverso da parole riservate SQL.");
            var context = SqlAnalysis.ReadScope(sql, caret);
            int position = Math.Min(caret, Math.Max(0, sql.Length - 1));
            if (!SqlAnalysis.ParseIndex(sql).Scopes.Any(s => s.Query.StartOffset <= position && position < s.Query.StartOffset + s.Query.FragmentLength))
                throw new ArgumentException("Posiziona il cursore nell'ambito SELECT dell'alias; la rinomina di target UPDATE/DELETE non è supportata.");
            var matches = context.Tables.Where(t => string.Equals(t.Alias, oldName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1)
                throw new ArgumentException("L'alias deve identificare una sola sorgente nell'ambito corrente.");
            var scan = new AliasScan();
            tree.Accept(scan);
            var target = scan.Tables.SingleOrDefault(t => t.StartOffset == matches[0].Offset && t.Alias != null);
            if (target == null)
                throw new ArgumentException("Rinomina disponibile per alias di tabelle, viste e CTE nominate.");
            if (scan.Tables.Any(t => (t.Alias?.Value ?? t.SchemaObject.BaseIdentifier.Value).Equals(newName, StringComparison.OrdinalIgnoreCase)) || SqlAnalysis.ParseIndex(sql).Scopes.Any(s => SqlAnalysis.ReadScope(sql, s.Query.StartOffset).Tables.Any(t => (t.Alias ?? t.Name).Equals(newName, StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException("Il nuovo alias è già usato nello script: scegli un nome distinto.");
            var edits = new List<Identifier>
            {
                target.Alias
            };
            foreach (var chain in scan.Chains.Where(c => c.Identifiers.Count >= 1 && c.Identifiers[0].Value.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
            {
                var own = SqlAnalysis.ReadScope(sql, chain.StartOffset);
                var visible = own.Tables.Where(t => string.Equals(t.Alias, oldName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (visible.Count == 0)
                    visible = own.OuterTables.Where(t => string.Equals(t.Alias, oldName, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.ScopeLevel).Take(1).ToList();
                if (visible.Count == 1 && visible[0].Offset == target.StartOffset)
                    edits.Add(chain.Identifiers[0]);
            }

            foreach (var id in edits.GroupBy(i => i.StartOffset).Select(g => g.First()).OrderByDescending(i => i.StartOffset))
                sql = sql.Remove(id.StartOffset, id.FragmentLength).Insert(id.StartOffset, newName);
            new TSql160Parser(true).Parse(new StringReader(sql), out errors);
            if (errors.Count > 0)
                throw new InvalidOperationException("Rinomina non valida: nessuna modifica applicata.");
            return sql;
        }

        sealed class AliasScan : TSqlFragmentVisitor
        {
            public readonly List<NamedTableReference> Tables = new List<NamedTableReference>();
            public readonly List<MultiPartIdentifier> Chains = new List<MultiPartIdentifier>();
            public override void ExplicitVisit(NamedTableReference node)
            {
                Tables.Add(node);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(ColumnReferenceExpression node)
            {
                if (node.MultiPartIdentifier != null && node.MultiPartIdentifier.Identifiers.Count >= 2)
                    Chains.Add(node.MultiPartIdentifier);
                base.ExplicitVisit(node);
            }

            public override void ExplicitVisit(SelectStarExpression node)
            {
                if (node.Qualifier != null)
                    Chains.Add(node.Qualifier);
                base.ExplicitVisit(node);
            }
        }
    }
}
