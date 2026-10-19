using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    internal static class PredicateCompletion
    {
        internal static AutomaticTrigger TryCreate(string sql,int caret)
        {
            var tokens=SqlAnalysis.Tokens(sql).Where(t=>t.Offset<caret && SqlAnalysis.Significant(t)).ToList();
            int on=tokens.FindLastIndex(t=>t.TokenType==TSqlTokenType.On);
            if(on<0)return null;
            int start=tokens[on].Offset+tokens[on].Text.Length;
            while(start<caret && char.IsWhiteSpace(sql[start]))start++;
            if(start==caret)return null;
            var parts=tokens.Skip(on+1).ToList();
            if(parts.Count==0 || parts.Count>11 || parts.Any(t=>t.TokenType!=TSqlTokenType.Dot && t.TokenType!=TSqlTokenType.EqualsSign && !Identifier(t)))return null;
            if(parts.Count(t=>t.TokenType==TSqlTokenType.EqualsSign)>1)return null;
            if(parts.Last().TokenType==TSqlTokenType.Dot)return null;
            var comments=SqlAnalysis.Tokens(sql).Any(t=>t.Offset>=start && t.Offset<caret && (t.TokenType==TSqlTokenType.SingleLineComment || t.TokenType==TSqlTokenType.MultilineComment));
            if(comments)return null;
            // Only a simple unfinished identifier/equality; never replace AND/OR or another condition.
            int end=caret;var last=parts.Last();
            if(Identifier(last))end=Math.Max(end,last.Offset+last.Text.Length);
            var next=SqlAnalysis.Tokens(sql).FirstOrDefault(t=>t.Offset>=end && SqlAnalysis.Significant(t));
            if(next!=null && !new[]{";","GO",")","WHERE","GROUP","ORDER","HAVING","OPTION","JOIN","INNER","LEFT","RIGHT","FULL"}.Contains(next.Text,StringComparer.OrdinalIgnoreCase))return null;
            string changed=sql.Remove(start,end-start);
            QueryContext context;
            try{context=SqlAnalysis.AnalyzeJoin(changed,start,true);}catch(InvalidOperationException){return null;}
            context.ReplaceStart=start;context.ReplaceLength=end-start;
            string typed=sql.Substring(start,caret-start);
            var significant=SqlAnalysis.Tokens(typed).Where(SqlAnalysis.Significant).ToList();
            string prefix=significant.Count>0 && Identifier(significant.Last())?SqlName.Decode(significant.Last().Text):"";
            var fallback=AutomaticTrigger.NameTrigger(sql,caret);
            return new AutomaticTrigger{Context=context,Mode=CompletionMode.PartialPredicate,Caret=caret,Prefix=new[]{prefix},PredicatePrefix=typed,NameFallback=fallback,
                FallbackHead=fallback==null?null:sql.Substring(start,fallback.Context.ReplaceStart-start),
                BaseKey="PARTIALON\0"+sql.Substring(0,start)+"\0"+sql.Substring(end)+"\0"+(fallback?.BaseKey??"")};
        }
        static bool Identifier(TSqlParserToken t)
        {return t.TokenType==TSqlTokenType.Identifier || t.TokenType==TSqlTokenType.QuotedIdentifier;}
        static string[] Parts(string sql)
        {
            var tokens=SqlAnalysis.Tokens(sql).Where(SqlAnalysis.Significant).ToList();
            var parts=tokens.Select(t=>Identifier(t)?SqlName.Decode(t.Text):t.Text).ToList();
            int end=tokens.Count==0?0:tokens.Last().Offset+tokens.Last().Text.Length;
            string gap=sql.Substring(end).TrimStart();
            if(gap.StartsWith("[",StringComparison.Ordinal) || gap.StartsWith("\"",StringComparison.Ordinal))parts.Add(SqlName.Decode(gap));
            return parts.ToArray();
        }
        internal static bool Matches(string typed,string predicate)
        {
            var prefix=Parts(typed);var full=Parts(predicate);
            if(prefix.Length>full.Length)return false;
            for(int i=0;i<prefix.Length;i++)if(i==prefix.Length-1?!full[i].StartsWith(prefix[i],StringComparison.OrdinalIgnoreCase):!string.Equals(full[i],prefix[i],StringComparison.OrdinalIgnoreCase))return false;
            return true;
        }
        internal static string Reverse(string predicate)
        {
            var tokens=SqlAnalysis.Tokens(predicate).Where(SqlAnalysis.Significant).ToList();
            int equals=tokens.FindIndex(t=>t.TokenType==TSqlTokenType.EqualsSign);
            int and=tokens.FindIndex(t=>t.TokenType==TSqlTokenType.And);if(and<0)and=tokens.Count;
            if(equals<0)return predicate;
            int end=tokens[and-1].Offset+tokens[and-1].Text.Length;
            return predicate.Substring(tokens[equals+1].Offset,end-tokens[equals+1].Offset)+" = "+predicate.Substring(0,tokens[equals].Offset).TrimEnd()+predicate.Substring(end);
        }
    }
}
