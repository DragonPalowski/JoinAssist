using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    // Analysis only. No edited DML statement is ever sent to the server.
    internal static class DmlCompletion
    {
        internal static QueryContext FilterScope(string sql,int position,int length)
        {
            var tokens=SqlAnalysis.Tokens(sql).Where(SqlAnalysis.Significant).ToList();
            var top=new List<TSqlParserToken>();int depth=0;
            foreach(var token in tokens)
            {
                if(depth==0)top.Add(token);
                if(token.TokenType==TSqlTokenType.LeftParenthesis)depth++;
                else if(token.TokenType==TSqlTokenType.RightParenthesis)depth=Math.Max(0,depth-1);
            }
            var boundaries=new[]{"SELECT","INSERT","UPDATE","DELETE","MERGE","GO",";","CREATE","ALTER","DROP","DECLARE","EXEC","EXECUTE","USE","PRINT","RETURN","TRUNCATE","IF","WHILE","BEGIN","END"};
            var head=top.LastOrDefault(t=>t.Offset<position && boundaries.Contains(t.Text,StringComparer.OrdinalIgnoreCase));
            if(head==null || head.TokenType!=TSqlTokenType.Delete && head.TokenType!=TSqlTokenType.Update)return null;
            var where=top.FirstOrDefault(t=>t.Offset>head.Offset && t.TokenType==TSqlTokenType.Where);
            if(where==null || position<where.Offset+where.Text.Length)return null;
            // A nested SELECT owns its own columns; never turn a failed inner scope into the DML target.
            var nested=new List<bool>{false};
            foreach(var token in tokens.Where(t=>t.Offset>where.Offset && t.Offset<position))
            {
                if(token.TokenType==TSqlTokenType.LeftParenthesis)nested.Add(false);
                else if(token.TokenType==TSqlTokenType.RightParenthesis && nested.Count>1)nested.RemoveAt(nested.Count-1);
                else if(token.TokenType==TSqlTokenType.Select)nested[nested.Count-1]=true;
            }
            if(nested.Any(value=>value))return null;
            int begin=head.Offset;
            foreach(var token in top.Where(t=>t.Offset<head.Offset).Reverse())
            {
                if(boundaries.Contains(token.Text,StringComparer.OrdinalIgnoreCase))break;
                if(token.TokenType==TSqlTokenType.With){begin=token.Offset;break;}
            }
            // Replace the unfinished filter in an analysis-only copy, isolating earlier/later statements.
            // Parse the original DML header so aliases, TOP, OUTPUT, hints and CTEs keep their SQL meaning.
            string repaired=sql.Substring(begin,where.Offset+where.Text.Length-begin)+" 1=1;";
            IList<ParseError> errors;
            var script=new TSql160Parser(true).Parse(new StringReader(repaired),out errors);
            if(errors.Count!=0 || script==null)return null;
            var reader=new FilterReader();script.Accept(reader);
            if(reader.Target==null)return null;
            var context=new QueryContext{ReplaceStart=position,ReplaceLength=length,Clause="WHERE",Locals=LocalTables.Read(sql,position)};
            if(reader.Ctes!=null)
                foreach(var cte in reader.Ctes.CommonTableExpressions)context.Ctes[cte.ExpressionName.Value]=cte;
            if(reader.From!=null)
                foreach(var source in reader.From.TableReferences)SqlAnalysis.Collect(source,context.Tables,context.Ctes);
            // An alias target is already bound by FROM; an independent target remains visible too.
            var targets=new List<TableUse>();SqlAnalysis.Collect(reader.Target,targets,context.Ctes);
            foreach(var target in targets)
                if(!context.Tables.Any(t=>target.Schema==null && string.Equals(t.Alias??t.Name,target.Name,StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.Name,target.Name,StringComparison.OrdinalIgnoreCase) && (target.Schema==null || string.Equals(t.Schema,target.Schema,StringComparison.OrdinalIgnoreCase))))
                    context.Tables.Add(target);
            return context;
        }
        sealed class FilterReader:TSqlFragmentVisitor
        {
            internal TableReference Target;
            internal FromClause From;
            internal WithCtesAndXmlNamespaces Ctes;
            public override void ExplicitVisit(DeleteStatement node)
            {Target=node.DeleteSpecification.Target;From=node.DeleteSpecification.FromClause;Ctes=node.WithCtesAndXmlNamespaces;}
            public override void ExplicitVisit(UpdateStatement node)
            {Target=node.UpdateSpecification.Target;From=node.UpdateSpecification.FromClause;Ctes=node.WithCtesAndXmlNamespaces;}
        }
        internal static AutomaticTrigger TryCreate(string sql, int caret)
        {
            var all = SqlAnalysis.Tokens(sql).Where(SqlAnalysis.Significant).ToList();
            var depths = new Dictionary<int,int>(); int depth=0;
            foreach(var t in all) { depths[t.Offset]=depth; if(t.TokenType==TSqlTokenType.LeftParenthesis)depth++;if(t.TokenType==TSqlTokenType.RightParenthesis)depth--; }
            var head=all.LastOrDefault(t=>t.Offset<caret && depths[t.Offset]==0 && new[]{"INSERT","UPDATE","DELETE","MERGE","SELECT","GO",";"}.Contains(t.Text,StringComparer.OrdinalIgnoreCase));
            if(head==null || !(head.TokenType==TSqlTokenType.Insert || head.TokenType==TSqlTokenType.Update))return null;
            SqlAnalysis.CheckDatabaseContext(sql,caret);
            int at=all.IndexOf(head)+1;
            if(at<all.Count && all[at].TokenType==TSqlTokenType.Into)at++;
            // TOP and multi-database targets require a complete statement, so leave them to SSMS.
            if(at>=all.Count || !Name(all[at]))return null;
            var target=new List<string>{SqlName.Decode(all[at++].Text)};
            while(at+1<all.Count && all[at].TokenType==TSqlTokenType.Dot && Name(all[at+1])) { target.Add(SqlName.Decode(all[at+1].Text));at+=2; }
            if(target.Count>2)return null;
            bool insert=head.TokenType==TSqlTokenType.Insert;
            int targetEnd=all[at-1].Offset+all[at-1].Text.Length;
            if(insert && caret>targetEnd && sql.Substring(targetEnd,caret-targetEnd).All(char.IsWhiteSpace) &&
                (at>=all.Count || all[at].Offset>=caret && (all[at].TokenType==TSqlTokenType.Semicolon || all[at].TokenType==TSqlTokenType.Go)))
            {
                var templateContext=new QueryContext{ReplaceStart=caret,Locals=LocalTables.Read(sql,caret)};
                var templateTarget=InsertTarget(sql,head,targetEnd,all,depths,target,templateContext);
                if(templateTarget==null)return null;
                templateContext.Tables.Add(templateTarget);
                int line=sql.LastIndexOf('\n',Math.Max(0,head.Offset-1));
                string indent=new string(sql.Substring(line+1,head.Offset-line-1).TakeWhile(c=>c==' ' || c=='\t').ToArray());
                return new AutomaticTrigger{Context=templateContext,Caret=caret,Mode=CompletionMode.InsertTemplate,Prefix=new string[0],InsertIndent=indent,
                    InsertNewLine=sql.Contains("\r\n")?"\r\n":sql.Contains("\n")?"\n":Environment.NewLine,InsertTerminator=at>=all.Count || all[at].TokenType!=TSqlTokenType.Semicolon,
                    BaseKey="INSERT-TEMPLATE\0"+sql.Substring(0,caret)+"\0"+sql.Substring(caret)};
            }
            if(at>=all.Count)return null;
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
            if(insert){use=InsertTarget(sql,head,targetEnd,all,depths,target,context);if(use==null)return null;}
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
            var following=all.FirstOrDefault(t=>t.Offset>=caret);
            bool fill=insert && prefix.Length==0 && regionTokens.All(t=>Name(t)||t.TokenType==TSqlTokenType.Comma) &&
                (following==null || following.TokenType==TSqlTokenType.RightParenthesis || following.TokenType==TSqlTokenType.Comma);
            return new AutomaticTrigger { Context=context,Caret=caret,Mode=insert?CompletionMode.InsertColumn:CompletionMode.UpdateColumn,Prefix=new[]{prefix},Qualifier=qualifier.ToArray(),FillInsertColumns=fill,
                BaseKey=(insert?"INSERT":"UPDATE")+"\0"+sql.Substring(0,start)+"\0"+sql.Substring(end) };
        }
        static TableUse InsertTarget(string sql,TSqlParserToken head,int targetEnd,List<TSqlParserToken> all,Dictionary<int,int> depths,List<string> target,QueryContext context)
        {
            var use=new TableUse{Name=target.Last(),Schema=target.Count==2?target[0]:null,IsLocal=LocalTables.IsLocal(target.Last())};
            var boundary=new[]{"SELECT","INSERT","UPDATE","DELETE","MERGE","GO",";","CREATE","ALTER","DROP","DECLARE","EXEC","EXECUTE"};
            int begin=head.Offset;
            foreach(var token in all.Where(t=>t.Offset<head.Offset && depths[t.Offset]==0).Reverse())
            {
                if(boundary.Contains(token.Text,StringComparer.OrdinalIgnoreCase))break;
                if(token.TokenType==TSqlTokenType.With){begin=token.Offset;break;}
            }
            if(begin==head.Offset)return use;
            IList<ParseError> errors;
            var script=new TSql160Parser(true).Parse(new StringReader(sql.Substring(begin,targetEnd-begin)+" DEFAULT VALUES;"),out errors);
            if(errors.Count!=0 || script==null)return null;
            var reader=new InsertReader();script.Accept(reader);
            if(reader.Ctes!=null)foreach(var cte in reader.Ctes.CommonTableExpressions)context.Ctes[cte.ExpressionName.Value]=cte;
            CommonTableExpression definition;
            return target.Count==1 && context.Ctes.TryGetValue(target[0],out definition)?SqlAnalysis.CteUse(target[0],null,definition,context.Ctes):use;
        }
        sealed class InsertReader:TSqlFragmentVisitor
        {
            internal WithCtesAndXmlNamespaces Ctes;
            public override void ExplicitVisit(InsertStatement node){Ctes=node.WithCtesAndXmlNamespaces;}
        }
        static bool Name(TSqlParserToken t) { return t.TokenType==TSqlTokenType.Identifier || t.TokenType==TSqlTokenType.QuotedIdentifier || t.TokenType==TSqlTokenType.Variable; }
    }
}
