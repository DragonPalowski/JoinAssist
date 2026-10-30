using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    internal sealed class ProcedureParameter
    {
        internal string Name,Type;
        internal bool Output,Table,Unsupported;
    }
    internal sealed class ProcedureInfo
    {
        internal int Id;
        internal string Schema,Name;
        internal readonly List<ProcedureParameter> Parameters=new List<ProcedureParameter>();
    }
    internal sealed class ExecutionRequest
    {
        internal int Start,NameStart;
        internal string Header,Sql;
        internal string[] Name;
        internal bool ExistingArguments,ArgumentHasEquals;
        internal int PositionalArguments;
        internal readonly HashSet<string> Assigned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
    internal static class ExecutionCompletion
    {
        static bool Identifier(TSqlParserToken t)
        {return t.TokenType==TSqlTokenType.Identifier || t.TokenType==TSqlTokenType.QuotedIdentifier;}
        internal static AutomaticTrigger Trigger(string sql,int caret)
        {
            if(!PreferenceStore.Read.ExecutionCompletion)return null;
            var tokens=SqlAnalysis.Tokens(sql).Where(SqlAnalysis.Significant).ToList();
            int index=tokens.FindLastIndex(t=>t.Offset<caret && (t.TokenType==TSqlTokenType.Execute || t.Text.Equals("EXEC",StringComparison.OrdinalIgnoreCase)));
            if(index<0)return null;
            var exec=tokens[index];int i=index+1;
            if(caret<=exec.Offset+exec.Text.Length || !char.IsWhiteSpace(sql[exec.Offset+exec.Text.Length]))return null;
            // Dynamic SQL, remote/batch execution and return-assignment forms are left alone.
            if(i<tokens.Count && tokens[i].Offset<caret && tokens[i].TokenType==TSqlTokenType.Variable)return null;
            int start=i<tokens.Count?Math.Min(caret,tokens[i].Offset):caret;
            int nameEnd=start;var parts=new List<string>();
            while(i<tokens.Count && Identifier(tokens[i]))
            {
                parts.Add(SqlName.Decode(tokens[i].Text));nameEnd=tokens[i].Offset+tokens[i].Text.Length;i++;
                if(i>=tokens.Count || tokens[i].TokenType!=TSqlTokenType.Dot)break;
                nameEnd=tokens[i].Offset+1;i++;
                if(i>=tokens.Count || !Identifier(tokens[i])){parts.Add("");break;}
            }
            if(parts.Count>2 || parts.Count==0 && start<caret)return null;
            if(tokens.Skip(index+1).Any(t=>t.Offset<caret && (t.TokenType==TSqlTokenType.Semicolon || t.TokenType==TSqlTokenType.Go || new[]{"SELECT","INSERT","UPDATE","DELETE","EXEC","EXECUTE","WITH","AT"}.Contains(t.Text,StringComparer.OrdinalIgnoreCase))))return null;
            var request=new ExecutionRequest{Start=exec.Offset,NameStart=start,Name=parts.ToArray(),Header=sql.Substring(exec.Offset,start-exec.Offset),Sql=sql};
            if(caret<=nameEnd || parts.Count==0)
            {
                string text=sql.Substring(start,Math.Max(0,caret-start));var prefix=AutomaticTrigger.ReadPrefix(text);
                if(prefix==null)return null;if(prefix.Length==0)prefix=new[]{""};
                request.ExistingArguments=i<tokens.Count && tokens[i].TokenType!=TSqlTokenType.Semicolon && tokens[i].TokenType!=TSqlTokenType.Go && !NextStatement(tokens[i]);
                return new AutomaticTrigger{Mode=CompletionMode.Execution,Caret=caret,Prefix=prefix,Execution=request,
                    Context=new QueryContext{ReplaceStart=exec.Offset,ReplaceLength=nameEnd-exec.Offset},
                    BaseKey="EXEC\0"+sql.Substring(0,exec.Offset)+"\0"+sql.Substring(nameEnd)};
            }
            if(parts.Count==0 || parts.Last().Length==0)return null;
            var args=tokens.Skip(i).Where(t=>t.Offset<caret).ToList();
            int comma=args.FindLastIndex(t=>t.TokenType==TSqlTokenType.Comma);
            var slot=args.Skip(comma+1).ToList();
            if(slot.Count>1 || slot.Count==1 && slot[0].TokenType!=TSqlTokenType.Variable)return null;
            foreach(var pair in args.Select((t,n)=>new{t,n}).Where(p=>p.t.TokenType==TSqlTokenType.Variable && p.n+1<args.Count && args[p.n+1].TokenType==TSqlTokenType.EqualsSign))request.Assigned.Add(pair.t.Text);
            var completed=new List<TSqlParserToken>();
            foreach(var token in args.Take(comma+1))
            {
                if(token.TokenType==TSqlTokenType.Comma){if(completed.Count>0 && !(completed.Count>1 && completed[0].TokenType==TSqlTokenType.Variable && completed[1].TokenType==TSqlTokenType.EqualsSign))request.PositionalArguments++;completed.Clear();}
                else completed.Add(token);
            }
            string typed=slot.Count==0?"":slot[0].Text;int at=slot.Count==0?caret:slot[0].Offset;
            var full=tokens.FirstOrDefault(t=>t.Offset==at && t.TokenType==TSqlTokenType.Variable);int end=full==null?caret:full.Offset+full.Text.Length;
            request.ArgumentHasEquals=tokens.FirstOrDefault(t=>t.Offset>=end)?.TokenType==TSqlTokenType.EqualsSign;
            if(tokens.Any(t=>t.Offset>=end && t.Offset<caret))return null;
            return new AutomaticTrigger{Mode=CompletionMode.ExecutionParameter,Caret=caret,Prefix=new[]{typed},Execution=request,
                Context=new QueryContext{ReplaceStart=at,ReplaceLength=end-at},
                BaseKey="EXECPARAM\0"+sql.Substring(0,at)+"\0"+sql.Substring(end)};
        }
        static bool NextStatement(TSqlParserToken t)
        {return new[]{"SELECT","INSERT","UPDATE","DELETE","EXEC","EXECUTE","DROP","CREATE","ALTER","PRINT"}.Contains(t.Text,StringComparer.OrdinalIgnoreCase);}
        internal static async Task<List<ProcedureInfo>> ReadAsync(DbConnection connection,CancellationToken cancel,MetadataScope scope)
        {
            return await MetadataCache.ReadAsync(scope,"procedures:v1",async()=>{
                if(connection.State!=ConnectionState.Open)await connection.OpenAsync(cancel).ConfigureAwait(false);
                var result=new List<ProcedureInfo>();int count=0;
                using(var cmd=connection.CreateCommand())
                {
                    cmd.CommandTimeout=10;
                    cmd.CommandText=@"SELECT o.object_id,s.name,o.name,p.name,ts.name,t.name,p.max_length,p.precision,p.scale,p.is_output,t.is_table_type,t.is_user_defined,p.is_cursor_ref
FROM sys.all_objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
LEFT JOIN sys.all_parameters p ON p.object_id=o.object_id AND p.parameter_id>0
LEFT JOIN sys.types t ON t.user_type_id=p.user_type_id LEFT JOIN sys.schemas ts ON ts.schema_id=t.schema_id
WHERE o.type IN ('P','PC') ORDER BY o.object_id,p.parameter_id; -- ja_exec_catalog";
                    using(var rows=await cmd.ExecuteReaderAsync(cancel).ConfigureAwait(false))
                        while(await rows.ReadAsync(cancel).ConfigureAwait(false))
                        {
                            if(++count>100000)throw new InvalidOperationException("Troppi parametri visibili per il completamento EXEC.");
                            if(result.Count>10000)throw new InvalidOperationException("Troppe procedure visibili per il completamento EXEC.");
                            int id=rows.GetInt32(0);var proc=result.LastOrDefault();
                            if(proc==null || proc.Id!=id){proc=new ProcedureInfo{Id=id,Schema=rows.GetString(1),Name=rows.GetString(2)};result.Add(proc);}
                            if(rows.IsDBNull(3))continue;
                            string type=rows.IsDBNull(5)?null:rows.GetString(5);bool user=!rows.IsDBNull(11)&&rows.GetBoolean(11);
                            if(type!=null)
                            {
                                if(user)type=SqlName.Quote(rows.GetString(4))+"."+SqlName.Quote(type);
                                else if(new[]{"varchar","nvarchar","char","nchar","binary","varbinary"}.Contains(type))
                                {int len=Convert.ToInt32(rows.GetValue(6));type+="("+(len<0?"max":(type.StartsWith("n",StringComparison.Ordinal)?len/2:len).ToString(System.Globalization.CultureInfo.InvariantCulture))+")";}
                                else if(type=="decimal" || type=="numeric")type+="("+rows.GetValue(7)+","+rows.GetValue(8)+")";
                                else if(type=="datetime2" || type=="datetimeoffset" || type=="time")type+="("+rows.GetValue(8)+")";
                            }
                            proc.Parameters.Add(new ProcedureParameter{Name=rows.GetString(3),Type=type,Output=rows.GetBoolean(9),Table=!rows.IsDBNull(10)&&rows.GetBoolean(10),Unsupported=type==null || rows.GetBoolean(12) || type=="timestamp" || type=="rowversion"});
                        }
                }
                return result;
            },cancel).ConfigureAwait(false);
        }
        internal static List<JoinProposal> Items(AutomaticTrigger trigger,List<ProcedureInfo> procedures)
        {
            var r=trigger.Execution;var result=new List<JoinProposal>();
            if(trigger.Mode==CompletionMode.ExecutionParameter)
            {
                var matches=procedures.Where(p=>p.Name.Equals(r.Name.Last(),StringComparison.OrdinalIgnoreCase) && (r.Name.Length==1 || p.Schema.Equals(r.Name[0],StringComparison.OrdinalIgnoreCase))).ToList();
                if(matches.Count!=1)return result;
                foreach(var p in matches[0].Parameters.Skip(r.PositionalArguments).Where(p=>!r.Assigned.Contains(p.Name)))
                    result.Add(new JoinProposal{IsExecution=true,SearchName=p.Name,TargetTable=p.Name,Category="Parametro EXEC",CompletionText=p.Name+(r.ArgumentHasEquals?"":" = "),DisplayText=p.Name+"  |  "+p.Type+(p.Output?" OUTPUT":"")+(p.Table?" · tabella READONLY":"")+" · inserisci il valore"});
                return result;
            }
            foreach(var proc in procedures)
            {
                string name=SqlName.Format(proc.Schema)+"."+SqlName.Format(proc.Name);
                var item=new JoinProposal{IsExecution=true,TargetSchema=proc.Schema,TargetTable=proc.Name,SearchName=proc.Name,Category="Procedura",DisplayText=name+"  |  "+(proc.Parameters.Count==0?"Nessun parametro":string.Join(", ",proc.Parameters.Select(p=>p.Name+" "+p.Type+(p.Output?" OUTPUT":"")+(p.Table?" READONLY":""))))};
                item.CompletionText=r.Header+name;
                if(item.DisplayText.Length>1200)item.DisplayText=item.DisplayText.Substring(0,1200)+"… · firma abbreviata";
                // Existing arguments and conditional/procedural contexts get only name completion.
                bool plain=r.Sql.Substring(r.Sql.LastIndexOf('\n',Math.Max(0,r.Start-1))+1,r.Start-(r.Sql.LastIndexOf('\n',Math.Max(0,r.Start-1))+1)).All(char.IsWhiteSpace);
                var history=SqlAnalysis.Tokens(r.Sql.Substring(0,r.Start)).Where(SqlAnalysis.Significant).ToList();
                int go=history.FindLastIndex(t=>t.TokenType==TSqlTokenType.Go);
                plain=plain && !history.Skip(go+1).Any(t=>new[]{"IF","WHILE","BEGIN","PROCEDURE","PROC","TRIGGER","FUNCTION"}.Contains(t.Text,StringComparer.OrdinalIgnoreCase));
                if(!r.ExistingArguments && plain && proc.Parameters.All(p=>!p.Unsupported))
                {
                    var sql=new StringBuilder();var variables=new HashSet<string>(SqlAnalysis.Tokens(r.Sql).Where(t=>t.TokenType==TSqlTokenType.Variable).Select(t=>t.Text),StringComparer.OrdinalIgnoreCase);
                    var values=new Dictionary<string,string>();string nl=r.Sql.Contains("\r\n")?"\r\n":"\n";
                    foreach(var p in proc.Parameters.Where(p=>p.Output || p.Table))
                    {
                        string variable="@ja_"+p.Name.TrimStart('@');if(variable.Length>115)variable=variable.Substring(0,115);string stem=variable;int number=2;while(!variables.Add(variable))variable=stem+"_"+(number++);
                        values[p.Name]=variable;sql.Append(PreferenceStore.Keyword("DECLARE")).Append(' ').Append(variable).Append(' ').Append(p.Type).Append(';').Append(nl);
                        if(p.Table)sql.Append("-- Popola ").Append(variable).Append(" prima della chiamata (parametro tabellare).").Append(nl);
                    }
                    sql.Append(r.Header).Append(name);item.Fields=new List<SnippetField>();
                    for(int n=0;n<proc.Parameters.Count;n++)
                    {
                        var p=proc.Parameters[n];sql.Append(n==0?nl+"    ":","+nl+"    ").Append(p.Name).Append(" = ");
                        if(values.ContainsKey(p.Name))sql.Append(values[p.Name]);
                        else {int at=sql.Length;sql.Append("NULL");item.Fields.Add(new SnippetField{Name=p.Name,Start=at,Length=4});}
                        if(p.Output)sql.Append(' ').Append(PreferenceStore.Keyword("OUTPUT"));
                    }
                    item.CompletionText=sql.ToString();item.CaretOffset=item.Fields.Count>0?(int?)item.Fields[0].Start:null;
                }
                else if(!r.ExistingArguments)item.DisplayText+=" · solo nome: completa i parametri manualmente";
                result.Add(item);
            }
            return result;
        }
    }
}
