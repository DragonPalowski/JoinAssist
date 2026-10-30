using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    internal static class TableSelect
    {
        internal static AutomaticTrigger Trigger(string sql,int caret)
        {
            if(!PreferenceStore.Read.TableSelect)return null;
            int line=caret==0?0:sql.LastIndexOf('\n',caret-1)+1;
            int end=sql.IndexOfAny(new[]{'\r','\n'},caret);if(end<0)end=sql.Length;
            string head=sql.Substring(line,caret-line);
            int indent=head.Length-head.TrimStart().Length,start=line+indent;
            string typed=head.Substring(indent);
            var prefix=AutomaticTrigger.ReadPrefix(typed);
            if(prefix==null || prefix.Length==0 || prefix.Length>2 || prefix.Last().Length<2)return null;
            var full=AutomaticTrigger.ReadPrefix(sql.Substring(start,end-start).TrimEnd().TrimEnd(';').TrimEnd());
            if(full==null || full.Length!=prefix.Length)return null;
            // Require a genuine statement boundary, not a new line in an unfinished query.
            var before=SqlAnalysis.Tokens(sql.Substring(0,line)).Where(SqlAnalysis.Significant).ToList();
            if(before.Count>0 && before.Last().TokenType!=TSqlTokenType.Semicolon && before.Last().TokenType!=TSqlTokenType.Go)return null;
            if(before.Count>0)
            {
                int batch=before.FindLastIndex(t=>t.TokenType==TSqlTokenType.Go);
                var prior=before.Skip(batch+1).ToList();
                if(prior.Any(t=>new[]{"BEGIN","IF","WHILE","PROCEDURE","PROC","FUNCTION","TRIGGER"}.Contains(t.Text,StringComparer.OrdinalIgnoreCase)))return null;
                IList<ParseError> errors;new TSql160Parser(true).Parse(new StringReader(sql.Substring(0,line)),out errors);
                if(errors.Count>0)return null;
            }
            if(TypingTemplates.KeywordTrigger(sql,caret)!=null)return null;
            int replaceEnd=start+sql.Substring(start,end-start).TrimEnd().TrimEnd(';').TrimEnd().Length;
            return new AutomaticTrigger{Mode=CompletionMode.TableSelect,Caret=caret,Prefix=prefix,InsertTerminator=sql.Substring(replaceEnd,end-replaceEnd).TrimStart().StartsWith(";",StringComparison.Ordinal),
                Context=new QueryContext{ReplaceStart=start,ReplaceLength=replaceEnd-start,Locals=LocalTables.Read(sql,start)},
                BaseKey="TABLESELECT\0"+sql.Substring(0,start)+"\0"+sql.Substring(replaceEnd)};
        }
        internal static List<JoinProposal> Items(AutomaticTrigger trigger,CatalogSnapshot catalog)
        {
            var result=CompletionEngine.TableItems(catalog,trigger,null);
            foreach(var item in result)
            {
                string top=PreferenceStore.Read.TableSelectTop>0?PreferenceStore.Keyword("TOP")+" "+PreferenceStore.Read.TableSelectTop.ToString(System.Globalization.CultureInfo.InvariantCulture)+" ":"";
                string suffix=PreferenceStore.Read.TableSelectSemicolon && !trigger.InsertTerminator?";":"";
                item.CompletionText=PreferenceStore.Keyword("SELECT")+" "+top+"* "+PreferenceStore.Keyword("FROM")+" "+item.CompletionText+suffix;
                item.IsTableSelect=true;item.Category="Da tabella a SELECT";
                item.DisplayText=item.CompletionText+"  |  Tab prepara la SELECT";
                item.ScriptOnly=LocalTables.IsLocal(item.TargetTable);
            }
            return result;
        }
    }
}
