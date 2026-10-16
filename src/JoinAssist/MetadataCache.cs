using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace JoinAssist
{
    public sealed class MetadataScope
    {
        internal readonly object Sync = new object();
        internal readonly SemaphoreSlim Gate = new SemaphoreSlim(1,1);
        internal readonly Dictionary<string,object> Items = new Dictionary<string,object>();
        internal DateTime Expires;
        internal int Revision;
    }
    public static class MetadataCache
    {
        sealed class Owner { internal string Session; internal readonly Dictionary<string,MetadataScope> Databases = new Dictionary<string,MetadataScope>(); }
        static readonly ConditionalWeakTable<object,Owner> owners = new ConditionalWeakTable<object,Owner>();
        public static MetadataScope ScopeFor(object nativeConnection, string server, string database, object session)
        {
            if (nativeConnection == null) return null;
            var owner=owners.GetValue(nativeConnection,key=>new Owner());
            lock(owner)
            {
                string identity=Convert.ToString(session);
                if(owner.Session!=identity) { owner.Databases.Clear(); owner.Session=identity; }
                string key=server+"\0"+database;
                MetadataScope scope;
                if(!owner.Databases.TryGetValue(key,out scope))
                {
                    if(owner.Databases.Count>=16) owner.Databases.Clear();
                    owner.Databases[key]=scope=new MetadataScope();
                }
                return scope;
            }
        }
        public static void Invalidate(MetadataScope scope)
        {
            if(scope==null)return;
            lock(scope.Sync) { scope.Revision++; scope.Items.Clear(); scope.Expires=DateTime.MinValue; }
        }
        public static async Task<T> ReadAsync<T>(MetadataScope scope,string key,Func<Task<T>> read,CancellationToken cancel)
        {
            if(scope==null)return await read().ConfigureAwait(false);
            await scope.Gate.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                int revision;
                lock(scope.Sync)
                {
                    if(DateTime.UtcNow>=scope.Expires) { scope.Items.Clear(); scope.Expires=DateTime.UtcNow.AddMinutes(PreferenceStore.Current.CacheMinutes); scope.Revision++; }
                    object found; if(scope.Items.TryGetValue(key,out found))return (T)found;
                    revision=scope.Revision;
                }
                var value=await read().ConfigureAwait(false);cancel.ThrowIfCancellationRequested();
                lock(scope.Sync) { if(revision==scope.Revision && DateTime.UtcNow<scope.Expires) scope.Items[key]=value; }
                return value;
            }
            finally { scope.Gate.Release(); }
        }
    }
}
