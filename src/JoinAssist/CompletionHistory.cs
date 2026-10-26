using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace JoinAssist
{
    // Only accepted choices: hashed server/database + prefix + object identity, counts and UTC time.
    // No SQL text, connection strings, credentials or raw object names are persisted.
    public sealed class CompletionHistory
    {
        public sealed class Entry {public string Key {get;set;} public int Count {get;set;} public long Seen {get;set;}}
        public sealed class Saved {public string Generation {get;set;} public List<Entry> Entries {get;set;}=new List<Entry>();}
        readonly object sync=new object();readonly string path;
        readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.Ordinal);
        bool saving;string generation;long resetVersion;
        public long ResetVersion {get{return Interlocked.Read(ref resetVersion);}}
        public static CompletionHistory Current {get;private set;}=new CompletionHistory(null);
        public static void Initialize()
        {Current=new CompletionHistory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JoinAssist","choices.json"));}
        public CompletionHistory(string file)
        {
            path=file;
            if(path==null)return;
            var saved=Read();generation=saved.Generation;
            foreach(var e in saved.Entries)entries[e.Key]=e;
        }
        Saved Read()
        {
            try
            {
                var info=new FileInfo(path);if(!info.Exists || info.Length>524288)return new Saved();
                var saved=new JavaScriptSerializer().Deserialize<Saved>(File.ReadAllText(path));
                Guid marker;if(saved==null || !Guid.TryParse(saved.Generation,out marker))return new Saved();
                long oldest=DateTime.UtcNow.AddDays(-180).Ticks,latest=DateTime.UtcNow.AddDays(1).Ticks;
                saved.Entries=(saved.Entries??new List<Entry>()).Where(e=>e!=null && e.Key!=null && e.Key.Length==64 && e.Key.All(Uri.IsHexDigit) && e.Count>0 && e.Count<=1000 && e.Seen>=oldest && e.Seen<=latest).OrderByDescending(e=>e.Seen).Take(2048).ToList();return saved;
            }
            catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException) { }
            return new Saved();
        }
        public static string ScopeKey(string server,string database)
        {return string.IsNullOrEmpty(server)||string.IsNullOrEmpty(database)?null:Hash(server.ToUpperInvariant()+"\0"+database);}
        static string Hash(string value)
        {using(var sha=SHA256.Create())return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b=>b.ToString("x2")));}
        static string Key(string scope,string prefix,JoinProposal proposal)
        {
            if(string.IsNullOrEmpty(scope)||string.IsNullOrWhiteSpace(prefix)||proposal.IsInsertTemplate || proposal.IsFilterTemplate || proposal.IsPersonalRule || proposal.Keyword!=null)return null;
            return Hash(scope+"\0"+prefix.ToUpperInvariant()+"\0"+proposal.ChoiceIdentity);
        }
        public int Boost(string scope,string prefix,JoinProposal proposal)
        {
            var key=Key(scope,prefix,proposal);if(key==null)return 0;
            lock(sync)
            {
                Entry e;if(!entries.TryGetValue(key,out e))return 0;
                int months=(int)Math.Max(0,(DateTime.UtcNow-new DateTime(e.Seen,DateTimeKind.Utc)).TotalDays/30);
                return Math.Max(0,Math.Min(20,e.Count)-months);
            }
        }
        public void Record(string scope,string prefix,JoinProposal proposal,long? expectedResetVersion=null)
        {
            var key=Key(scope,prefix,proposal);if(key==null)return;
            lock(sync)
            {
                if(expectedResetVersion.HasValue && expectedResetVersion.Value!=ResetVersion)return;
                Entry e;if(!entries.TryGetValue(key,out e))entries[key]=e=new Entry{Key=key};
                e.Count=Math.Min(1000,e.Count+1);e.Seen=DateTime.UtcNow.Ticks;
                while(entries.Count>2048)entries.Remove(entries.Values.OrderBy(v=>v.Seen).First().Key);
                QueueSave();
            }
        }
        public void Clear()
        {
            Interlocked.Increment(ref resetVersion);
            // Serialize with a pending writer; after this returns it cannot restore old choices.
            lock(sync)
            {
                entries.Clear();
                if(path!=null)Persist(true);
            }
        }
        void QueueSave()
        {
            if(path==null || saving)return;saving=true;
            Task.Run(()=>
            {
                lock(sync)
                {
                    try{Persist(false);}
                    catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { }
                    finally{saving=false;Monitor.PulseAll(sync);}
                }
            });
        }
        void Persist(bool reset)
        {
            // Serialize independent SSMS instances. A reset generation prevents an older process
            // or queued writer from restoring choices cleared in another instance.
            using(var mutex=new Mutex(false,"Local\\JoinAssist.Choices."+Hash(Path.GetFullPath(path))))
            {
                bool held=false;
                try
                {
                    try{held=mutex.WaitOne(reset?500:2000);}catch(AbandonedMutexException){held=true;}
                    if(!held)throw new IOException("Choice history is busy");
                    var latest=Read();
                    if(reset){entries.Clear();generation=Guid.NewGuid().ToString("N");}
                    else
                    {
                        bool resetElsewhere=generation!=null && latest.Generation!=null && generation!=latest.Generation;
                        if(resetElsewhere)entries.Clear();
                        foreach(var item in latest.Entries)
                        {
                            Entry current;
                            if(!entries.TryGetValue(item.Key,out current) || current.Count<item.Count)entries[item.Key]=item;
                            else current.Seen=Math.Max(current.Seen,item.Seen);
                        }
                        generation=latest.Generation??generation??Guid.NewGuid().ToString("N");
                        while(entries.Count>2048)entries.Remove(entries.Values.OrderBy(e=>e.Seen).First().Key);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    Write(new JavaScriptSerializer().Serialize(new Saved{Generation=generation,Entries=entries.Values.ToList()}));
                }
                finally{if(held)mutex.ReleaseMutex();}
            }
        }
        void Write(string json)
        {
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,json,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        internal void WaitForSave() {lock(sync)while(saving)Monitor.Wait(sync);}
    }
}
