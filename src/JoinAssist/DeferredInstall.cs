using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace JoinAssist
{
    // Shared with the standalone worker. Contains no editor, SQL, credential or network integration.
    internal sealed class DeferredPlan
    {
        public int Format {get;set;} = 1;
        public string Id {get;set;}
        public string Version {get;set;}
        public string Package {get;set;}
        public string PackageHash {get;set;}
        public long Size {get;set;}
        public string Host {get;set;}
        public string Installer {get;set;}
        public string InstallerHash {get;set;}
        public long RequestedUtc {get;set;}
        public long ExpiresUtc {get;set;}
        public string RequestHash {get;set;}
        public string State {get;set;}
        public string FailureCode {get;set;}
        public int WorkerId {get;set;}
        public long WorkerStartedUtc {get;set;}
        internal bool Pending {get{return State=="Preparing" || State=="Waiting" || State=="Starting";}}
    }
    internal sealed class DeferredInstall
    {
        internal static string UpdatesRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"JoinAssist","Updates");}}
        internal static string DefaultRoot {get{return Path.Combine(UpdatesRoot,"Deferred");}}
        internal readonly string Root;
        readonly string updates;
        internal DeferredInstall(string root)
        {Root=Path.GetFullPath(root);updates=Directory.GetParent(Root).FullName;}
        internal T Locked<T>(Func<T> action)
        {
            Directory.CreateDirectory(Root);NoLinks(Root);
            var timer=Stopwatch.StartNew();FileStream gate=null;
            while(gate==null)
            {
                try{gate=new FileStream(Path.Combine(Root,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
                catch(IOException){if(timer.Elapsed>TimeSpan.FromSeconds(3))throw new IOException("Un'altra operazione di aggiornamento è in corso. Riprova.");Thread.Sleep(40);}
            }
            using(gate)return action();
        }
        internal DeferredPlan Read()
        {
            string path=Path.Combine(Root,"pending.json");if(!File.Exists(path))return null;
            NoLinks(path);
            if(new FileInfo(path).Length>32768)throw new InvalidDataException("Dati dell'aggiornamento in attesa non validi.");
            var plan=new JavaScriptSerializer{MaxJsonLength=32768,RecursionLimit=12}.Deserialize<DeferredPlan>(File.ReadAllText(path));
            if(plan==null)throw new InvalidDataException("Dati dell'aggiornamento in attesa non validi.");
            Validate(plan);return plan;
        }
        internal void Write(DeferredPlan plan)
        {
            string path=Path.Combine(Root,"pending.json"),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(plan),new UTF8Encoding(false));
                if(File.Exists(path)){NoLinks(path);File.Replace(temporary,path,null);}else File.Move(temporary,path);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        internal void Validate(DeferredPlan p)
        {
            Guid id;Version version;
            if(p.Format!=1 || !Guid.TryParseExact(p.Id,"N",out id) || !Regex.IsMatch(p.Version??"",@"\A\d+\.\d+\.\d+(?:\.\d+)?\z") || !Version.TryParse(p.Version,out version) ||
                !Digest(p.PackageHash) || !Digest(p.InstallerHash) || !Digest(p.RequestHash) || p.Size<=0 || p.Size>25*1024*1024 ||
                p.RequestedUtc<new DateTime(1970,1,1).Ticks || p.ExpiresUtc-p.RequestedUtc!=TimeSpan.FromHours(24).Ticks || p.RequestedUtc>DateTime.UtcNow.AddMinutes(5).Ticks ||
                !new[]{"Preparing","Waiting","Starting","Launched","Cancelled","Expired","Failed"}.Contains(p.State) || !string.Equals(Fingerprint(p),p.RequestHash,StringComparison.Ordinal))
                throw new InvalidDataException("Dati dell'aggiornamento in attesa non validi.");
            if(!Under(p.Package,updates) || !string.Equals(Path.GetFileName(p.Package),"JoinAssist-"+p.Version+".vsix",StringComparison.Ordinal) ||
                !string.Equals(Path.GetFileName(p.Host),"Ssms.exe",StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFullPath(p.Installer),Path.Combine(Path.GetDirectoryName(Path.GetFullPath(p.Host)),"VSIXInstaller.exe"),StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Percorsi dell'aggiornamento in attesa non validi.");
        }
        internal static string Fingerprint(DeferredPlan p)
        {
            string payload=string.Join("\0",new[]{p.Format.ToString(CultureInfo.InvariantCulture),p.Id,p.Version,p.Package,p.PackageHash,p.Size.ToString(CultureInfo.InvariantCulture),p.Host,p.Installer,p.InstallerHash,p.RequestedUtc.ToString(CultureInfo.InvariantCulture),p.ExpiresUtc.ToString(CultureInfo.InvariantCulture)});
            using(var sha=SHA256.Create())return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }
        internal static string HashFile(string path)
        {NoLinks(path);using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))using(var sha=SHA256.Create())return Hex(sha.ComputeHash(file));}
        internal static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
        static bool Digest(string value){return value!=null && Regex.IsMatch(value,@"\A[a-f0-9]{64}\z",RegexOptions.CultureInvariant);}
        internal static bool Under(string path,string parent)
        {return !string.IsNullOrEmpty(path) && Path.IsPathRooted(path) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        internal static void NoLinks(string path)
        {
            for(string current=Path.GetFullPath(path);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
                if((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("La cartella dell'aggiornamento contiene un collegamento non supportato.");
        }
        internal void VerifyFiles(DeferredPlan p)
        {
            Validate(p);NoLinks(p.Host);if(!File.Exists(p.Host))throw new FileNotFoundException("Installazione SSMS non trovata.");
            if(new FileInfo(p.Package).Length!=p.Size || HashFile(p.Package)!=p.PackageHash || HashFile(p.Installer)!=p.InstallerHash)
                throw new InvalidDataException("Il pacchetto o l'installer sono cambiati. Scarica e programma nuovamente l'aggiornamento.");
        }
        internal bool Cancel(string id)
        {
            return Locked(()=>{var p=Read();if(p==null || p.Id!=id || p.State!="Waiting" && p.State!="Preparing")return false;p.State="Cancelled";Write(p);return true;});
        }
        internal bool Step(string id,string fingerprint,Func<bool> ssmsRunning,Action<DeferredPlan> launch,DateTime now)
        {
            return Locked(()=>{
                var p=Read();
                if(p==null || p.Id!=id || p.RequestHash!=fingerprint || !p.Pending)return false;
                if(p.State=="Starting"){p.State="Failed";p.FailureCode="JA-UPDATE-START-01";Write(p);return false;}
                if(now.Ticks>=p.ExpiresUtc){p.State="Expired";Write(p);return false;}
                if(p.State=="Preparing"){p.State="Waiting";Write(p);}
                if(ssmsRunning())return true;
                try
                {
                    VerifyFiles(p);
                    // Keep the verified files read-locked until the installer has been launched.
                    using(var package=new FileStream(p.Package,FileMode.Open,FileAccess.Read,FileShare.Read))
                    using(var installer=new FileStream(p.Installer,FileMode.Open,FileAccess.Read,FileShare.Read))
                    {
                        using(var sha=SHA256.Create())if(package.Length!=p.Size || Hex(sha.ComputeHash(package))!=p.PackageHash || Hex(sha.ComputeHash(installer))!=p.InstallerHash)throw new InvalidDataException();
                        if(ssmsRunning())return true;
                        p.State="Starting";Write(p);launch(p);p.State="Launched";Write(p);return false;
                    }
                }
                catch(Exception ex) when(ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is System.Security.SecurityException)
                {p.State="Failed";p.FailureCode=ex is InvalidDataException?"JA-UPDATE-INTEGRITY-01":"JA-UPDATE-START-01";Write(p);return false;}
            });
        }
        internal static bool SsmsRunning()
        {
            var processes=Process.GetProcessesByName("Ssms");
            try{return processes.Any(p=>!p.HasExited);}
            catch(InvalidOperationException){return true;} // A changing process list is not proof that SSMS is closed.
            finally{foreach(var process in processes)process.Dispose();}
        }
        internal static bool WorkerAlive(DeferredPlan p)
        {
            if(p.WorkerId<=0)return false;
            try{using(var process=Process.GetProcessById(p.WorkerId))return !process.HasExited && process.StartTime.ToUniversalTime().Ticks==p.WorkerStartedUtc;}
            catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
        }
    }
    internal static class DeferredWorker
    {
        internal static int Run(DeferredInstall store,string id,string fingerprint,Func<bool> running,Action<DeferredPlan> launch,int quietMilliseconds=3000,int pollMilliseconds=500)
        {
            try
            {
                var quiet=Stopwatch.StartNew();var lifetime=Stopwatch.StartNew();
                while(lifetime.Elapsed<TimeSpan.FromHours(24))
                {
                    bool exists=running();if(exists)quiet.Restart();
                    bool again=store.Step(id,fingerprint,()=>exists || quiet.ElapsedMilliseconds<quietMilliseconds || running(),launch,DateTime.UtcNow);
                    if(!again)return 0;
                    Thread.Sleep(pollMilliseconds);
                }
                store.Locked(()=>{var p=store.Read();if(p!=null && p.Id==id && p.RequestHash==fingerprint && p.Pending){p.State="Expired";store.Write(p);}return true;});return 0;
            }
            catch(Exception ex) when(ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is System.Security.SecurityException)
            {
                try{store.Locked(()=>{var p=store.Read();if(p!=null && p.Id==id && p.RequestHash==fingerprint && p.Pending){p.State="Failed";p.FailureCode="JA-UPDATE-WORKER-01";store.Write(p);}return true;});}
                catch(Exception failure) when(failure is InvalidDataException || failure is IOException || failure is UnauthorizedAccessException || failure is ArgumentException || failure is InvalidOperationException || failure is System.Security.SecurityException){}
                return 1;
            }
        }
    }
}
