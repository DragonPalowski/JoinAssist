using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    public enum PersonalRuleKind { SelectTable, Snippet }
    public enum RuleOrder { None, FirstColumn, NamedColumn }
    public sealed class PersonalRule
    {
        public string Id {get;set;} = Guid.NewGuid().ToString("N");
        public string Name {get;set;} = "Nuova regola";
        public bool Enabled {get;set;} = true;
        public PersonalRuleKind Kind {get;set;}
        public string Server {get;set;} = "";
        public string Database {get;set;} = "";
        public string Schema {get;set;} = "dbo";
        public string Table {get;set;} = "";
        public int Top {get;set;} = 100;
        public RuleOrder Order {get;set;} = RuleOrder.FirstColumn;
        public string OrderColumn {get;set;} = "";
        public bool Descending {get;set;} = true;
        public bool AutoOnSpace {get;set;}
        public string Shortcut {get;set;} = "";
        public string Sql {get;set;} = "";
        public PersonalRule Copy(){return (PersonalRule)MemberwiseClone();}
        public override string ToString(){return (Enabled?"":"[Disattivata] ")+Name+" · "+(Kind==PersonalRuleKind.Snippet?Shortcut:Schema+"."+Table)+(AutoOnSpace?" · Spazio":" · Tab");}
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(Id) || Id.Length>64 || string.IsNullOrWhiteSpace(Name) || Name.Length>100)throw new ArgumentException("Specifica un nome regola (massimo 100 caratteri).");
            if(!Enum.IsDefined(typeof(PersonalRuleKind),Kind)||!Enum.IsDefined(typeof(RuleOrder),Order))throw new ArgumentException("Tipo di regola o ordinamento non valido.");
            if((Server??"").Length>256 || (Database??"").Length>128)throw new ArgumentException("Ambito server/database troppo lungo.");
            if(Kind==PersonalRuleKind.SelectTable)
            {
                if(string.IsNullOrWhiteSpace(Schema)||Schema.Length>128 || string.IsNullOrWhiteSpace(Table)||Table.Length>128)throw new ArgumentException("Specifica schema e tabella in campi separati (massimo 128 caratteri ciascuno).");
                if(Top<0 || Top>1000000)throw new ArgumentException("TOP deve essere compreso tra 0 (nessun limite) e 1.000.000.");
                if(Top==0 && Order==RuleOrder.None)throw new ArgumentException("Scegli almeno TOP o un ordinamento.");
                if(Order==RuleOrder.NamedColumn && (string.IsNullOrWhiteSpace(OrderColumn)||OrderColumn.Length>128))throw new ArgumentException("Specifica la colonna di ordinamento.");
            }
            else
            {
                if(string.IsNullOrWhiteSpace(Shortcut)||Shortcut.Length>64 || !char.IsLetter(Shortcut[0]) || !Shortcut.All(c=>char.IsLetterOrDigit(c)||c=='_'))throw new ArgumentException("La scorciatoia deve iniziare con una lettera e contenere solo lettere, numeri o underscore (massimo 64).");
                var token=SqlAnalysis.Tokens(Shortcut).Where(SqlAnalysis.Significant).ToList();
                if(token.Count!=1 || token[0].TokenType!=TSqlTokenType.Identifier)throw new ArgumentException("Scegli una scorciatoia diversa da una parola riservata SQL (ad esempio ultimilog).");
                if(string.IsNullOrWhiteSpace(Sql)||Sql.Length>8192)throw new ArgumentException("Scrivi il testo SQL della scorciatoia (massimo 8.192 caratteri).");
                if(Sql.IndexOf("$cursor$",StringComparison.Ordinal)!=Sql.LastIndexOf("$cursor$",StringComparison.Ordinal))throw new ArgumentException("Usa al massimo un segnaposto $cursor$.");
            }
        }
    }
    public static class PersonalRules
    {
        sealed class Parsed {public TSqlScript Script;public IList<ParseError> Errors;}
        static readonly AnalysisCache<Parsed> parses=new AnalysisCache<Parsed>(2);
        internal static void ClearCache(){parses.Clear();}
        public static void Validate(IEnumerable<PersonalRule> rules)
        {
            var list=(rules??Enumerable.Empty<PersonalRule>()).ToList();
            if(list.Count>50 || list.Any(r=>r==null) || list.Select(r=>r.Id).Distinct(StringComparer.Ordinal).Count()!=list.Count)throw new ArgumentException("Massimo 50 regole, con identificativi distinti.");
            foreach(var rule in list)rule.Validate();
            if(list.Sum(r=>(long)(r.Sql??"").Length)>131072)throw new ArgumentException("Il testo totale delle scorciatoie supera 128 KiB di caratteri.");
        }
        public static string Export(IEnumerable<PersonalRule> rules)
        {Validate(rules);return new JavaScriptSerializer().Serialize(rules.ToList());}
        public static List<PersonalRule> Import(string json)
        {
            if(json==null || json.Length>1048576)throw new ArgumentException("File regole troppo grande (massimo 1 MiB).");
            var list=new JavaScriptSerializer().Deserialize<List<PersonalRule>>(json);if(list==null)throw new ArgumentException("File regole non valido.");
            Validate(list);
            foreach(var rule in list){rule.Id=Guid.NewGuid().ToString("N");rule.Enabled=false;rule.AutoOnSpace=false;}
            return list;
        }
        static bool Scope(PersonalRule r,string server,string database)
        {return (string.IsNullOrWhiteSpace(r.Server)||string.Equals(r.Server,server,StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(r.Database)||string.Equals(r.Database,database,StringComparison.Ordinal));}
        static int Specificity(PersonalRule r){return (string.IsNullOrWhiteSpace(r.Server)?0:1)+(string.IsNullOrWhiteSpace(r.Database)?0:1);}
        static List<PersonalRule> Best(IEnumerable<PersonalRule> rules)
        {var list=rules.ToList();return list.Count==0?list:list.Where(r=>Specificity(r)==list.Max(Specificity)).ToList();}
        static string Stamp(PersonalRule r){return new JavaScriptSerializer().Serialize(r);}
        public static bool NeedsConnection(AutomaticTrigger trigger)
        {return trigger.RuleTableName!=null || PreferenceStore.Read.Rules.Any(r=>r.Enabled && r.Kind==PersonalRuleKind.Snippet && r.Shortcut.StartsWith(trigger.Prefix.LastOrDefault()??"",StringComparison.OrdinalIgnoreCase) && (!string.IsNullOrWhiteSpace(r.Server)||!string.IsNullOrWhiteSpace(r.Database)));}
        public static AutomaticTrigger Trigger(string sql,int caret)
        {
            var rules=PreferenceStore.Read.Rules.Where(r=>r.Enabled).ToList();if(rules.Count==0)return null;
            var tokens=SqlAnalysis.Tokens(sql).Where(SqlAnalysis.Significant).ToList();
            var word=tokens.LastOrDefault(t=>t.Offset<caret && t.Offset+t.Text.Length<=caret && t.TokenType==TSqlTokenType.Identifier);
            if(word!=null && word.Text.All(c=>char.IsLetterOrDigit(c)||c=='_') && sql.Substring(word.Offset+word.Text.Length,caret-word.Offset-word.Text.Length).All(char.IsWhiteSpace))
            {
                var previous=tokens.LastOrDefault(t=>t.Offset<word.Offset);
                bool boundary=previous==null || previous.TokenType==TSqlTokenType.Semicolon || previous.TokenType==TSqlTokenType.Go;
                if(boundary && rules.Any(r=>r.Kind==PersonalRuleKind.Snippet && r.Shortcut.StartsWith(word.Text,StringComparison.OrdinalIgnoreCase)))
                    return new AutomaticTrigger{Mode=CompletionMode.PersonalRule,Prefix=new[]{word.Text},Caret=caret,RuleSql=sql,KeywordChoices=TypingTemplates.KeywordTrigger(sql,caret)?.KeywordChoices,
                        KeywordSpace=word.Offset+word.Text.Length==sql.Length || !char.IsWhiteSpace(sql[word.Offset+word.Text.Length]),
                        Context=new QueryContext{ReplaceStart=word.Offset,ReplaceLength=word.Text.Length},BaseKey="RULE-SNIPPET\0"+sql.Substring(0,word.Offset)+"\0"+sql.Substring(word.Offset+word.Text.Length)};
            }
            // After a complete table and whitespace, the ordinary name trigger has ended.
            if(caret==0 || !char.IsWhiteSpace(sql[caret-1]) || !rules.Any(r=>r.Kind==PersonalRuleKind.SelectTable))return null;
            SelectStatement statement;QuerySpecification query;NamedTableReference table;
            if(!Simple(sql,caret,out statement,out query,out table))return null;
            int end=table.SchemaObject.StartOffset+table.SchemaObject.FragmentLength;
            if(caret<=end || !sql.Substring(end,caret-end).All(char.IsWhiteSpace))return null;
            var ids=table.SchemaObject.Identifiers;if(ids.Count<1 || ids.Count>2)return null;
            string name=ids.Last().Value,schema=ids.Count==2?ids[0].Value:null;
            if(!rules.Any(r=>r.Kind==PersonalRuleKind.SelectTable && string.Equals(r.Table,name,StringComparison.OrdinalIgnoreCase) && (schema==null || string.Equals(r.Schema,schema,StringComparison.OrdinalIgnoreCase))))return null;
            SqlAnalysis.CheckDatabaseContext(sql,caret);
            return new AutomaticTrigger{Mode=CompletionMode.PersonalRule,Prefix=new string[0],Caret=caret,RuleSql=sql,RuleTableName=name,RuleTableSchema=schema,
                Context=new QueryContext{ReplaceStart=table.SchemaObject.StartOffset,ReplaceLength=table.SchemaObject.FragmentLength},
                BaseKey="RULE-TABLE\0"+sql.Substring(0,end)+"\0"+sql.Substring(caret)};
        }
        public static List<JoinProposal> Snippets(AutomaticTrigger trigger,string server,string database)
        {
            var result=new List<JoinProposal>();string prefix=trigger.Prefix.LastOrDefault()??"";
            foreach(var group in PreferenceStore.Read.Rules.Where(r=>r.Enabled && r.Kind==PersonalRuleKind.Snippet && Scope(r,server,database) && r.Shortcut.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).GroupBy(r=>r.Shortcut,StringComparer.OrdinalIgnoreCase))
                foreach(var rule in Best(group))
                {
                    int cursor=rule.Sql.IndexOf("$cursor$",StringComparison.Ordinal);string text=rule.Sql.Replace("$cursor$","");
                    var p=Proposal(rule,text,trigger.Context.ReplaceStart,trigger.Context.ReplaceLength);
                    p.SearchName=rule.Shortcut;p.CaretOffset=cursor<0?text.Length:cursor;p.RuleOffline=string.IsNullOrWhiteSpace(rule.Server)&&string.IsNullOrWhiteSpace(rule.Database);result.Add(p);
                }
            return result;
        }
        public static async Task<List<JoinProposal>> Tables(CatalogSnapshot catalog,AutomaticTrigger trigger,string server,string database,CancellationToken cancel,IList<JoinProposal> ordinary=null)
        {
            var result=new List<JoinProposal>();
            var rules=PreferenceStore.Read.Rules.Where(r=>r.Enabled && r.Kind==PersonalRuleKind.SelectTable && Scope(r,server,database));
            // Resolve before grouping so differently cased rule spellings still
            // share priority, without conflating distinct exact catalog objects.
            foreach(var group in rules.Select(r=>new{Rule=r,Object=catalog.Find(r.Schema,r.Table)}).Where(x=>x.Object!=null).GroupBy(x=>x.Object))
            {
                cancel.ThrowIfCancellationRequested();var obj=group.Key;if(obj.Kind!="U" && obj.Kind!="V")continue;
                string sqlName;
                if(trigger.Mode==CompletionMode.Table)
                {
                    var candidate=ordinary==null?null:ordinary.FirstOrDefault(p=>p.TargetSchema==obj.Schema && p.TargetTable==obj.Name);
                    if(candidate==null || !trigger.Matches(candidate))continue;sqlName=candidate.CompletionText;
                }
                else
                {
                    if(catalog.Find(trigger.RuleTableSchema,trigger.RuleTableName)!=obj)continue;
                    sqlName=trigger.RuleSql.Substring(trigger.Context.ReplaceStart,trigger.Context.ReplaceLength);
                }
                foreach(var rule in Best(group.Select(x=>x.Rule)))
                {
                    if(rule.Order==RuleOrder.NamedColumn)
                    {
                        await catalog.EnsureAsync(obj,cancel).ConfigureAwait(false);
                        if(obj.Columns.Count(c=>c.Name==rule.OrderColumn)!=1)continue;
                    }
                    var p=TableProposal(rule,trigger,sqlName);if(p==null)continue;
                    p.TargetTable=obj.Name;p.TargetSchema=obj.Schema;p.RuleTableSql=sqlName;p.SearchName=obj.Name;result.Add(p);
                }
            }
            return result;
        }
        static JoinProposal Proposal(PersonalRule r,string sql,int start,int length)
        {return new JoinProposal{IsPersonalRule=true,RuleId=r.Id,RuleStamp=Stamp(r),RuleAuto=r.AutoOnSpace,RuleReplaceStart=start,RuleReplaceLength=length,
            CompletionText=sql,RuleName=r.Name,ContextRank=-20,Category="Regola personale",SearchIdentity="rule\0"+r.Id,
            DisplayText=sql+"  |  "+r.Name+" · "+(string.IsNullOrWhiteSpace(r.Database)?"tutti i database":r.Database)+(r.AutoOnSpace?" · anche Spazio":" · Tab")};}
        public static JoinProposal TableProposal(PersonalRule rule,AutomaticTrigger trigger,string tableSql)
        {
            string original=trigger.RuleSql;if(original==null)return null;
            int offset=trigger.Context.ReplaceStart,length=trigger.Context.ReplaceLength;
            string changed=original.Remove(offset,length).Insert(offset,tableSql);
            SelectStatement statement;QuerySpecification query;NamedTableReference table;
            if(!Simple(changed,offset,out statement,out query,out table) || table.SchemaObject.StartOffset!=offset)return null;
            // Exact schema/name matching is established against the catalog before this method.
            int start=statement.StartOffset,end=statement.StartOffset+statement.FragmentLength;
            var edits=new List<Tuple<int,string>>();
            if(query.TopRowFilter==null && rule.Top>0)edits.Add(Tuple.Create(query.StartOffset+6," "+PreferenceStore.Keyword("TOP")+" "+rule.Top.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if(query.OrderByClause==null && rule.Order!=RuleOrder.None)
            {
                var last=SqlAnalysis.Tokens(changed).Where(SqlAnalysis.Significant).Last(t=>t.Offset>=start && t.Offset<end && t.TokenType!=TSqlTokenType.Semicolon);
                string column=rule.Order==RuleOrder.FirstColumn?"1":SqlName.Format(rule.OrderColumn);
                edits.Add(Tuple.Create(last.Offset+last.Text.Length," "+PreferenceStore.Keyword("ORDER BY")+" "+column+" "+PreferenceStore.Keyword(rule.Descending?"DESC":"ASC")));
            }
            if(edits.Count==0)return null;
            string text=changed.Substring(start,end-start);
            foreach(var edit in edits.OrderByDescending(e=>e.Item1))text=text.Insert(edit.Item1-start,edit.Item2);
            return Proposal(rule,text,start,end-(tableSql.Length-length)-start);
        }
        static bool Simple(string sql,int position,out SelectStatement statement,out QuerySpecification query,out NamedTableReference table)
        {
            statement=null;query=null;table=null;
            var parsed=parses.Get(sql,()=>{IList<ParseError> errors;var tree=new TSql160Parser(true).Parse(new StringReader(sql),out errors) as TSqlScript;return new Parsed{Script=tree,Errors=errors};});
            var script=parsed.Script;if(script==null)return false;
            foreach(var s in script.Batches.SelectMany(b=>b.Statements).OfType<SelectStatement>())
            {
                var q=s.QueryExpression as QuerySpecification;
                if(q==null || s.WithCtesAndXmlNamespaces!=null || s.Into!=null || q.FromClause==null || q.FromClause.TableReferences.Count!=1 || q.SelectElements.Count!=1 || !(q.SelectElements[0] is SelectStarExpression) || q.UniqueRowFilter!=UniqueRowFilter.NotSpecified || q.GroupByClause!=null || q.HavingClause!=null || q.ForClause!=null || s.OptimizerHints.Count>0)continue;
                var t=q.FromClause.TableReferences[0] as NamedTableReference;if(t==null || t.SchemaObject.Identifiers.Count>2)continue;
                int a=t.SchemaObject.StartOffset,b=a+t.SchemaObject.FragmentLength;
                if(position<a || position>b && (position>sql.Length || !sql.Substring(b,position-b).All(char.IsWhiteSpace)))continue;
                if(parsed.Errors.Any(e=>e.Offset>=s.StartOffset && e.Offset<=s.StartOffset+s.FragmentLength))continue;
                statement=s;query=q;table=t;return true;
            }
            return false;
        }
        public static bool Matches(AutomaticTrigger trigger,JoinProposal p)
        {
            var rule=PreferenceStore.Read.Rules.FirstOrDefault(r=>r.Id==p.RuleId && r.Enabled);if(rule==null || Stamp(rule)!=p.RuleStamp)return false;
            if(rule.Kind==PersonalRuleKind.Snippet)return trigger.Mode==CompletionMode.PersonalRule && trigger.RuleTableName==null && rule.Shortcut.StartsWith(trigger.Prefix.LastOrDefault()??"",StringComparison.OrdinalIgnoreCase);
            if(trigger.Mode==CompletionMode.PersonalRule)return string.Equals(trigger.RuleTableName,p.TargetTable,StringComparison.OrdinalIgnoreCase) && (trigger.RuleTableSchema==null || string.Equals(trigger.RuleTableSchema,p.TargetSchema,StringComparison.OrdinalIgnoreCase));
            return trigger.Mode==CompletionMode.Table && (trigger.Prefix.Length<2 || string.Equals(trigger.Prefix[0],p.TargetSchema,StringComparison.OrdinalIgnoreCase)) && NameMatching.Score(p.TargetTable,trigger.Prefix.LastOrDefault()??"")!=int.MaxValue;
        }
        public static bool Revalidate(AutomaticTrigger trigger,JoinProposal proposal,string server,string database)
        {
            if(!Matches(trigger,proposal))return false;
            var rule=PreferenceStore.Read.Rules.Single(r=>r.Id==proposal.RuleId);
            if(!Scope(rule,server,database))return false;
            JoinProposal fresh=rule.Kind==PersonalRuleKind.Snippet?Snippets(trigger,server,database).FirstOrDefault(p=>p.RuleId==rule.Id):TableProposal(rule,trigger,proposal.RuleTableSql);
            return fresh!=null && fresh.CompletionText==proposal.CompletionText && fresh.RuleReplaceStart==proposal.RuleReplaceStart && fresh.RuleReplaceLength==proposal.RuleReplaceLength;
        }
        public static string Preview(PersonalRule rule)
        {
            if(rule.Kind==PersonalRuleKind.Snippet)return rule.Sql??"";
            string prefix=PreferenceStore.Keyword("SELECT")+" * "+PreferenceStore.Keyword("FROM")+" ";
            string sql=prefix+SqlName.Format(rule.Schema??"dbo")+"."+SqlName.Format(string.IsNullOrEmpty(rule.Table)?"Tabella":rule.Table);
            var trigger=new AutomaticTrigger{RuleSql=sql,Context=new QueryContext{ReplaceStart=prefix.Length,ReplaceLength=sql.Length-prefix.Length}};
            return TableProposal(rule,trigger,sql.Substring(trigger.Context.ReplaceStart))?.CompletionText??sql;
        }
    }
}
