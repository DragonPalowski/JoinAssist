using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    internal static class CompletionRanking
    {
        internal static void Comparison(QueryContext context,IList<TSqlParserToken> before)
        {
            if(before.Count<2 || before.Last().TokenType!=TSqlTokenType.EqualsSign)return;
            int i=before.Count-2;
            if(before[i].TokenType!=TSqlTokenType.Identifier && before[i].TokenType!=TSqlTokenType.QuotedIdentifier)return;
            context.ComparisonColumn=SqlName.Decode(before[i].Text);
            if(i>=2 && before[i-1].TokenType==TSqlTokenType.Dot)context.ComparisonQualifier=SqlName.Decode(before[i-2].Text);
        }
        internal static void Apply(AutomaticTrigger trigger,List<JoinProposal> items)
        {
            var context=trigger.Context;
            var all=context.Tables.Concat(context.OuterTables).ToList();
            var counterparts=all.Where(t=>context.ComparisonQualifier==null || string.Equals(t.Alias??t.Name,context.ComparisonQualifier,StringComparison.OrdinalIgnoreCase))
                .SelectMany(t=>t.Columns??new List<ColumnShape>()).Where(c=>string.Equals(c.Name,context.ComparisonColumn,StringComparison.OrdinalIgnoreCase)).ToList();
            var other=counterparts.Count==1?counterparts[0]:null;
            foreach(var item in items.Where(p=>p.Column!=null))
            {
                // Scope beats key hints; name matching remains the primary ordering criterion.
                if(context.Clause=="ON")
                {
                    if(context.FocusedReferences.Count>0 && !context.FocusedReferences.Contains(item.SourceReference))item.ContextRank+=40;
                    if(item.Column.PrimaryKey!=true && item.Column.ForeignKey!=true)item.ContextRank+=5;
                }
                if(other!=null && !Compatible(other,item.Column))item.ContextRank+=10;
            }
        }
        static bool Compatible(ColumnShape a,ColumnShape b)
        {
            string left=(a.TypeName??"").Split('(')[0],right=(b.TypeName??"").Split('(')[0];
            if(left.Length==0 || right.Length==0)return false;
            if(string.Equals(left,right,StringComparison.OrdinalIgnoreCase))return true;
            string[] integers={"tinyint","smallint","int","bigint"};
            return integers.Contains(left,StringComparer.OrdinalIgnoreCase) && integers.Contains(right,StringComparer.OrdinalIgnoreCase);
        }
    }
}
