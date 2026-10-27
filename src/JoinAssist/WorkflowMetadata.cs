using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JoinAssist
{
    public sealed class RelationStep
    {
        public ForeignKey Key;
        public bool ToParent;
        public int From
        {
            get
            {
                return ToParent ? Key.ChildId : Key.ParentId;
            }
        }

        public int To
        {
            get
            {
                return ToParent ? Key.ParentId : Key.ChildId;
            }
        }

        public string Predicate(string from, string to)
        {
            return string.Join(" AND ", Key.Columns.OrderBy(c => c.Ordinal).Select(c => from + "." + SqlName.Quote(ToParent ? c.Child : c.Parent) + " = " + to + "." + SqlName.Quote(ToParent ? c.Parent : c.Child)));
        }
    }

    public sealed class RelationPath
    {
        public readonly List<RelationStep> Steps = new List<RelationStep>();
        public override string ToString()
        {
            return Steps.Count + " JOIN · " + string.Join(" → ", Steps.Select(s => s.ToParent ? s.Key.ParentSchema + "." + s.Key.ParentTable : s.Key.ChildSchema + "." + s.Key.ChildTable)) + " · " + string.Join(" / ", Steps.Select(s => s.Key.Name));
        }
    }

    public sealed class WorkflowGraph
    {
        public CatalogSnapshot Catalog;
        public readonly List<ForeignKey> Keys = new List<ForeignKey>();
        public readonly Dictionary<int, List<HashSet<string>>> Unique = new Dictionary<int, List<HashSet<string>>>();
        public static async Task<WorkflowGraph> ReadAsync(DbConnection connection, CatalogSnapshot catalog, CancellationToken cancel)
        {
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancel).ConfigureAwait(false);
            var graph = new WorkflowGraph
            {
                Catalog = catalog
            };
            var map = new Dictionary<int, ForeignKey>();
            int total = 0;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandTimeout = 15;
                cmd.CommandText = @"SELECT f.object_id,f.name,f.parent_object_id,f.referenced_object_id,f.is_disabled,f.is_not_trusted,
cs.name,ct.name,ps.name,pt.name,k.constraint_column_id,cc.name,pc.name
FROM sys.foreign_keys f JOIN sys.foreign_key_columns k ON k.constraint_object_id=f.object_id
JOIN sys.tables ct ON ct.object_id=f.parent_object_id JOIN sys.schemas cs ON cs.schema_id=ct.schema_id
JOIN sys.tables pt ON pt.object_id=f.referenced_object_id JOIN sys.schemas ps ON ps.schema_id=pt.schema_id
JOIN sys.columns cc ON cc.object_id=k.parent_object_id AND cc.column_id=k.parent_column_id
JOIN sys.columns pc ON pc.object_id=k.referenced_object_id AND pc.column_id=k.referenced_column_id ORDER BY f.object_id,k.constraint_column_id;";
                using (var r = await cmd.ExecuteReaderAsync(cancel).ConfigureAwait(false))
                    while (await r.ReadAsync(cancel).ConfigureAwait(false))
                    {
                        if (++total > 20000)
                            throw new InvalidOperationException("Più di 20.000 coppie FK: ricerca non disponibile per questo catalogo.");
                        int id = r.GetInt32(0);
                        ForeignKey key;
                        if (!map.TryGetValue(id, out key))
                        {
                            map[id] = key = new ForeignKey
                            {
                                Id = id,
                                Name = r.GetString(1),
                                ChildId = r.GetInt32(2),
                                ParentId = r.GetInt32(3),
                                Disabled = r.GetBoolean(4),
                                Untrusted = r.GetBoolean(5),
                                ChildSchema = r.GetString(6),
                                ChildTable = r.GetString(7),
                                ParentSchema = r.GetString(8),
                                ParentTable = r.GetString(9)
                            };
                            graph.Keys.Add(key);
                        }

                        key.Columns.Add(new KeyColumn { Ordinal = r.GetInt32(10), Child = r.GetString(11), Parent = r.GetString(12) });
                    }
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandTimeout = 15;
                cmd.CommandText = @"SELECT i.object_id,i.index_id,c.name FROM sys.indexes i JOIN sys.index_columns k ON k.object_id=i.object_id AND k.index_id=i.index_id JOIN sys.columns c ON c.object_id=k.object_id AND c.column_id=k.column_id WHERE i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0 AND i.is_hypothetical=0 AND k.key_ordinal>0 ORDER BY i.object_id,i.index_id,k.key_ordinal;";
                var indexes = new Dictionary<string, HashSet<string>>();
                total = 0;
                using (var r = await cmd.ExecuteReaderAsync(cancel).ConfigureAwait(false))
                    while (await r.ReadAsync(cancel).ConfigureAwait(false))
                    {
                        if (++total > 50000)
                            throw new InvalidOperationException("Troppi indici per la ricerca relazionale.");
                        int obj = r.GetInt32(0);
                        string id = obj + ":" + r.GetInt32(1);
                        HashSet<string> columns;
                        if (!indexes.TryGetValue(id, out columns))
                        {
                            indexes[id] = columns = new HashSet<string>(StringComparer.Ordinal);
                            if (!graph.Unique.ContainsKey(obj))
                                graph.Unique[obj] = new List<HashSet<string>>();
                            graph.Unique[obj].Add(columns);
                        }

                        columns.Add(r.GetString(2));
                    }
            }

            return graph;
        }

        public List<RelationPath> Paths(int root, int target, int depth, CancellationToken cancel)
        {
            if (depth < 1 || depth > 5)
                throw new ArgumentException("Profondità da 1 a 5.");
            var result = new List<RelationPath>();
            var queue = new Queue<Tuple<int, RelationPath, HashSet<int>>>();
            queue.Enqueue(Tuple.Create(root, new RelationPath(), new HashSet<int> { root }));
            int examined = 0;
            while (queue.Count > 0 && result.Count < 30)
            {
                cancel.ThrowIfCancellationRequested();
                var item = queue.Dequeue();
                if (item.Item2.Steps.Count >= depth)
                    continue;
                foreach (var key in Keys.Where(k => !k.Disabled && k.Columns.Count > 0 && (k.ChildId == item.Item1 || k.ParentId == item.Item1)).OrderBy(k => k.Id))
                {
                    if (result.Count >= 30)
                        break;
                    if (++examined > 10000)
                        throw new InvalidOperationException("Ricerca troppo ampia: riduci la profondità.");
                    var step = new RelationStep
                    {
                        Key = key,
                        ToParent = key.ChildId == item.Item1
                    };
                    if (item.Item3.Contains(step.To) || Catalog.Objects.All(o => o.Id != step.To))
                        continue;
                    var path = new RelationPath();
                    path.Steps.AddRange(item.Item2.Steps);
                    path.Steps.Add(step);
                    if (step.To == target || target == 0)
                        result.Add(path);
                    if (step.To != target)
                    {
                        var visited = new HashSet<int>(item.Item3)
                        {
                            step.To
                        };
                        queue.Enqueue(Tuple.Create(step.To, path, visited));
                    }
                }
            }

            return result.OrderBy(p => p.Steps.Count).ToList();
        }

        public string Cardinality(RelationStep step)
        {
            if (step.Key.Disabled)
                return "FK disabilitata: relazione non usata";
            List<HashSet<string>> indexes;
            bool one = step.ToParent || Unique.TryGetValue(step.Key.ChildId, out indexes) && indexes.Any(i => i.Count > 0 && i.IsSubsetOf(new HashSet<string>(step.Key.Columns.Select(c => c.Child), StringComparer.Ordinal)));
            return (one ? "0..1 corrispondenze per riga (chiave univoca)" : "0..N possibili: il JOIN può moltiplicare le righe") + (step.Key.Untrusted ? " · FK non verificata" : "") + " · non è un conteggio dei dati";
        }

        string Source(int id)
        {
            var obj = Catalog.Objects.Single(o => o.Id == id);
            return SqlName.Quote(obj.Schema) + "." + SqlName.Quote(obj.Name);
        }

        public string JoinSql(int root, RelationPath path)
        {
            if (path == null || path.Steps.Count == 0)
                throw new ArgumentException("Scegli un percorso.");
            string sql = "SELECT TOP 100 p0.*, p" + path.Steps.Count + ".*\r\nFROM " + Source(root) + " AS p0";
            int current = root;
            for (int i = 0; i < path.Steps.Count; i++)
            {
                var s = path.Steps[i];
                if (s.From != current)
                    throw new ArgumentException("Percorso non valido.");
                sql += "\r\nINNER JOIN " + Source(s.To) + " AS p" + (i + 1) + " ON " + s.Predicate("p" + i, "p" + (i + 1));
                current = s.To;
            }

            return sql + ";\r\n\r\n-- " + string.Join("\r\n-- ", path.Steps.Select(Cardinality));
        }

        public string RelatedSql(int root, RelationPath path, ColumnShape filter, string value)
        {
            if (path == null || path.Steps.Count == 0 || filter == null)
                throw new ArgumentException("Scegli relazione e colonna filtro.");
            string predicate = "p0." + SqlName.Quote(filter.Name) + " = " + (string.IsNullOrEmpty(value) ? "@ValoreDaCompilare" : WorkflowSql.Literal(value, filter.TypeName));
            string from = Source(root) + " AS p0";
            for (int i = 0; i < path.Steps.Count - 1; i++)
            {
                var s = path.Steps[i];
                from += "\r\n    INNER JOIN " + Source(s.To) + " AS p" + (i + 1) + " ON " + s.Predicate("p" + i, "p" + (i + 1));
            }

            var last = path.Steps.Last();
            return "SELECT TOP 100 t.*\r\nFROM " + Source(last.To) + " AS t\r\nWHERE EXISTS (\r\n    SELECT 1 FROM " + from + "\r\n    WHERE " + predicate + "\r\n      AND " + last.Predicate("p" + (path.Steps.Count - 1), "t") + "\r\n);";
        }

        public string Orphans(int child, int keyId)
        {
            var key = Keys.Single(k => k.Id == keyId && k.ChildId == child && !k.Disabled);
            var s = new RelationStep
            {
                Key = key,
                ToParent = true
            };
            return "SELECT TOP 100 c.* FROM " + Source(child) + " AS c\r\nWHERE " + string.Join(" AND ", key.Columns.Select(c => "c." + SqlName.Quote(c.Child) + " IS NOT NULL")) + "\r\nAND NOT EXISTS (SELECT 1 FROM " + Source(key.ParentId) + " AS p WHERE " + s.Predicate("c", "p") + ");";
        }
    }

    public sealed class MetadataHit
    {
        public string Object, Detail;
        public override string ToString()
        {
            return Object + " · " + Detail;
        }
    }

    public static class WorkflowSearch
    {
        public static async Task<List<MetadataHit>> Find(DbConnection connection, string term, int? dependency, CancellationToken cancel)
        {
            if (term == null || term.Length > 128)
                throw new ArgumentException("Ricerca troppo lunga.");
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancel).ConfigureAwait(false);
            var result = new List<MetadataHit>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandTimeout = 12;
                cmd.CommandText = dependency.HasValue ? @"SELECT DISTINCT TOP (500) s.name+'.'+o.name,o.type_desc FROM sys.sql_expression_dependencies d JOIN sys.objects o ON o.object_id=d.referencing_id JOIN sys.schemas s ON s.schema_id=o.schema_id WHERE d.referenced_id=@id ORDER BY s.name+'.'+o.name;" : @"SELECT TOP (500) s.name+'.'+o.name,c.name+' · '+TYPE_NAME(c.user_type_id) FROM sys.columns c JOIN sys.objects o ON o.object_id=c.object_id JOIN sys.schemas s ON s.schema_id=o.schema_id WHERE o.is_ms_shipped=0 AND CHARINDEX(@term,c.name)>0 ORDER BY s.name,o.name,c.column_id;";
                MetadataReader.Add(cmd, dependency.HasValue ? "@id" : "@term", dependency.HasValue ? (object)dependency.Value : term);
                using (var r = await cmd.ExecuteReaderAsync(cancel).ConfigureAwait(false))
                    while (await r.ReadAsync(cancel).ConfigureAwait(false))
                        result.Add(new MetadataHit { Object = r.GetString(0), Detail = r.IsDBNull(1) ? "" : r.GetString(1) });
            }

            return result;
        }
    }
}
