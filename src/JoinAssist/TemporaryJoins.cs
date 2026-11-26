using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JoinAssist
{
    // Optional, explicitly inferred equalities. These never populate TableUse.Maps or
    // the constraint flags of a SELECT INTO: temporary data has no inherited FK.
    internal static class TemporaryJoins
    {
        internal static bool Enabled(AutomaticTrigger trigger)
        {return PreferenceStore.Read.TemporaryJoins && (trigger.Mode==CompletionMode.Join || trigger.Mode==CompletionMode.Predicate || trigger.Mode==CompletionMode.PartialPredicate);}
        internal static bool NeedsMetadata(AutomaticTrigger trigger)
        {
            if(!Enabled(trigger))return false;
            var locals=trigger.Context.Locals;
            var targets=locals.Values.Where(l=>trigger.Matches(new JoinProposal{TargetTable=l.Name,CompletionText=l.Name})).Take(20).ToList();
            return targets.Count>0 && (targets.Any(l=>l.Query!=null) || trigger.Context.Tables.Any(t=>!LocalTables.IsLocal(t.Name) || !locals.ContainsKey(t.Name) || locals[t.Name].Query!=null));
        }
        internal static async Task<List<TableUse>> TargetsAsync(AutomaticTrigger trigger,CatalogSnapshot catalog,CancellationToken cancel)
        {
            var result=new List<TableUse>();if(!Enabled(trigger))return result;
            if(trigger.Context.PredicateOnly){if(trigger.Context.LastJoinTarget!=null)result.Add(trigger.Context.LastJoinTarget);return result;}
            foreach(var local in trigger.Context.Locals.Values.Where(l=>trigger.Matches(new JoinProposal{TargetTable=l.Name,CompletionText=l.Name})).OrderBy(l=>NameMatching.Score(l.Name,trigger.Prefix.LastOrDefault()??"")).Take(20))
                result.Add(new TableUse{Name=local.Name,IsLocal=true});
            if(trigger.Context.Tables.Any(t=>t.IsLocal))
                foreach(var obj in catalog.Objects.Where(o=>o.Kind=="U" && trigger.Matches(new JoinProposal{TargetSchema=o.Schema,TargetTable=o.Name,CompletionText=o.Sql})).OrderBy(o=>NameMatching.Score(o.Name,trigger.Prefix.LastOrDefault()??"")).Take(20))
                    result.Add(new TableUse{Name=obj.Name,Schema=obj.Schema});
            var context=new QueryContext{Locals=trigger.Context.Locals};context.Tables.AddRange(result);
            if(result.Count>0)await new ShapeResolver(catalog,cancel).ResolveAsync(context,false).ConfigureAwait(false);
            return result;
        }
        internal static QueryContext MetadataContext(QueryContext context,IEnumerable<TableUse> targets)
        {
            var result=new QueryContext{PredicateOnly=context.PredicateOnly};result.Tables.AddRange(context.Tables);result.CteTargets.AddRange(context.CteTargets);
            foreach(int id in context.Tables.Concat(targets).SelectMany(t=>t.Columns??new List<ColumnShape>()).Select(c=>c.SnapshotObjectId).Where(id=>id!=0).Distinct())
                result.Tables.Add(new TableUse{Name="origin"+id,ObjectId=id});
            return result;
        }
        internal static List<JoinProposal> Build(AutomaticTrigger trigger,CatalogSnapshot catalog,IEnumerable<TableUse> targets,IEnumerable<ForeignKey> keys,CancellationToken cancel)
        {
            var result=new List<JoinProposal>();if(!Enabled(trigger))return result;
            var context=trigger.Context;var relationships=keys.Where(k=>!k.Disabled && k.Columns.Count>0).ToList();
            foreach(var source in context.Tables.Where(t=>!context.PredicateOnly || !ReferenceEquals(t,context.LastJoinTarget)).Take(50))
            foreach(var target in targets)
            {
                cancel.ThrowIfCancellationRequested();
                if(!source.IsLocal && !target.IsLocal || ReferenceEquals(source,target))continue;
                var left=Unique(source);var right=Unique(target);if(left.Count==0 || right.Count==0)continue;
                string alias=context.PredicateOnly?target.Alias:JoinBuilder.NecessaryAlias(context,target.Name,target.IsLocal?0:target.ObjectId);
                string sql=target.IsLocal?SqlName.Format(target.Name):PreferenceStore.Read.AlwaysSchema || !target.BareAllowed || context.Ctes.ContainsKey(target.Name)?SqlName.Format(target.ResolvedSchema??target.Schema)+"."+SqlName.Format(target.Name):SqlName.Format(target.Name);
                string reference=context.PredicateOnly?target.Reference:alias==null?sql:SqlName.Format(alias);
                Action<IEnumerable<Tuple<ColumnShape,ColumnShape>>,string,int> add=(columns,basis,rank)=>{
                    if(result.Count>=1000)return;
                    var pairs=columns.ToList();if(pairs.Count==0 || pairs.Any(p=>p.Item1==null || p.Item2==null || !Compatible(p.Item1,p.Item2)))return;
                    result.Add(new JoinProposal{Target=sql,TargetTable=target.Name,TargetSchema=target.IsLocal?null:target.ResolvedSchema??target.Schema,Alias=alias,Source=source.Reference,
                        Predicate=string.Join(" "+PreferenceStore.Keyword("AND")+" ",pairs.Select(p=>source.Reference+"."+SqlName.Format(p.Item1.Name)+" = "+reference+"."+SqlName.Format(p.Item2.Name))),
                        TemporaryJoinBasis=basis,ForeignKeyName=basis,Warning=" [collegamento ricavato: verificare righe e cardinalità]",ContextRank=rank,
                        ScriptOnly=source.IsLocal && target.IsLocal && Declared(context,source.Name) && Declared(context,target.Name)});
                };
                var origins=new HashSet<int>(left.Values.Concat(right.Values).Select(c=>c.BaseObjectId!=0?c.BaseObjectId:c.SnapshotObjectId));
                foreach(var obj in catalog.Objects.Where(o=>o.Kind=="U" && o.Loaded && origins.Contains(o.Id)))
                {
                    var pk=obj.Columns.Where(c=>c.PrimaryKey==true).ToList();if(pk.Count==0)continue;
                    var pairs=pk.Select(c=>Tuple.Create(Origin(left,obj.Id,c.Name),Origin(right,obj.Id,c.Name))).ToList();
                    if(pairs.All(p=>p.Item1!=null && p.Item2!=null))add(pairs,"PK origine "+SqlName.Format(obj.Schema)+"."+SqlName.Format(obj.Name),4);
                }
                foreach(var key in relationships)
                {
                    cancel.ThrowIfCancellationRequested();
                    var ordered=key.Columns.OrderBy(c=>c.Ordinal).ToList();
                    add(ordered.Select(c=>Tuple.Create(Origin(left,key.ChildId,c.Child),Origin(right,key.ParentId,c.Parent))),"FK di origine "+key.Name+(key.Untrusted?" (non verificata)":""),6);
                    add(ordered.Select(c=>Tuple.Create(Origin(left,key.ParentId,c.Parent),Origin(right,key.ChildId,c.Child))),"FK di origine "+key.Name+(key.Untrusted?" (non verificata)":""),6);
                }
                var leftKey=(source.Columns??new List<ColumnShape>()).Where(c=>c.PrimaryKey==true).ToList();
                if(leftKey.Count>0 && leftKey.All(c=>left.ContainsKey(c.Name)))add(leftKey.Select(c=>Tuple.Create(c,Named(right,c.Name))),"chiave completa sorgente · nomi e tipi",8);
                var rightKey=(target.Columns??new List<ColumnShape>()).Where(c=>c.PrimaryKey==true).ToList();
                if(rightKey.Count>0 && rightKey.All(c=>right.ContainsKey(c.Name)))add(rightKey.Select(c=>Tuple.Create(Named(left,c.Name),c)),"chiave completa destinazione · nomi e tipi",8);
                var common=left.Values.Where(c=>right.ContainsKey(c.Name));
                if(trigger.Mode==CompletionMode.PartialPredicate && !string.IsNullOrEmpty(trigger.PredicatePrefix))
                    common=common.Where(c=>{string predicate=source.Reference+"."+SqlName.Format(c.Name)+" = "+reference+"."+SqlName.Format(c.Name);return PredicateCompletion.Matches(trigger.PredicatePrefix,predicate) || PredicateCompletion.Matches(trigger.PredicatePrefix,PredicateCompletion.Reverse(predicate));});
                foreach(var column in common.OrderBy(c=>c.Name,StringComparer.OrdinalIgnoreCase).Take(32))
                    add(new[]{Tuple.Create(column,right[column.Name])},"nomi e tipi compatibili",20);
                if(result.Count>=1000)return Ordered(result,context.JoinKind);
            }
            return Ordered(result,context.JoinKind);
        }
        static List<JoinProposal> Ordered(IEnumerable<JoinProposal> result,string kind)
        {return result.OrderBy(p=>p.ContextRank).GroupBy(p=>p.Sql(kind),StringComparer.Ordinal).Select(g=>g.First()).Take(200).ToList();}
        static bool Declared(QueryContext context,string name)
        {LocalTable local;return context.Locals.TryGetValue(name,out local) && local.Columns!=null;}
        static Dictionary<string,ColumnShape> Unique(TableUse use)
        {return (use.Columns??new List<ColumnShape>()).GroupBy(c=>c.Name,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.Single(),StringComparer.OrdinalIgnoreCase);}
        static ColumnShape Named(Dictionary<string,ColumnShape> columns,string name)
        {ColumnShape value;return columns.TryGetValue(name,out value)?value:null;}
        static ColumnShape Origin(Dictionary<string,ColumnShape> columns,int id,string name)
        {
            var matches=columns.Values.Where(c=>(c.BaseObjectId==id && string.Equals(c.BaseColumn,name,StringComparison.OrdinalIgnoreCase)) || (c.SnapshotObjectId==id && string.Equals(c.SnapshotColumn,name,StringComparison.OrdinalIgnoreCase))).Take(2).ToList();
            return matches.Count==1?matches[0]:null;
        }
        internal static bool Compatible(ColumnShape left,ColumnShape right)
        {string a=Family(left.TypeName),b=Family(right.TypeName);return a!=null && a==b;}
        static string Family(string type)
        {
            string name=(type??"").Split('(')[0].Trim().ToLowerInvariant();
            if(new[]{"tinyint","smallint","int","bigint","decimal","numeric","money","smallmoney","real","float"}.Contains(name))return "number";
            if(new[]{"char","varchar","nchar","nvarchar"}.Contains(name))return "text";
            if(new[]{"binary","varbinary","timestamp","rowversion"}.Contains(name))return "binary";
            if(new[]{"date","datetime","datetime2","smalldatetime","datetimeoffset"}.Contains(name))return "date";
            return new[]{"bit","time","uniqueidentifier"}.Contains(name)?name:null;
        }
    }
}
