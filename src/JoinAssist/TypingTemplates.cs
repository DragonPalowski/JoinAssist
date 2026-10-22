using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    public static class TypingTemplates
    {
        static bool Identifier(TSqlParserToken t) {return t.TokenType==TSqlTokenType.Identifier || t.TokenType==TSqlTokenType.QuotedIdentifier;}
        static string Upper(TSqlParserToken t) {return t.Text.ToUpperInvariant();}
        public static AutomaticTrigger FilterTrigger(string sql,int caret)
        {
            if(!PreferenceStore.Current.FilterTemplates)return null;
            var before=SqlAnalysis.Tokens(sql.Substring(0,caret)).Where(SqlAnalysis.Significant).ToList();
            if(before.Count<3)return null;
            var op=before.Last();string word=Upper(op);
            if(word!="LIKE" && word!="IN" || !sql.Substring(op.Offset+op.Text.Length,caret-op.Offset-op.Text.Length).All(char.IsWhiteSpace))return null;
            var full=SqlAnalysis.Tokens(sql).Where(SqlAnalysis.Significant).ToList();
            if(!full.Any(t=>t.Offset==op.Offset && t.Text.Length==op.Text.Length))return null;
            var next=full.FirstOrDefault(t=>t.Offset>=op.Offset+op.Text.Length);
            if(next!=null && !new[]{"AND","OR","GROUP","ORDER","HAVING","OPTION","UNION","EXCEPT","INTERSECT","SELECT",";",")"}.Contains(Upper(next)))return null;
            int oi=before.Count-1,begin=oi;
            if(oi>0 && Upper(before[oi-1])=="NOT")begin--;
            if(begin<oi && !sql.Substring(before[begin].Offset+before[begin].Text.Length,op.Offset-before[begin].Offset-before[begin].Text.Length).All(char.IsWhiteSpace))return null;
            int col=begin-1;if(col<0 || !Identifier(before[col]))return null;
            var parts=new List<string>{SqlName.Decode(before[col].Text)};
            while(col>=2 && before[col-1].TokenType==TSqlTokenType.Dot && Identifier(before[col-2]))
            {col-=2;parts.Insert(0,SqlName.Decode(before[col].Text));}
            if(parts.Count>3 || col==0 || !new[]{"WHERE","ON","AND","OR","HAVING","WHEN","(","NOT"}.Contains(Upper(before[col-1])))return null;
            int start=before[begin].Offset,end=op.Offset+op.Text.Length;
            var scope=SqlAnalysis.ExpressionScope(sql,before[col].Offset,before[begin-1].Offset+before[begin-1].Text.Length-before[col].Offset);
            scope.ReplaceStart=start;scope.ReplaceLength=end-start;
            return new AutomaticTrigger{Mode=CompletionMode.FilterTemplate,Context=scope,Caret=caret,Prefix=new string[0],TemplateOperator=(begin<oi?"NOT ":"")+word,TemplateOperand=parts.ToArray(),
                BaseKey="FILTER\0"+sql.Substring(0,start)+"\0"+sql.Substring(end)+"\0"+(begin<oi?"NOT ":"")+word};
        }
        public static List<JoinProposal> FilterItems(AutomaticTrigger trigger)
        {
            var context=trigger.Context;string[] operand=trigger.TemplateOperand;
            var local=context.Tables.AsEnumerable();var outer=context.OuterTables.AsEnumerable();
            if(operand.Length>1)
            {
                local=ShapeResolver.MatchTables(local,operand.Take(operand.Length-1).ToArray());
                outer=ShapeResolver.MatchTables(outer,operand.Take(operand.Length-1).ToArray());
                // An inner alias shadows an outer alias even when its output shape is unresolved.
                if(local.Any())outer=Enumerable.Empty<TableUse>();
                else if(outer.Any())outer=outer.Where(t=>t.ScopeLevel==outer.Min(o=>o.ScopeLevel)).ToList();
            }
            // With an unqualified name, an unreadable source could contain a competing column.
            if(operand.Length==1 && local.Any(t=>t.Columns==null || t.Columns.Count==0))return new List<JoinProposal>();
            Func<IEnumerable<TableUse>,List<ColumnShape>> find=tables=>tables.SelectMany(t=>t.Columns??new List<ColumnShape>()).Where(c=>string.Equals(c.Name,operand.Last(),StringComparison.OrdinalIgnoreCase)).ToList();
            var columns=find(local);
            if(columns.Count==0 && (operand.Length==1 || !local.Any()))
            {
                var possible=outer.Where(t=>(t.Columns??new List<ColumnShape>()).Any(c=>string.Equals(c.Name,operand.Last(),StringComparison.OrdinalIgnoreCase))).ToList();
                if(possible.Count>0)columns=find(possible.Where(t=>t.ScopeLevel==possible.Min(o=>o.ScopeLevel)));
            }
            if(columns.Count!=1 || string.IsNullOrEmpty(columns[0].TypeName))return new List<JoinProposal>();
            string type=columns[0].TypeName.Split('(')[0].Trim().ToLowerInvariant();
            bool text=new[]{"char","varchar","nchar","nvarchar","sysname","text","ntext"}.Contains(type);
            bool comparable=text && type!="text" && type!="ntext" || new[]{"tinyint","smallint","int","bigint","decimal","numeric","money","smallmoney","float","real","bit","date","time","datetime","datetime2","smalldatetime","datetimeoffset","uniqueidentifier","binary","varbinary","timestamp","rowversion"}.Contains(type);
            bool like=trigger.TemplateOperator.EndsWith("LIKE",StringComparison.Ordinal);
            if(like && !text || !like && !comparable)return new List<JoinProposal>();
            string keyword=PreferenceStore.Keyword(trigger.TemplateOperator),insert=keyword+(like?" '%%'":text?" ('', '')":" ( ,)");
            int cursor=like?insert.IndexOf("%%",StringComparison.Ordinal)+1:text?insert.IndexOf("''",StringComparison.Ordinal)+1:insert.IndexOf('(')+1;
            return new List<JoinProposal>{new JoinProposal{IsFilterTemplate=true,TemplateOperator=trigger.TemplateOperator,CompletionText=insert,CaretOffset=cursor,SearchName=keyword,Category="Filtro SQL",Column=columns[0],
                DisplayText=insert+"  |  "+string.Join(".",operand)+" · "+columns[0].Description+" · Tab prepara il filtro"}};
        }
        public static AutomaticTrigger KeywordTrigger(string sql,int caret)
        {
            if(!PreferenceStore.Current.KeywordCompletion)return null;
            var before=SqlAnalysis.Tokens(sql.Substring(0,caret)).Where(SqlAnalysis.Significant).ToList();
            if(before.Count==0)return null;
            var last=before.Last();string typed=last.Text;
            if(last.Offset+typed.Length!=caret || typed.Length<2 || !typed.All(char.IsLetter))return null;
            before.RemoveAt(before.Count-1);
            if(before.Count>0 && before.Last().TokenType==TSqlTokenType.Dot)return null;
            var full=SqlAnalysis.Tokens(sql).FirstOrDefault(t=>t.Offset==last.Offset && !string.IsNullOrEmpty(t.Text) && t.Text.All(char.IsLetter));
            if(full==null)return null;
            int end=full.Offset+full.Text.Length;
            var candidates=KeywordChoices(before).ToArray();
            int line=last.Offset==0?0:sql.LastIndexOf('\n',last.Offset-1)+1;
            if(sql.Substring(line,last.Offset-line).All(char.IsWhiteSpace))
                candidates=candidates.Concat(new[]{"SELECT","INSERT INTO","UPDATE","DELETE FROM","WITH"}).Distinct().ToArray();
            if(!candidates.Any(k=>k.StartsWith(typed,StringComparison.OrdinalIgnoreCase)))return null;
            return new AutomaticTrigger{Mode=CompletionMode.Keyword,Prefix=new[]{typed},Caret=caret,KeywordChoices=candidates,KeywordSpace=end==sql.Length || !char.IsWhiteSpace(sql[end]),
                Context=new QueryContext{ReplaceStart=last.Offset,ReplaceLength=end-last.Offset},BaseKey="KEYWORD\0"+sql.Substring(0,last.Offset)+"\0"+sql.Substring(end)};
        }
        static IEnumerable<string> KeywordChoices(IList<TSqlParserToken> tokens)
        {
            int depth=0;var at=new Dictionary<int,string>();string previous=tokens.Count==0?"":Upper(tokens.Last());
            foreach(var t in tokens)
            {
                string word=Upper(t);
                if(word==";" || word=="GO"){at.Clear();depth=0;continue;}
                if(word=="("){depth++;at.Remove(depth);continue;}
                if(word==")"){at.Remove(depth);depth=Math.Max(0,depth-1);continue;}
                if(new[]{"SELECT","FROM","JOIN","ON","WHERE","HAVING","GROUP","ORDER","UPDATE","SET","INSERT","INTO","DELETE","VALUES","WITH"}.Contains(word))at[depth]=word;
            }
            string clause;if(!at.TryGetValue(depth,out clause))return new[]{"SELECT","INSERT INTO","UPDATE","DELETE FROM","WITH"};
            if(previous=="NOT")return new[]{"LIKE","IN","EXISTS"};
            if(previous=="GROUP" || previous=="ORDER")return new[]{"BY"};
            if(new[]{"FROM","JOIN","INTO","UPDATE","APPLY","AS"}.Contains(previous))return new string[0];
            if(clause=="SELECT")return previous=="SELECT"?new[]{"DISTINCT","TOP","CASE","NULL","FROM"}:new[]{"FROM","AS","CASE","NULL"};
            if(clause=="JOIN")return new[]{"ON","AS"};
            if(clause=="FROM")return new[]{"WHERE","INNER JOIN","LEFT JOIN","RIGHT JOIN","FULL JOIN","CROSS JOIN","CROSS APPLY","OUTER APPLY","JOIN","GROUP BY","ORDER BY","AS","UNION ALL"};
            if(clause=="WHERE" || clause=="ON" || clause=="HAVING")return new[]{"AND","OR","NOT","LIKE","NOT LIKE","IN","NOT IN","IS NULL","IS NOT NULL","BETWEEN","EXISTS","NOT EXISTS","GROUP BY","ORDER BY"};
            if(clause=="GROUP")return new[]{"HAVING","ORDER BY"};
            if(clause=="ORDER")return new[]{"ASC","DESC"};
            if(clause=="UPDATE")return new[]{"SET"};
            if(clause=="INTO" || clause=="INSERT")return new[]{"VALUES","SELECT"};
            if(clause=="SET")return new[]{"WHERE","FROM"};
            if(clause=="WITH")return new[]{"SELECT"};
            return new string[0];
        }
        public static List<JoinProposal> KeywordItems(AutomaticTrigger trigger)
        {
            if(!PreferenceStore.Current.KeywordCompletion)return new List<JoinProposal>();
            return (trigger.KeywordChoices??new string[0]).Select(k=>new JoinProposal{Keyword=k,SearchName=k,CompletionText=PreferenceStore.Keyword(k)+(trigger.KeywordSpace?" ":""),Category="Parola chiave",ContextRank=500,
                DisplayText=PreferenceStore.Keyword(k)+"  |  Parola chiave SQL · Tab completa"}).ToList();
        }
    }
}
