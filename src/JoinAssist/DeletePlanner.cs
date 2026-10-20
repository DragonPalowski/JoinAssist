using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace JoinAssist
{
    public sealed class DeleteRequest
    {
        public int Start,Length;
        public string Original,Schema,Table,Alias,Filter;
        public static DeleteRequest Create(string sql,int caret)
        {
            SqlAnalysis.CheckCaret(sql,caret);SqlAnalysis.CheckDatabaseContext(sql,caret);
            var tokens=SqlAnalysis.Tokens(sql).Where(SqlAnalysis.Significant).ToList();
            var top=new List<TSqlParserToken>();int depth=0;
            foreach(var token in tokens){if(depth==0)top.Add(token);if(token.TokenType==TSqlTokenType.LeftParenthesis)depth++;else if(token.TokenType==TSqlTokenType.RightParenthesis)depth=Math.Max(0,depth-1);}
            var heads=new[]{"SELECT","INSERT","UPDATE","DELETE","MERGE","GO",";","CREATE","ALTER","DROP","DECLARE","EXEC","EXECUTE","USE","PRINT","RETURN","TRUNCATE"};
            var head=top.LastOrDefault(t=>t.Offset<=caret && heads.Contains(t.Text,StringComparer.OrdinalIgnoreCase));
            // The caret may also be directly after the DELETE's semicolon.
            if(head!=null && head.TokenType==TSqlTokenType.Semicolon && sql.Substring(head.Offset+1,caret-head.Offset-1).All(char.IsWhiteSpace))
                head=top.LastOrDefault(t=>t.Offset<head.Offset && heads.Contains(t.Text,StringComparer.OrdinalIgnoreCase));
            if(head==null || head.TokenType!=TSqlTokenType.Delete)throw new InvalidOperationException("Posiziona il cursore in una DELETE completa con WHERE.");
            var history=tokens.Where(t=>t.Offset<head.Offset).ToList();
            if(history.Any(t=>string.Equals(t.Text,"REVERT",StringComparison.OrdinalIgnoreCase) || string.Equals(t.Text,"SETUSER",StringComparison.OrdinalIgnoreCase)) ||
                history.Where((t,i)=>i+1<history.Count && (t.TokenType==TSqlTokenType.Execute || string.Equals(t.Text,"EXEC",StringComparison.OrdinalIgnoreCase)) && history[i+1].TokenType==TSqlTokenType.As).Any())
                throw new InvalidOperationException("Usa una scheda senza EXECUTE AS/REVERT/SETUSER: la connessione metadati non replica quel contesto di esecuzione.");
            int begin=head.Offset,end=sql.Length;
            foreach(var token in top.Where(t=>t.Offset>head.Offset))
                if(heads.Contains(token.Text,StringComparer.OrdinalIgnoreCase)) {end=token.Offset+(token.TokenType==TSqlTokenType.Semicolon?1:0);break;}
            if(caret>end && !sql.Substring(end,caret-end).All(char.IsWhiteSpace))throw new InvalidOperationException("Posiziona il cursore dentro la DELETE da preparare.");
            foreach(var token in top.Where(t=>t.Offset<head.Offset).Reverse())
            {if(heads.Contains(token.Text,StringComparer.OrdinalIgnoreCase))break;if(token.TokenType==TSqlTokenType.With){begin=token.Offset;break;}}
            string candidate=sql.Substring(begin,end-begin);IList<ParseError> errors;
            var script=new TSql160Parser(true).Parse(new StringReader(candidate),out errors) as TSqlScript;
            var statements=script==null?new List<TSqlStatement>():script.Batches.SelectMany(b=>b.Statements).ToList();
            var statement=statements.Count==1?statements[0] as DeleteStatement:null;
            if(errors.Count!=0 || statement==null)throw new InvalidOperationException("Completa la DELETE e il suo WHERE prima di preparare le dipendenze.");
            var spec=statement.DeleteSpecification;
            if(statement.WithCtesAndXmlNamespaces!=null || statement.OptimizerHints.Count!=0 || spec.TopRowFilter!=null || spec.OutputClause!=null || spec.OutputIntoClause!=null)
                throw new InvalidOperationException("Questa versione richiede una DELETE semplice: senza CTE, TOP, OUTPUT o OPTION.");
            if(spec.WhereClause==null || spec.WhereClause.SearchCondition==null)throw new InvalidOperationException("È richiesto un WHERE completo. Non vengono preparate DELETE senza filtro.");
            var target=spec.Target as NamedTableReference;
            var source=target;
            if(spec.FromClause!=null)
            {
                source=spec.FromClause.TableReferences.Count==1?spec.FromClause.TableReferences[0] as NamedTableReference:null;
                if(source==null || target==null)throw new InvalidOperationException("Le DELETE con JOIN o più sorgenti non sono ancora supportate dal piano di cancellazione.");
                string alias=source.Alias?.Value;
                var ids=target.SchemaObject.Identifiers;
                // Compare decoded identifier lists rather than ScriptDOM's object display string.
                bool bound=ids.Count==1 && string.Equals(ids[0].Value,alias??source.SchemaObject.BaseIdentifier.Value,StringComparison.OrdinalIgnoreCase) ||
                    ids.Select(i=>i.Value).SequenceEqual(source.SchemaObject.Identifiers.Select(i=>i.Value),StringComparer.OrdinalIgnoreCase);
                if(!bound)throw new InvalidOperationException("La tabella da eliminare non coincide con l'unica sorgente FROM.");
            }
            if(source==null || source.SchemaObject.Identifiers.Count>2 || source.TableHints.Count!=0 || source.SchemaObject.BaseIdentifier.Value.StartsWith("#",StringComparison.Ordinal))
                throw new InvalidOperationException("Usa una tabella persistente del database corrente, senza hint o riferimenti ad altri database.");
            int start=begin+statement.StartOffset,length=statement.FragmentLength;
            var filter=spec.WhereClause.SearchCondition;
            string predicate=candidate.Substring(filter.StartOffset,filter.FragmentLength);
            if(SqlAnalysis.Tokens(predicate).Any(t=>t.Text!=null && (t.Text.StartsWith("@@",StringComparison.Ordinal) || new[]{"XACT_STATE","CURRENT_TRANSACTION_ID","SCOPE_IDENTITY"}.Contains(t.Text,StringComparer.OrdinalIgnoreCase))))
                throw new InvalidOperationException("Il WHERE usa lo stato della sessione/transazione. Sostituiscilo con un parametro esplicito prima di preparare il piano.");
            return new DeleteRequest{Start=start,Length=length,Original=sql.Substring(start,length),Schema=source.SchemaObject.SchemaIdentifier?.Value,
                Table=source.SchemaObject.BaseIdentifier.Value,Alias=source.Alias?.Value,Filter=predicate};
        }
    }
    public sealed class DeleteTable
    {
        public int Id;
        public string Schema,Name;
        public bool BareAllowed,Temporal,Triggers,SecurityPolicy,MemoryOptimized;
        public readonly List<string> Columns=new List<string>(),PrimaryKey=new List<string>();
        public readonly HashSet<string> RowVersions=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string Sql {get{return SqlName.Quote(Schema)+"."+SqlName.Quote(Name);}}
    }
    public sealed class DeleteLink
    {
        public ForeignKey Key;
        // SQL Server: 0 NO ACTION, 1 CASCADE, 2 SET NULL, 3 SET DEFAULT.
        public int Action;
    }
    public sealed class DeleteSchema
    {
        public readonly List<DeleteTable> Tables=new List<DeleteTable>();
        public readonly List<DeleteLink> Links=new List<DeleteLink>();
    }
    public sealed class DeleteStep
    {
        public DeleteTable Table;
        public bool Explicit;
        public string Relationships;
        public string Caption {get{return Table.Sql+" · "+(Explicit?"DELETE esplicita":"gestita da CASCADE")+" · "+Relationships;}}
    }
    public sealed class DeletePlan
    {
        public string Script;
        public readonly List<DeleteStep> Steps=new List<DeleteStep>();
        public readonly List<string> Problems=new List<string>(),Notices=new List<string>();
        public bool Ready {get{return Problems.Count==0 && !string.IsNullOrEmpty(Script);}}
    }
    public static class DeletePlanner
    {
        public static async Task<DeletePlan> LoadAsync(DbConnection connection,DeleteRequest request,CancellationToken cancel)
        {
            // Deliberately fresh and database-wide: cached/focused completion metadata is insufficient here.
            if(connection.State!=ConnectionState.Open)await connection.OpenAsync(cancel).ConfigureAwait(false);
            using(var cmd=connection.CreateCommand())
            {
                cmd.CommandTimeout=10;cmd.CommandText="SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION') AS ja_delete_permission;";
                var permission=await cmd.ExecuteScalarAsync(cancel).ConfigureAwait(false);
                if(permission==null || permission==DBNull.Value || Convert.ToInt32(permission)!=1)
                    throw new InvalidOperationException("Il piano richiede VIEW DEFINITION sul database per leggere le dipendenze. Nessun permesso viene modificato dal plugin.");
            }
            var schema=new DeleteSchema();var tables=new Dictionary<int,DeleteTable>();int total=0;
            using(var cmd=connection.CreateCommand())
            {
                cmd.CommandTimeout=10;cmd.CommandText=@"SELECT t.object_id AS ja_delete_table,s.name,t.name,
CONVERT(bit,CASE WHEN OBJECT_ID(QUOTENAME(t.name))=t.object_id THEN 1 ELSE 0 END),t.temporal_type,
CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM sys.triggers tr WHERE tr.parent_id=t.object_id AND tr.is_disabled=0) THEN 1 ELSE 0 END),
CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM sys.security_predicates sp JOIN sys.security_policies po ON po.object_id=sp.object_id WHERE sp.target_object_id=t.object_id AND po.is_enabled=1) THEN 1 ELSE 0 END),
c.name,CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.indexes ix ON ix.object_id=ic.object_id AND ix.index_id=ic.index_id WHERE ic.object_id=c.object_id AND ic.column_id=c.column_id AND ic.key_ordinal>0 AND ix.is_primary_key=1) THEN 1 ELSE 0 END),c.system_type_id,t.is_memory_optimized
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id
WHERE t.is_ms_shipped=0 AND c.is_hidden=0 ORDER BY t.object_id,c.column_id;";
                using(var rows=await cmd.ExecuteReaderAsync(cancel).ConfigureAwait(false))
                    while(await rows.ReadAsync(cancel).ConfigureAwait(false))
                    {
                        if(++total>100000)throw new InvalidOperationException("Catalogo troppo grande per il piano di cancellazione (100.000 colonne).");
                        int id=rows.GetInt32(0);DeleteTable table;
                        if(!tables.TryGetValue(id,out table))
                        {
                            table=new DeleteTable{Id=id,Schema=rows.GetString(1),Name=rows.GetString(2),BareAllowed=rows.GetBoolean(3),Temporal=Convert.ToInt32(rows.GetValue(4))!=0,Triggers=rows.GetBoolean(5),SecurityPolicy=rows.GetBoolean(6),MemoryOptimized=rows.GetBoolean(10)};
                            tables.Add(id,table);schema.Tables.Add(table);
                        }
                        table.Columns.Add(rows.GetString(7));if(rows.GetBoolean(8))table.PrimaryKey.Add(rows.GetString(7));
                        if(Convert.ToInt32(rows.GetValue(9))==189)table.RowVersions.Add(rows.GetString(7));
                    }
            }
            var links=new Dictionary<int,DeleteLink>();total=0;
            using(var cmd=connection.CreateCommand())
            {
                cmd.CommandTimeout=10;cmd.CommandText=@"SELECT fk.object_id AS ja_delete_fk,fk.name,fk.parent_object_id,fk.referenced_object_id,fk.is_disabled,fk.is_not_trusted,fk.delete_referential_action,
fkc.constraint_column_id,cc.name,pc.name FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
JOIN sys.columns cc ON cc.object_id=fkc.parent_object_id AND cc.column_id=fkc.parent_column_id
JOIN sys.columns pc ON pc.object_id=fkc.referenced_object_id AND pc.column_id=fkc.referenced_column_id
ORDER BY fk.object_id,fkc.constraint_column_id;";
                using(var rows=await cmd.ExecuteReaderAsync(cancel).ConfigureAwait(false))
                    while(await rows.ReadAsync(cancel).ConfigureAwait(false))
                    {
                        if(++total>50000)throw new InvalidOperationException("Troppe colonne FK per il piano di cancellazione.");
                        int id=rows.GetInt32(0);DeleteLink link;
                        if(!links.TryGetValue(id,out link))
                        {
                            link=new DeleteLink{Action=Convert.ToInt32(rows.GetValue(6)),Key=new ForeignKey{Id=id,Name=rows.GetString(1),ChildId=rows.GetInt32(2),ParentId=rows.GetInt32(3),Disabled=rows.GetBoolean(4),Untrusted=rows.GetBoolean(5)}};
                            links.Add(id,link);schema.Links.Add(link);
                        }
                        link.Key.Columns.Add(new KeyColumn{Ordinal=rows.GetInt32(7),Child=rows.GetString(8),Parent=rows.GetString(9)});
                    }
            }
            cancel.ThrowIfCancellationRequested();return Build(request,schema);
        }
        public static DeletePlan Build(DeleteRequest request,DeleteSchema schema)
        {
            var plan=new DeletePlan();
            var candidates=schema.Tables.Where(t=>string.Equals(t.Name,request.Table,StringComparison.OrdinalIgnoreCase) && (request.Schema==null?t.BareAllowed:string.Equals(t.Schema,request.Schema,StringComparison.OrdinalIgnoreCase))).ToList();
            var exact=candidates.Where(t=>t.Name==request.Table && (request.Schema==null || t.Schema==request.Schema)).ToList();
            if(exact.Count==1)candidates=exact;
            if(candidates.Count!=1){plan.Problems.Add("Tabella principale non visibile o non risolvibile senza ambiguità nel database corrente.");return plan;}
            var root=candidates[0];var byId=schema.Tables.ToDictionary(t=>t.Id);
            var selected=new HashSet<int>{root.Id};var queue=new Queue<int>();queue.Enqueue(root.Id);
            while(queue.Count>0)
            {
                int parent=queue.Dequeue();
                foreach(var link in schema.Links.Where(l=>l.Key.ParentId==parent))
                {
                    if(link.Key.Disabled){plan.Notices.Add("FK disabilitata, non seguita: "+Clean(link.Key.Name));continue;}
                    if(link.Action<0 || link.Action>3){plan.Problems.Add("Azione FK non riconosciuta: "+Clean(link.Key.Name));continue;}
                    DeleteTable child;
                    if(!byId.TryGetValue(link.Key.ChildId,out child)){plan.Problems.Add("Tabella figlia non visibile per "+Clean(link.Key.Name));continue;}
                    if(link.Action>=2)
                    {
                        plan.Notices.Add(Clean(link.Key.Name)+": "+(link.Action==2?"SET NULL":"SET DEFAULT")+" gestito dal database; non aggiunge righe da eliminare in "+child.Sql+".");
                        if(child.Triggers || child.SecurityPolicy || child.Temporal || child.MemoryOptimized)plan.Problems.Add("Azione automatica su tabella non supportata (temporale, memory-optimized, trigger o policy di sicurezza): "+child.Sql);
                        continue;
                    }
                    if(selected.Add(child.Id))queue.Enqueue(child.Id);
                    if(selected.Count>64){plan.Problems.Add("Più di 64 tabelle raggiungibili: piano non generato.");return plan;}
                }
            }
            var relevant=schema.Links.Where(l=>!l.Key.Disabled && selected.Contains(l.Key.ParentId) && selected.Contains(l.Key.ChildId)).ToList();
            if(relevant.Count>256){plan.Problems.Add("Più di 256 collegamenti tra le tabelle coinvolte: piano non generato.");return plan;}
            foreach(int id in selected)
            {
                var table=byId[id];
                if(table.PrimaryKey.Count==0)plan.Problems.Add("Chiave primaria necessaria per fissare le righe: "+table.Sql);
                if(table.Columns.GroupBy(c=>c,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))plan.Problems.Add("Colonne distinte solo per maiuscole/minuscole non supportate nelle tabelle di appoggio: "+table.Sql);
                if(table.Temporal)plan.Problems.Add("Tabella temporale/history non supportata: "+table.Sql);
                if(table.MemoryOptimized)plan.Problems.Add("Tabella memory-optimized non supportata dai lock del piano: "+table.Sql);
                if(table.Triggers)plan.Problems.Add("Trigger DML abilitato: comportamento non ricostruibile dalle sole FK in "+table.Sql);
                if(table.SecurityPolicy)plan.Problems.Add("Policy di sicurezza abilitata: insieme delle righe non verificabile in "+table.Sql);
            }
            foreach(var link in relevant)
            {
                var key=link.Key;
                if(key.Columns.Count==0 || key.Columns.Select(c=>c.Ordinal).Distinct().Count()!=key.Columns.Count ||
                    key.Columns.Any(c=>!byId[key.ChildId].Columns.Contains(c.Child,StringComparer.OrdinalIgnoreCase) || !byId[key.ParentId].Columns.Contains(c.Parent,StringComparer.OrdinalIgnoreCase)))
                    plan.Problems.Add("Colonne FK non risolvibili: "+Clean(key.Name));
                if(key.Untrusted)plan.Notices.Add("FK abilitata ma non trusted, inclusa nel piano: "+Clean(key.Name));
            }
            // All actions between selected tables constrain ordering, even SET DEFAULT that could change a key.
            var next=selected.ToDictionary(id=>id,id=>new HashSet<int>(relevant.Where(l=>l.Key.ParentId==id).Select(l=>l.Key.ChildId)));
            var indegree=selected.ToDictionary(id=>id,id=>next.Values.Count(children=>children.Contains(id)));
            var order=new List<int>();
            while(order.Count<selected.Count)
            {
                var ready=selected.Where(id=>indegree[id]==0 && !order.Contains(id)).OrderBy(id=>byId[id].Sql,StringComparer.Ordinal).ToList();
                if(ready.Count==0){plan.Problems.Add("Ciclo o relazione verso la stessa tabella: "+string.Join(" → ",selected.Where(id=>!order.Contains(id)).Select(id=>byId[id].Sql))+". Nessuna sequenza automatica generata.");break;}
                foreach(int id in ready){order.Add(id);foreach(int child in next[id])indegree[child]--;}
            }
            var scope=relevant.Where(l=>l.Action<=1).ToList();
            foreach(int id in order.AsEnumerable().Reverse())
            {
                var incoming=scope.Where(l=>l.Key.ChildId==id).ToList();
                plan.Steps.Add(new DeleteStep{Table=byId[id],Explicit=id==root.Id || incoming.Any(l=>l.Action==0),Relationships=id==root.Id?"filtro originale":string.Join(", ",incoming.Select(l=>Clean(l.Key.Name)))});
            }
            if(plan.Problems.Count==0)plan.Script=Script(request,root,order,byId,scope,plan);
            return plan;
        }
        static string Script(DeleteRequest request,DeleteTable root,List<int> order,Dictionary<int,DeleteTable> tables,List<DeleteLink> links,DeletePlan plan)
        {
            string tag=Guid.NewGuid().ToString("N").Substring(0,12),variable="@ja_xact_"+tag;
            var temp=order.ToDictionary(id=>id,id=>"#JA_Delete_"+tag+"_"+order.IndexOf(id));
            var text=new StringBuilder();
            Action<string> line=value=>text.Append(value).Append("\r\n");
            line("-- JoinAssist · piano DELETE basato sulle FK visibili. Controllare prima di eseguire.");
            line("-- PROVA: il ROLLBACK finale annulla le cancellazioni. Sostituirlo con COMMIT solo dopo verifica.");
            line("-- Nessun conteggio di righe è stato eseguito dal plugin. I parametri originali devono essere disponibili.");
            foreach(string notice in plan.Notices.Distinct())line("-- "+Clean(notice));
            // RAISERROR + RETURN deliberately avoids THROW/XACT_ABORT rolling back a caller's transaction.
            line("IF @@TRANCOUNT <> 0");line("BEGIN");line("    RAISERROR(N'Eseguire il piano senza una transazione già aperta.', 16, 1);");line("    RETURN;");line("END;");
            line("IF (2 & @@OPTIONS) = 2");line("BEGIN");line("    RAISERROR(N'Disattivare IMPLICIT_TRANSACTIONS prima di eseguire il piano.', 16, 1);");line("    RETURN;");line("END;");
            line("SET ROWCOUNT 0; -- Nessun limite implicito sulle righe; questa impostazione resta nella sessione.");
            line("DECLARE "+variable+" bit = CASE WHEN (16384 & @@OPTIONS) = 16384 THEN 1 ELSE 0 END;");
            line("SET XACT_ABORT ON;");line("BEGIN TRY");line("    BEGIN TRANSACTION;");
            line("    -- Fissare tutti gli insiemi prima di eliminare righe; mantenere i lock fino alla fine.");
            foreach(int id in order)
            {
                var table=tables[id];
                var columns=table.PrimaryKey.Concat(links.Where(l=>l.Key.ParentId==id).SelectMany(l=>l.Key.Columns.Select(c=>c.Parent))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                string alias=id==root.Id?request.Alias??root.Name:"ja_source";
                line("    SELECT "+string.Join(", ",columns.Select(c=>table.RowVersions.Contains(c)?"CONVERT(binary(8), "+Q(alias)+"."+Q(c)+") AS "+Q(c):Q(alias)+"."+Q(c)))+" INTO "+Q(temp[id]));
                line("    FROM "+table.Sql+(id==root.Id && request.Alias==null?"":" AS "+Q(alias))+" WITH (UPDLOCK, HOLDLOCK)");
                if(id==root.Id){line("    WHERE "+request.Filter);line("    ;");}
                else
                {
                    var incoming=links.Where(l=>l.Key.ChildId==id).ToList();
                    line("    WHERE "+string.Join("\r\n       OR ",incoming.Select(l=>"EXISTS (SELECT 1 FROM "+Q(temp[l.Key.ParentId])+" AS [ja_parent] WHERE "+string.Join(" AND ",l.Key.Columns.OrderBy(c=>c.Ordinal).Select(c=>"[ja_source]."+Q(c.Child)+" = [ja_parent]."+Q(c.Parent)))+")"))+";");
                }
            }
            line("");line("    -- Eliminare prima i figli; CASCADE resta una responsabilità del database.");
            foreach(var step in plan.Steps)
            {
                var table=step.Table;
                if(!step.Explicit){line("    -- "+Clean(table.Sql)+": righe gestite da CASCADE.");continue;}
                line("    DELETE [ja_delete] FROM "+table.Sql+" AS [ja_delete]");
                line("    WHERE EXISTS (SELECT 1 FROM "+Q(temp[table.Id])+" AS [ja_scope] WHERE "+string.Join(" AND ",table.PrimaryKey.Select(c=>"[ja_delete]."+Q(c)+" = [ja_scope]."+Q(c)))+");");
                line("    SELECT N'"+table.Sql.Replace("'","''")+"' AS [Tabella], @@ROWCOUNT AS [Righe eliminate nella prova];");
            }
            line("");line("    ROLLBACK TRANSACTION; -- PROVA: sostituire con COMMIT TRANSACTION solo dopo verifica.");
            foreach(int id in order)line("    IF OBJECT_ID(N'tempdb.."+temp[id]+"') IS NOT NULL DROP TABLE "+Q(temp[id])+";");
            line("    IF "+variable+" = 0 SET XACT_ABORT OFF;");line("END TRY");line("BEGIN CATCH");line("    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;");
            foreach(int id in order)line("    IF OBJECT_ID(N'tempdb.."+temp[id]+"') IS NOT NULL DROP TABLE "+Q(temp[id])+";");
            line("    IF "+variable+" = 0 SET XACT_ABORT OFF;");line("    THROW;");line("END CATCH;");
            return text.ToString();
        }
        static string Q(string value){return SqlName.Quote(value);}
        static string Clean(string value){return new string((value??"").Select(c=>char.IsControl(c)||char.IsWhiteSpace(c)?' ':c).ToArray());}
    }
}
