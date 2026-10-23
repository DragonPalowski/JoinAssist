using System;
using System.Collections.Generic;
using System.Linq;

namespace JoinAssist
{
    public static class JoinPaths
    {
        public static List<JoinProposal> Build(QueryContext context,IEnumerable<ForeignKey> relationships,CatalogSnapshot catalog)
        {
            if(context.PredicateOnly)return new List<JoinProposal>();
            var keys=relationships.Where(k=>!k.Disabled).ToList();
            var paths=new List<JoinProposal>();
            var usedIds=new HashSet<int>(context.Tables.SelectMany(t=>t.Maps).Select(m=>m.ObjectId));
            foreach(var first in JoinBuilder.Build(context,keys).Where(p=>p.TargetSchema!=null).Take(200))
            {
                var intermediate=catalog.Find(first.TargetSchema,first.TargetTable);
                if(intermediate==null || usedIds.Contains(intermediate.Id))continue;
                var use=new TableUse{Name=intermediate.Name,Schema=intermediate.Schema,ResolvedSchema=intermediate.Schema,Alias=first.Alias,ObjectId=intermediate.Id,BareAllowed=intermediate.BareAllowed};
                use.NeedsQualifiedReference=first.Alias==null && first.Target!=SqlName.Format(intermediate.Name);
                var extended=new QueryContext{Ctes=context.Ctes};extended.Tables.AddRange(context.Tables);extended.Tables.Add(use);
                foreach(var last in JoinBuilder.Build(extended,keys).Where(p=>p.TargetSchema!=null && p.Source==use.Reference).Take(200))
                {
                    var destination=catalog.Find(last.TargetSchema,last.TargetTable);
                    if(destination==null || destination.Id==intermediate.Id || usedIds.Contains(destination.Id))continue;
                    var proposal=new JoinProposal{Target=last.Target,TargetTable=last.TargetTable,TargetSchema=last.TargetSchema,Source=first.Source,ForeignKeyName=first.ForeignKeyName+" + "+last.ForeignKeyName,Steps=new List<JoinProposal>{first,last},ContextRank=20};
                    proposal.DisplayText=last.Target+"  |  Percorso tramite "+first.Target+" (2 JOIN)\n"+first.Sql(context.JoinKind)+"\n"+last.Sql(context.JoinKind)+first.Warning+last.Warning;
                    paths.Add(proposal);
                    if(paths.Count>=2000)break;
                }
                if(paths.Count>=2000)break;
            }
            return paths.GroupBy(p=>p.Sql(context.JoinKind)).Select(g=>g.First()).OrderBy(p=>p.TargetTable,StringComparer.OrdinalIgnoreCase).ThenBy(p=>p.ForeignKeyName,StringComparer.Ordinal).Take(100).ToList();
        }
        public static List<JoinProposal> SharedKeys(QueryContext context,IEnumerable<ForeignKey> relationships)
        {
            // This is a candidate equality between child keys, NOT a foreign key between
            // the two children or a claim that removing a parent JOIN preserves results.
            if(context.PredicateOnly)return new List<JoinProposal>();
            var used=new HashSet<int>(context.Tables.SelectMany(t=>t.Maps).Select(m=>m.ObjectId));
            var keys=relationships.Where(k=>!k.Disabled && CompleteKey(k)).OrderBy(k=>k.Untrusted).ThenBy(k=>k.Name,StringComparer.Ordinal).ToList();
            var parents=keys.ToLookup(k=>k.ParentId);
            var result=new List<JoinProposal>();
            foreach(var first in keys.Where(k=>used.Contains(k.ChildId)).Take(200))
                foreach(var last in parents[first.ParentId].Where(k=>k.ChildId!=first.ChildId && !used.Contains(k.ChildId)).Take(200))
                {
                    // Match the FULL referenced key by column identity, not by ordinal,
                    // child column spelling, or a partial intersection of composite keys.
                    if(first.Columns.Count!=last.Columns.Count || !new HashSet<string>(first.Columns.Select(c=>c.Parent),StringComparer.Ordinal).SetEquals(last.Columns.Select(c=>c.Parent)))continue;
                    var relation=new ForeignKey{ChildId=first.ChildId,ParentId=last.ChildId,
                        ChildSchema=first.ChildSchema,ChildTable=first.ChildTable,ChildBare=first.ChildBare,
                        ParentSchema=last.ChildSchema,ParentTable=last.ChildTable,ParentBare=last.ChildBare,
                        Name=first.Name+" + "+last.Name};
                    foreach(var pair in first.Columns.OrderBy(c=>c.Ordinal))
                        relation.Columns.Add(new KeyColumn{Ordinal=pair.Ordinal,Child=pair.Child,Parent=last.Columns.Single(c=>c.Parent==pair.Parent).Child});
                    string parent=SqlName.Format(first.ParentSchema)+"."+SqlName.Format(first.ParentTable);
                    string parentCore=first.ParentTable;
                    foreach(string suffix in new[]{"_tb","_vw"})if(parentCore.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)){parentCore=parentCore.Substring(0,parentCore.Length-suffix.Length);break;}
                    // Name affinity only orders real shared-key candidates. It never
                    // creates a relationship or overrides an actual direct FK.
                    int rank=NameMatching.Match(last.ChildTable,parentCore,SearchTolerance.Precise).Band<=1?10:11;
                    foreach(var proposal in JoinBuilder.Build(context,new[]{relation}))
                    {
                        proposal.SharedKeyParent=parent;proposal.ContextRank=rank;
                        proposal.SearchIdentity="shared-key\0"+relation.Name+"\0"+proposal.Source+"\0"+proposal.Target+"\0"+proposal.Alias+"\0"+proposal.Predicate;
                        proposal.Warning+=" [collegamento ricavato, non FK diretta]"+
                            (first.Untrusted || last.Untrusted?" [FK di supporto non verificate]":"");
                        proposal.DisplayText=proposal.Target+"  |  1 JOIN · FK comuni verso "+parent+"\nON "+proposal.Predicate+"\n"+relation.Name+proposal.Warning;
                        result.Add(proposal);
                        if(result.Count>=2000)break;
                    }
                    if(result.Count>=2000)goto Finished;
                }
            Finished:
            return result.GroupBy(p=>p.Sql(context.JoinKind),StringComparer.Ordinal).Select(g=>g.First())
                .OrderBy(p=>p.ContextRank).ThenBy(p=>p.TargetTable,StringComparer.OrdinalIgnoreCase).ThenBy(p=>p.ForeignKeyName,StringComparer.Ordinal).Take(200).ToList();
        }
        static bool CompleteKey(ForeignKey key)
        {
            return key.ChildId!=0 && key.ParentId!=0 && !string.IsNullOrEmpty(key.ChildSchema) && !string.IsNullOrEmpty(key.ChildTable) &&
                !string.IsNullOrEmpty(key.ParentSchema) && !string.IsNullOrEmpty(key.ParentTable) &&
                key.Columns.Count>0 && key.Columns.All(c=>!string.IsNullOrEmpty(c.Child) && !string.IsNullOrEmpty(c.Parent)) &&
                key.Columns.Select(c=>c.Parent).Distinct(StringComparer.Ordinal).Count()==key.Columns.Count &&
                key.Columns.Select(c=>c.Child).Distinct(StringComparer.Ordinal).Count()==key.Columns.Count;
        }
    }
}
