using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    internal static class IncrementalSql
    {
        static readonly object sync=new object();
        static string previous="";
        static IList<TSqlParserToken> previousTokens=new List<TSqlParserToken>();
        static int safeEnd;
        internal static long LexedCharacters,ReusedTokens,ParsedCharacters;
        static readonly AnalysisCache<ScopeIndex> segments=new AnalysisCache<ScopeIndex>(64);
        internal static void Clear(){lock(sync){previous="";previousTokens=new List<TSqlParserToken>();safeEnd=0;LexedCharacters=ReusedTokens=ParsedCharacters=0;segments.Clear();}}
        internal static IList<TSqlParserToken> Tokens(string sql)
        {
            lock(sync)
            {
                int common=0;while(common<Math.Min(sql.Length,previous.Length) && sql[common]==previous[common])common++;
                common=Math.Min(common,safeEnd);
                int index=0;
                while(index<previousTokens.Count && previousTokens[index].Offset+(previousTokens[index].Text??"").Length<common && previousTokens[index].TokenType!=TSqlTokenType.EndOfFile)index++;
                index=Math.Max(0,index-1); // Relex a complete preceding token: edits can merge tokens.
                int offset=index<previousTokens.Count?previousTokens[index].Offset:0;
                if(offset>common){index=0;offset=0;}
                int line=index<previousTokens.Count?previousTokens[index].Line:1,column=index<previousTokens.Count?previousTokens[index].Column:1;
                IList<ParseError> errors;
                var suffix=new TSql160Parser(true).GetTokenStream(new StringReader(sql.Substring(offset)),out errors,offset,line,column);
                var result=previousTokens.Take(index).Concat(suffix).ToList();
                LexedCharacters+=sql.Length-offset;ReusedTokens+=index;
                previous=sql;previousTokens=result;safeEnd=errors.Count==0?sql.Length:Math.Max(0,errors.Min(e=>e.Offset));
                return new ReadOnlyCollection<TSqlParserToken>(result);
            }
        }
        internal static ScopeIndex Parse(string sql,Func<string,int,int,int,ScopeIndex> parse)
        {
            var tokens=SqlAnalysis.Tokens(sql);var result=new ScopeIndex();
            // QUOTED_IDENTIFIER changes parser state across batches. Keep the host parser's full semantics.
            if(tokens.Any(t=>SqlAnalysis.Significant(t) && string.Equals(t.Text,"QUOTED_IDENTIFIER",StringComparison.OrdinalIgnoreCase)))
            {System.Threading.Interlocked.Add(ref ParsedCharacters,sql.Length);return parse(sql,0,1,1);}
            bool independent=!tokens.Any(t=>SqlAnalysis.Significant(t) && new[]{"BEGIN","END","IF","WHILE","CREATE","ALTER"}.Contains(t.Text,StringComparer.OrdinalIgnoreCase));
            int start=0,line=1,column=1,depth=0;string first=null;
            Action<int> add=end=>{
                if(end<=start)return;
                string text=sql.Substring(start,end-start);
                string key=start+":"+line+":"+column+":"+text;
                int offset=start,initialLine=line,initialColumn=column;
                var part=segments.Get(key,()=>{System.Threading.Interlocked.Add(ref ParsedCharacters,text.Length);return parse(text,offset,initialLine,initialColumn);});
                result.Scopes.AddRange(part.Scopes);result.Joins.AddRange(part.Joins);
            };
            foreach(var token in tokens)
            {
                if(token.Offset<start)continue;
                if(token.TokenType==TSqlTokenType.Go)
                {
                    add(token.Offset);
                    int end=sql.IndexOf('\n',token.Offset);start=end<0?sql.Length:end+1;line=token.Line+1;column=1;depth=0;first=null;continue;
                }
                if(!SqlAnalysis.Significant(token))continue;
                if(first==null)first=token.Text.ToUpperInvariant();
                if(token.TokenType==TSqlTokenType.LeftParenthesis)depth++;
                else if(token.TokenType==TSqlTokenType.RightParenthesis)depth--;
                else if(independent && token.TokenType==TSqlTokenType.Semicolon && depth==0 && (first=="SELECT" || first=="WITH"))
                {
                    int end=token.Offset+token.Text.Length;add(end);start=end;line=token.Line;column=token.Column+token.Text.Length;first=null;
                }
            }
            add(sql.Length);return result;
        }
    }
}
