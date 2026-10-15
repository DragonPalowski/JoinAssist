using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JoinAssist
{
    public static class CompletionEngine
    {
        public static async Task<List<JoinProposal>> LoadAsync(DbConnection connection, AutomaticTrigger trigger, CancellationToken cancel)
        {
            var catalog = await CatalogReader.ReadAsync(connection, cancel).ConfigureAwait(false);
            var resolver = new ShapeResolver(catalog, cancel);
            var context = trigger.Context;
            if (trigger.Mode == CompletionMode.Table) return TableItems(catalog, trigger, null);
            if (trigger.Mode == CompletionMode.Join || trigger.Mode == CompletionMode.Predicate)
            {
                await resolver.ResolveAsync(context, true).ConfigureAwait(false);
                var keys = await MetadataReader.ReadForeignKeysAsync(connection, context, catalog, cancel).ConfigureAwait(false);
                return BuildJoinItems(catalog, trigger, keys);
            }
            await resolver.ResolveAsync(context, false).ConfigureAwait(false);
            if (trigger.Mode == CompletionMode.Column)
            {
                var matches = VisibleMatches(context, trigger.Qualifier);
                if (matches.Count > 1) return new List<JoinProposal>();
                if (matches.Count == 0 && !context.Tables.Concat(context.OuterTables).Any(t => string.Equals(t.Name, trigger.Qualifier.Last(), StringComparison.OrdinalIgnoreCase)))
                {
                    string name = trigger.Qualifier.Last();
                    var obj = catalog.Find(trigger.Qualifier.Length == 2 ? trigger.Qualifier[0] : null, name);
                    if (obj != null)
                    {
                        var use = new TableUse { Name = obj.Name, Schema = obj.Schema };
                        await resolver.ResolveUseAsync(use).ConfigureAwait(false); matches.Add(use);
                    }
                }
                return ColumnItems(matches, false);
            }
            var visible = context.Tables.Concat(context.OuterTables.Where(o => !context.Tables.Any(t => string.Equals(t.Alias ?? t.Name, o.Alias ?? o.Name, StringComparison.OrdinalIgnoreCase)))).ToList();
            var columns = ColumnItems(visible, true);
            columns.AddRange(visible.Select(t => new JoinProposal { CompletionText = t.Reference, SearchName = t.Alias ?? t.Name, Category = "Riferimento",
                DisplayText = t.Reference + "  |  tabella/alias della query" }));
            return columns;
        }
        public static List<JoinProposal> BuildJoinItems(CatalogSnapshot catalog, AutomaticTrigger trigger, IEnumerable<ForeignKey> keys)
        {
            var result = JoinBuilder.Build(trigger.Context, keys);
            if (trigger.Mode == CompletionMode.Join) result.AddRange(TableItems(catalog, trigger, result));
            return result;
        }
        static List<TableUse> VisibleMatches(QueryContext context, string[] qualifier)
        {
            var local = ShapeResolver.MatchTables(context.Tables, qualifier);
            if (local.Count > 0) return local;
            var outer = ShapeResolver.MatchTables(context.OuterTables, qualifier);
            return outer.Count == 0 ? outer : outer.Where(t => t.ScopeLevel == outer.Min(o => o.ScopeLevel)).ToList();
        }
        public static List<JoinProposal> TableItems(CatalogSnapshot catalog, AutomaticTrigger trigger, IList<JoinProposal> joins)
        {
            var result = new List<JoinProposal>();
            var ctes = trigger.Context.Ctes;
            foreach (var obj in catalog.Objects.Where(o => o.Kind == "U" || o.Kind == "V" || o.Kind == "SN"))
            {
                if (joins != null && joins.Any(p => p.TargetSchema == obj.Schema && p.TargetTable == obj.Name)) continue;
                bool schema = !obj.BareAllowed || ctes.ContainsKey(obj.Name) || trigger.Prefix.Length == 2;
                string sql = schema ? SqlName.Format(obj.Schema) + "." + SqlName.Format(obj.Name) : SqlName.Format(obj.Name);
                result.Add(new JoinProposal { TargetSchema = obj.Schema, TargetTable = obj.Name, SearchName = obj.Name, Category = obj.Caption,
                    CompletionText = trigger.Mode == CompletionMode.Join ? trigger.Context.JoinKind + " JOIN " + sql + " " : sql,
                    DisplayText = sql + "  |  " + obj.Caption + " · " + obj.Schema + (schema && !obj.BareAllowed ? " · schema necessario" : "") });
            }
            if (trigger.Prefix.Length < 2)
                foreach (var name in ctes.Keys)
                {
                    if (joins != null && joins.Any(p => p.TargetSchema == null && p.TargetTable == name)) continue;
                    result.Add(new JoinProposal { TargetTable = name, SearchName = name, Category = "CTE",
                        CompletionText = (trigger.Mode == CompletionMode.Join ? trigger.Context.JoinKind + " JOIN " : "") + SqlName.Format(name) + (trigger.Mode == CompletionMode.Join ? " " : ""),
                        DisplayText = SqlName.Format(name) + "  |  CTE della query" });
                }
            return result.OrderBy(p => p.TargetTable, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.TargetSchema).ToList();
        }
        public static List<JoinProposal> ColumnItems(IList<TableUse> sources, bool qualifyAmbiguous)
        {
            var result = new List<JoinProposal>();
            foreach (var table in sources)
                foreach (var column in (table.Columns ?? new List<ColumnShape>()).GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() == 1).Select(g => g.First()))
                {
                    bool qualify = qualifyAmbiguous && sources.Sum(t => (t.Columns ?? new List<ColumnShape>()).Count(c => string.Equals(c.Name, column.Name, StringComparison.OrdinalIgnoreCase))) > 1;
                    string name = SqlName.Format(column.Name);
                    result.Add(new JoinProposal { CompletionText = (qualify ? table.Reference + "." : "") + name, SearchName = column.Name, Category = "Colonna",
                        DisplayText = name + "  |  " + table.Reference + (string.IsNullOrEmpty(column.TypeName) ? "" : " · " + column.TypeName) });
                }
            return result.OrderBy(p => p.SearchName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.DisplayText).ToList();
        }
    }
}
