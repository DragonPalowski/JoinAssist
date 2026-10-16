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
                    var proposal=new JoinProposal{Target=last.Target,TargetTable=last.TargetTable,TargetSchema=last.TargetSchema,Source=first.Source,ForeignKeyName=first.ForeignKeyName+" + "+last.ForeignKeyName,Steps=new List<JoinProposal>{first,last}};
                    proposal.DisplayText=last.Target+"  |  Percorso tramite "+first.Target+" (2 JOIN)\n"+first.Sql(context.JoinKind)+"\n"+last.Sql(context.JoinKind)+first.Warning+last.Warning;
                    paths.Add(proposal);
                    if(paths.Count>=2000)break;
                }
                if(paths.Count>=2000)break;
            }
            return paths.GroupBy(p=>p.Sql(context.JoinKind)).Select(g=>g.First()).OrderBy(p=>p.TargetTable,StringComparer.OrdinalIgnoreCase).ThenBy(p=>p.ForeignKeyName,StringComparer.Ordinal).Take(100).ToList();
        }
    }
}
