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
        public static async Task<List<JoinProposal>> LoadAsync(DbConnection connection, AutomaticTrigger trigger, CancellationToken cancel, MetadataScope scope=null)
        {
            if(trigger.Mode==CompletionMode.Keyword)return TypingTemplates.KeywordItems(trigger);
            var catalog = await CatalogReader.ReadAsync(connection, cancel,scope).ConfigureAwait(false);
            var resolver = new ShapeResolver(catalog, cancel);
            var context = trigger.Context;
            if(trigger.Mode==CompletionMode.FilterTemplate)
            {
                await resolver.ResolveAsync(context,false).ConfigureAwait(false);
                return TypingTemplates.FilterItems(trigger);
            }
            if (trigger.Mode == CompletionMode.Table) return TableItems(catalog, trigger, null);
            if(trigger.Mode==CompletionMode.InsertTemplate)
            {
                await resolver.ResolveAsync(context,false).ConfigureAwait(false);
                return InsertTemplates.Items(trigger,catalog);
            }
            if(trigger.Mode==CompletionMode.InsertColumn || trigger.Mode==CompletionMode.UpdateColumn)
            {
                await resolver.ResolveAsync(context,false).ConfigureAwait(false);
                var templates=trigger.Mode==CompletionMode.InsertColumn?InsertTemplates.Items(trigger,catalog):new List<JoinProposal>();
                bool known=context.Tables.Any(t=>t.Columns!=null && t.Columns.Count>0);
                // A target with unknown writeability stays conservative about the names it exposes.
                foreach(var use in context.Tables)
                    use.Columns=(use.Columns??new List<ColumnShape>()).Where(c=>!context.AssignedColumns.Contains(c.Name) && c.Identity!=true && c.Computed!=true &&
                        c.Generated!=true && !string.Equals(c.TypeName,"timestamp",StringComparison.OrdinalIgnoreCase) && !string.Equals(c.TypeName,"rowversion",StringComparison.OrdinalIgnoreCase)).ToList();
                var items=ColumnItems(context.Tables,false);
                items.InsertRange(0,templates);
                if(items.Count==0 && known)Diagnostics.Set(context,"JA-DML-01","Nessuna colonna scrivibile non ancora indicata: identity, calcolate e rowversion sono escluse.");
                else Diagnostics.Explain(trigger,context.Tables,items);
                return items;
            }
            if (trigger.Mode == CompletionMode.Join || trigger.Mode == CompletionMode.Predicate || trigger.Mode==CompletionMode.PartialPredicate)
            {
                await resolver.ResolveAsync(context, true).ConfigureAwait(false);
                var keys = await MetadataReader.ReadForPathsAsync(connection, context, catalog, cancel,scope).ConfigureAwait(false);
                return BuildJoinItems(catalog, trigger, keys);
            }
            if (trigger.Mode == CompletionMode.Column)
            {
                // Bind names cheaply first, then load only the qualified source's columns/lineage.
                foreach(var use in context.Tables.Concat(context.OuterTables).Where(t=>!t.IsLocal && t.Definition==null))
                { var obj=catalog.Find(use.Schema,use.Name);if(obj!=null)use.ResolvedSchema=obj.Schema; }
                var matches = VisibleMatches(context, trigger.Qualifier);
                if (matches.Count > 1) { Diagnostics.Set(context,"JA-SCOPE-02","Riferimento ambiguo: usa uno schema o un alias distinto.");return new List<JoinProposal>(); }
                var focused=new QueryContext{Locals=context.Locals};focused.Tables.AddRange(matches);
                if(context.ComparisonColumn!=null && context.ComparisonQualifier!=null)
                    focused.Tables.AddRange(VisibleMatches(context,new[]{context.ComparisonQualifier}).Where(t=>!focused.Tables.Contains(t)));
                await resolver.ResolveAsync(focused,false).ConfigureAwait(false);
                if (matches.Count == 0 && !context.Tables.Concat(context.OuterTables).Any(t => string.Equals(t.Name, trigger.Qualifier.Last(), StringComparison.OrdinalIgnoreCase)))
                {
                    string name = trigger.Qualifier.Last();
                    if (trigger.Qualifier.Length == 1 && context.Locals.ContainsKey(name))
                    {
                        var use = new TableUse { Name = name, IsLocal = true, IsVirtual = true };
                        await resolver.ResolveUseAsync(use).ConfigureAwait(false); matches.Add(use);
                    }
                    var obj = catalog.Find(trigger.Qualifier.Length == 2 ? trigger.Qualifier[0] : null, name);
                    if (obj != null && matches.Count == 0 && !LocalTables.IsLocal(name))
                    {
                        var use = new TableUse { Name = obj.Name, Schema = obj.Schema };
                        await resolver.ResolveUseAsync(use).ConfigureAwait(false); matches.Add(use);
                    }
                }
                var items=ColumnItems(matches, false);CompletionRanking.Apply(trigger,items);Diagnostics.Explain(trigger,matches,items);return items;
            }
            await resolver.ResolveAsync(context, false).ConfigureAwait(false);
            var visible = context.Tables.Concat(context.OuterTables.Where(o => !context.Tables.Any(t => string.Equals(t.Alias ?? t.Name, o.Alias ?? o.Name, StringComparison.OrdinalIgnoreCase)))).ToList();
            var columns = ColumnItems(visible, true);
            CompletionRanking.Apply(trigger,columns);
            Diagnostics.Explain(trigger,visible,columns);
            columns.AddRange(visible.Select(t => new JoinProposal { CompletionText = t.Reference, SearchName = t.Alias ?? t.Name, AlternateSearchName=t.Name,Category = "Riferimento",
                ContextRank=t.ScopeLevel*100,SearchIdentity="reference\0"+(t.ResolvedSchema??t.Schema)+"\0"+t.Name+"\0"+t.Reference,
                DisplayText = t.Reference + "  |  " + (string.IsNullOrEmpty(t.ResolvedSchema??t.Schema)?"":(t.ResolvedSchema??t.Schema)+".") + t.Name + " · tabella/alias della query" }));
            columns.AddRange(TypingTemplates.KeywordItems(trigger));
            return columns;
        }
        public static List<JoinProposal> BuildJoinItems(CatalogSnapshot catalog, AutomaticTrigger trigger, IEnumerable<ForeignKey> keys)
        {
            var result=JoinItems(trigger.Context,keys,catalog);
            if(trigger.Mode==CompletionMode.PartialPredicate && trigger.NameFallback!=null)
            {
                var fallback=trigger.NameFallback;
                var sources=fallback.Qualifier.Length>0?VisibleMatches(trigger.Context,fallback.Qualifier):trigger.Context.Tables;
                var columns=ColumnItems(sources,fallback.Qualifier.Length==0);
                foreach(var item in columns){item.CompletionText=trigger.FallbackHead+item.CompletionText;item.ContextRank+=1000;}
                result.AddRange(columns);
            }
            if(result.Count==0)Diagnostics.Set(trigger.Context,"JA-FK-01","Nessuna foreign key visibile per le sorgenti: sono disponibili solo i nomi delle tabelle.");
            if (trigger.Mode == CompletionMode.Join) result.AddRange(TableItems(catalog, trigger, result));
            return result;
        }
        public static List<JoinProposal> JoinItems(QueryContext context,IEnumerable<ForeignKey> keys,CatalogSnapshot catalog)
        {
            var all=keys.ToList();var result=JoinBuilder.Build(context,all);
            if(PreferenceStore.Current.JoinPaths)
            {
                var direct=new HashSet<string>(result.Select(p=>p.Sql(context.JoinKind)),StringComparer.Ordinal);
                result.AddRange(JoinPaths.SharedKeys(context,all).Where(p=>!direct.Contains(p.Sql(context.JoinKind))));
                result.AddRange(JoinPaths.Build(context,all,catalog));
            }
            return result.OrderBy(p=>p.ContextRank).ToList();
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
                bool schema = PreferenceStore.Current.AlwaysSchema || !obj.BareAllowed || ctes.ContainsKey(obj.Name) || trigger.Prefix.Length == 2;
                string sql = schema ? SqlName.Format(obj.Schema) + "." + SqlName.Format(obj.Name) : SqlName.Format(obj.Name);
                result.Add(new JoinProposal { TargetSchema = obj.Schema, TargetTable = obj.Name, SearchName = obj.Name, Category = obj.Caption,
                    ContextRank=trigger.Mode==CompletionMode.Join?30:trigger.Context.Tables.Any(t=>string.Equals(t.Name,obj.Name,StringComparison.OrdinalIgnoreCase) && (t.Schema==null||t.Schema==obj.Schema))?-5:0,
                    CompletionText = trigger.Mode == CompletionMode.Join ? PreferenceStore.Keyword(trigger.Context.JoinKind + " JOIN") + " " + sql + " " : sql,
                    DisplayText = sql + "  |  " + obj.Caption + " · " + obj.Schema + (schema && !obj.BareAllowed ? " · schema necessario" : "") });
            }
            if (trigger.Prefix.Length < 2)
            {
                foreach (var local in trigger.Context.Locals.Values)
                    result.Add(new JoinProposal { TargetTable = local.Name, SearchName = local.Name,
                        ContextRank=trigger.Mode==CompletionMode.Join?30:-10,
                        Category = local.Name.StartsWith("@", StringComparison.Ordinal) ? "Variabile tabellare" : "Tabella temporanea",
                        CompletionText = (trigger.Mode == CompletionMode.Join ? PreferenceStore.Keyword(trigger.Context.JoinKind + " JOIN") + " " : "") + SqlName.Format(local.Name) + (trigger.Mode == CompletionMode.Join ? " " : ""),
                        DisplayText = local.Name + "  |  " + (local.Name.StartsWith("@", StringComparison.Ordinal) ? "Variabile tabellare" : "Tabella temporanea") + " · definita nello script" });
                foreach (var name in ctes.Keys)
                {
                    if (joins != null && joins.Any(p => p.TargetSchema == null && p.TargetTable == name)) continue;
                    result.Add(new JoinProposal { TargetTable = name, SearchName = name, Category = "CTE",ContextRank=trigger.Mode==CompletionMode.Join?30:-10,
                        CompletionText = (trigger.Mode == CompletionMode.Join ? PreferenceStore.Keyword(trigger.Context.JoinKind + " JOIN") + " " : "") + SqlName.Format(name) + (trigger.Mode == CompletionMode.Join ? " " : ""),
                        DisplayText = SqlName.Format(name) + "  |  CTE della query" });
                }
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
                        SourceReference=table.Reference,Column=column,ContextRank=table.ScopeLevel*100,
                        SearchIdentity="column\0"+(table.ResolvedSchema??table.Schema)+"\0"+table.Name+"\0"+table.Reference+"\0"+column.Name,
                        DisplayText = name + "  |  " + table.Reference + (table.Alias==null?"":" ("+(string.IsNullOrEmpty(table.ResolvedSchema??table.Schema)?"":(table.ResolvedSchema??table.Schema)+".")+table.Name+")") + " · " + column.Description });
                }
            return result.OrderBy(p => p.SearchName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.DisplayText).ToList();
        }
    }
}
