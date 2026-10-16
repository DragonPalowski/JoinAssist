using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    // Only examines tokens. No INSERT/UPDATE statement is ever sent to the server.
    internal static class DmlCompletion
    {
        internal static AutomaticTrigger TryCreate(string sql, int caret)
        {
            var all = SqlAnalysis.Tokens(sql).Where(SqlAnalysis.Significant).ToList();
            var depths = new Dictionary<int,int>(); int depth=0;
            foreach(var t in all) { depths[t.Offset]=depth; if(t.TokenType==TSqlTokenType.LeftParenthesis)depth++;if(t.TokenType==TSqlTokenType.RightParenthesis)depth--; }
            var head=all.LastOrDefault(t=>t.Offset<caret && depths[t.Offset]==0 && new[]{"INSERT","UPDATE","DELETE","MERGE","SELECT","GO",";"}.Contains(t.Text,StringComparer.OrdinalIgnoreCase));
            if(head==null || !(head.TokenType==TSqlTokenType.Insert || head.TokenType==TSqlTokenType.Update))return null;
            int at=all.IndexOf(head)+1;
            if(at<all.Count && all[at].TokenType==TSqlTokenType.Into)at++;
            // TOP and multi-database targets require a complete statement, so leave them to SSMS.
            if(at>=all.Count || !Name(all[at]))return null;
            var target=new List<string>{SqlName.Decode(all[at++].Text)};
            while(at+1<all.Count && all[at].TokenType==TSqlTokenType.Dot && Name(all[at+1])) { target.Add(SqlName.Decode(all[at+1].Text));at+=2; }
            if(target.Count>2 || at>=all.Count)return null;
            bool insert=head.TokenType==TSqlTokenType.Insert;
            int region, finish=sql.Length;
            if(insert)
            {
                if(all[at].TokenType!=TSqlTokenType.LeftParenthesis)return null;
                region=at+1;
                var close=all.Skip(region).FirstOrDefault(t=>depths[t.Offset]==1 && t.TokenType==TSqlTokenType.RightParenthesis);
                if(close!=null)finish=close.Offset;
            }
            else
            {
                var set=all.Skip(at).FirstOrDefault(t=>depths[t.Offset]==0 && t.TokenType==TSqlTokenType.Set);
                if(set==null)return null;
                region=all.IndexOf(set)+1;
                var tail=all.Skip(region).FirstOrDefault(t=>depths[t.Offset]==0 && new[]{"FROM","WHERE","OUTPUT","OPTION","SELECT","UPDATE","INSERT","DELETE","MERGE","GO",";"}.Contains(t.Text,StringComparer.OrdinalIgnoreCase));
                if(tail!=null)finish=tail.Offset;
            }
            int begin=all[region-1].Offset+all[region-1].Text.Length;
            if(caret<begin || caret>finish)return null;
            int start=caret,end=caret;
            var current=all.FirstOrDefault(t=>t.Offset<caret && caret<=t.Offset+t.Text.Length && Name(t));
            string prefix="";
            if(current!=null) { start=current.Offset;end=current.Offset+current.Text.Length;prefix=SqlName.Decode(sql.Substring(start,caret-start)); }
            else
            {
                // ScriptDOM omits unfinished delimited identifiers from its token stream.
                int last=all.Where(t=>t.Offset<caret).Select(t=>t.Offset+t.Text.Length).DefaultIfEmpty(begin).Max();
                string gap=sql.Substring(last,caret-last).TrimStart();
                if(gap.StartsWith("[",StringComparison.Ordinal) || gap.StartsWith("\"",StringComparison.Ordinal))
                {
                    start=caret-gap.Length;prefix=SqlName.Decode(gap);char closing=gap[0]=='['?']':'"';
                    for(int i=caret;i<sql.Length && sql[i]!='\r' && sql[i]!='\n';i++)if(sql[i]==closing) {if(i+1<sql.Length && sql[i+1]==closing){i++;continue;}end=i+1;break;}
                }
            }
            int level=insert?1:0;
            var regionTokens=all.Skip(region).Where(t=>t.Offset<finish).ToList();
            int segment=regionTokens.Where(t=>t.Offset<start && depths[t.Offset]==level && t.TokenType==TSqlTokenType.Comma).Select(t=>t.Offset+t.Text.Length).DefaultIfEmpty(begin).Max();
            var preceding=regionTokens.Where(t=>t.Offset>=segment && t.Offset<start).ToList();
            // An UPDATE RHS expression is different from its assignment's column name.
            if(preceding.Any(t=>depths[t.Offset]==level && t.Text=="="))return null;
            var qualifier=new List<string>();
            if(preceding.Count!=0)
            {
                if(insert || preceding.Count!=2 || !Name(preceding[0]) || preceding[1].TokenType!=TSqlTokenType.Dot)return null;
                qualifier.Add(SqlName.Decode(preceding[0].Text));
            }
            var context=new QueryContext { ReplaceStart=start,ReplaceLength=end-start,Locals=LocalTables.Read(sql,caret) };
            string targetName=target.Last();
            var use=new TableUse{Name=targetName,Schema=target.Count==2?target[0]:null,IsLocal=LocalTables.IsLocal(targetName)};
            if(!insert)
            {
                var from=all.Skip(region).FirstOrDefault(t=>depths[t.Offset]==0 && t.TokenType==TSqlTokenType.From);
                if(from!=null)
                {
                    var tail=all.Skip(all.IndexOf(from)+1).FirstOrDefault(t=>depths[t.Offset]==0 && new[]{"WHERE","OUTPUT","OPTION","SELECT","UPDATE","INSERT","DELETE","MERGE","GO",";"}.Contains(t.Text,StringComparer.OrdinalIgnoreCase));
                    string sources="SELECT * "+sql.Substring(from.Offset,(tail==null?sql.Length:tail.Offset)-from.Offset)+" WHERE x";
                    var binding=SqlAnalysis.ExpressionScope(sources,sources.Length-1,1);
                    var found=ShapeResolver.MatchTables(binding.Tables,target.ToArray());
                    if(found.Count==1)use=found[0];
                }
            }
            if(qualifier.Count>0 && !string.Equals(qualifier[0],use.Alias??use.Name,StringComparison.OrdinalIgnoreCase))return null;
            context.Tables.Add(use);
            // Exclude assignments both before and after the caret, except the item being edited.
            var segments=new List<List<TSqlParserToken>>{new List<TSqlParserToken>()};
            foreach(var t in regionTokens)
            {
                if(depths[t.Offset]==level && t.TokenType==TSqlTokenType.Comma)segments.Add(new List<TSqlParserToken>());
                else segments.Last().Add(t);
            }
            foreach(var part in segments)
            {
                var names=part.TakeWhile(t=>t.Text!="=").Where(Name).ToList();
                var column=names.LastOrDefault();
                if(column!=null && !(column.Offset==start) && !(part.Count==0) && (insert || part.Any(t=>t.Text=="=")))context.AssignedColumns.Add(SqlName.Decode(column.Text));
            }
            return new AutomaticTrigger { Context=context,Caret=caret,Mode=insert?CompletionMode.InsertColumn:CompletionMode.UpdateColumn,Prefix=new[]{prefix},Qualifier=qualifier.ToArray(),
                BaseKey=(insert?"INSERT":"UPDATE")+"\0"+sql.Substring(0,start)+"\0"+sql.Substring(end) };
        }
        static bool Name(TSqlParserToken t) { return t.TokenType==TSqlTokenType.Identifier || t.TokenType==TSqlTokenType.QuotedIdentifier || t.TokenType==TSqlTokenType.Variable; }
    }
}
