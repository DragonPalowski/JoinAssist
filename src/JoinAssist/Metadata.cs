using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JoinAssist
{
    public static class MetadataReader
    {
        public static async Task<List<ForeignKey>> ReadAsync(DbConnection connection, QueryContext context, CancellationToken cancel, MetadataScope scope=null)
        {
            var catalog = await CatalogReader.ReadAsync(connection, cancel,scope).ConfigureAwait(false);
            await new ShapeResolver(catalog, cancel).ResolveAsync(context, true).ConfigureAwait(false);
            return await ReadForPathsAsync(connection, context, catalog, cancel,scope).ConfigureAwait(false);
        }
        public static async Task<List<ForeignKey>> ReadForPathsAsync(DbConnection connection, QueryContext context, CatalogSnapshot catalog, CancellationToken cancel, MetadataScope scope=null)
        {
            var first=await ReadForeignKeysAsync(connection,context,catalog,cancel,scope).ConfigureAwait(false);
            if(!PreferenceStore.Read.JoinPaths || context.PredicateOnly)return first;
            var additional=new QueryContext();
            foreach(int id in first.SelectMany(k=>new[]{k.ChildId,k.ParentId}).Distinct().Take(1000))additional.Tables.Add(new TableUse{ObjectId=id,Name="metadata"+id});
            var second=await ReadForeignKeysAsync(connection,additional,catalog,cancel,scope).ConfigureAwait(false);
            return first.Concat(second).GroupBy(k=>k.Id).Select(g=>g.First()).ToList();
        }
        public static async Task<List<ForeignKey>> ReadForeignKeysAsync(DbConnection connection, QueryContext context, CatalogSnapshot catalog, CancellationToken cancel, MetadataScope scope=null)
        {
            string key="fk:"+string.Join(",",context.Tables.Concat(context.CteTargets).SelectMany(t=>t.Maps).Select(m=>m.ObjectId).Where(id=>id!=0).Distinct().OrderBy(id=>id));
            var keys=await MetadataCache.ReadAsync(scope,key,()=>ReadRawAsync(connection,context,catalog,cancel),cancel).ConfigureAwait(false);
            return keys.Select(k=>{
                var copy=new ForeignKey{Id=k.Id,Name=k.Name,ChildId=k.ChildId,ParentId=k.ParentId,ChildSchema=k.ChildSchema,ChildTable=k.ChildTable,ParentSchema=k.ParentSchema,ParentTable=k.ParentTable,ChildBare=k.ChildBare,ParentBare=k.ParentBare,Disabled=k.Disabled,Untrusted=k.Untrusted};
                copy.Columns.AddRange(k.Columns.Select(c=>new KeyColumn{Ordinal=c.Ordinal,Child=c.Child,Parent=c.Parent}));return copy;
            }).ToList();
        }
        static async Task<List<ForeignKey>> ReadRawAsync(DbConnection connection, QueryContext context, CatalogSnapshot catalog, CancellationToken cancel)
        {
            if(connection.State!=ConnectionState.Open)await connection.OpenAsync(cancel).ConfigureAwait(false);
            var keys = new Dictionary<int, ForeignKey>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandTimeout = 10;
                var ids = context.Tables.Concat(context.CteTargets).SelectMany(t => t.Maps).Select(m => m.ObjectId).Where(id => id != 0).Distinct().ToList();
                if (ids.Count == 0) return new List<ForeignKey>();
                if (ids.Count > 1000) throw new InvalidOperationException("Troppe sorgenti per una singola richiesta di completamento.");
                var names = new List<string>();
                for (int i = 0; i < ids.Count; i++) { string name = "@id" + i; names.Add(name); Add(cmd, name, ids[i]); }
                string idList = string.Join(",", names);
                cmd.CommandText = @"
SELECT fk.object_id, fk.name, fk.parent_object_id, fk.referenced_object_id,
       cs.name, ct.name, ps.name, pt.name, fk.is_disabled, fk.is_not_trusted,
       fkc.constraint_column_id, cc.name, pc.name
FROM sys.foreign_keys AS fk
JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id=fk.object_id
JOIN sys.tables AS ct ON ct.object_id=fk.parent_object_id
JOIN sys.schemas AS cs ON cs.schema_id=ct.schema_id
JOIN sys.tables AS pt ON pt.object_id=fk.referenced_object_id
JOIN sys.schemas AS ps ON ps.schema_id=pt.schema_id
JOIN sys.columns AS cc ON cc.object_id=fkc.parent_object_id AND cc.column_id=fkc.parent_column_id
JOIN sys.columns AS pc ON pc.object_id=fkc.referenced_object_id AND pc.column_id=fkc.referenced_column_id
WHERE fk.parent_object_id IN (" + idList + ") OR fk.referenced_object_id IN (" + idList + @")
ORDER BY fk.object_id, fkc.constraint_column_id;";
                using (var rows = await cmd.ExecuteReaderAsync(cancel).ConfigureAwait(false))
                    while (await rows.ReadAsync(cancel).ConfigureAwait(false))
                    {
                        int id = rows.GetInt32(0);
                        ForeignKey fk;
                        if (!keys.TryGetValue(id, out fk))
                        {
                            fk = new ForeignKey { Id = id, Name = rows.GetString(1), ChildId = rows.GetInt32(2), ParentId = rows.GetInt32(3),
                                ChildSchema = rows.GetString(4), ChildTable = rows.GetString(5), ParentSchema = rows.GetString(6), ParentTable = rows.GetString(7),
                                Disabled = rows.GetBoolean(8), Untrusted = rows.GetBoolean(9) };
                            var child = catalog.Objects.FirstOrDefault(o => o.Id == fk.ChildId);
                            var parent = catalog.Objects.FirstOrDefault(o => o.Id == fk.ParentId);
                            fk.ChildBare = child != null && child.BareAllowed; fk.ParentBare = parent != null && parent.BareAllowed;
                            keys.Add(id, fk);
                        }
                        fk.Columns.Add(new KeyColumn { Ordinal = rows.GetInt32(10), Child = rows.GetString(11), Parent = rows.GetString(12) });
                    }
            }
            return keys.Values.ToList();
        }
        internal static void Add(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value;
            parameter.DbType = value is int ? DbType.Int32 : DbType.String;
            if (value is string) parameter.Size = 776;
            command.Parameters.Add(parameter);
        }
    }
}
