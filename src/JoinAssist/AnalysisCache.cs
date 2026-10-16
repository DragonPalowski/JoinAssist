using System;
using System.Collections.Generic;

namespace JoinAssist
{
    // Bounded memory only; query text never goes to disk or to diagnostics.
    internal sealed class AnalysisCache<T>
    {
        readonly object sync=new object();
        readonly LinkedList<KeyValuePair<string,T>> entries=new LinkedList<KeyValuePair<string,T>>();
        int characters;
        internal T Get(string sql,Func<T> create)
        {
            lock(sync)
                for(var node=entries.First;node!=null;node=node.Next)
                    if(string.Equals(node.Value.Key,sql,StringComparison.Ordinal)) { var found=node.Value;entries.Remove(node);entries.AddFirst(found);return found.Value; }
            T value=create();
            lock(sync)
            {
                for(var node=entries.First;node!=null;node=node.Next)if(node.Value.Key==sql)return node.Value.Value;
                entries.AddFirst(new KeyValuePair<string,T>(sql,value));characters+=sql.Length;
                while(entries.Count>4 || characters>1000000) { characters-=entries.Last.Value.Key.Length;entries.RemoveLast(); }
            }
            return value;
        }
        internal void Clear() { lock(sync){ entries.Clear();characters=0; } }
    }
}
