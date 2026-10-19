using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace JoinAssist
{
    internal sealed class DeferredScheduler
    {
        readonly DeferredInstall store=new DeferredInstall(DeferredInstall.DefaultRoot);
        internal Task<DeferredPlan> InspectAsync()
        {
            return Task.Run(()=>{
                if(!Directory.Exists(store.Root))return null;
                return store.Locked(()=>{
                    var p=store.Read();
                    if(p!=null && (p.State=="Preparing" || p.State=="Waiting") && AlreadyInstalled(p))
                    {p.State="Cancelled";p.FailureCode="JA-UPDATE-ALREADY-01";store.Write(p);}
                    else if(p!=null && p.Pending && !DeferredInstall.WorkerAlive(p))
                    {p.State="Failed";p.FailureCode="JA-UPDATE-WORKER-01";store.Write(p);}
                    return p;
                });
            });
        }
        internal Task<bool> CancelAsync(string id){return Task.Run(()=>store.Cancel(id));}
        internal Task<DeferredPlan> ScheduleAsync(UpdateRelease release,string package,CancellationToken cancel)
        {
            return Task.Run(()=>store.Locked(()=>{
                cancel.ThrowIfCancellationRequested();
                if(release==null || release.Version<=UpdateClient.InstalledVersion)throw new InvalidOperationException("Non è disponibile una versione più recente da programmare.");
                var previous=store.Read();
                if(previous!=null && previous.Pending && !AlreadyInstalled(previous) && DeferredInstall.WorkerAlive(previous))return previous;
                string host=Process.GetCurrentProcess().MainModule.FileName;
                if(!string.Equals(Path.GetFileName(host),"Ssms.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Programma l'aggiornamento dalla finestra di SSMS.");
                string installer=Path.Combine(Path.GetDirectoryName(host),"VSIXInstaller.exe");
                if(!File.Exists(installer))throw new InvalidOperationException("Installer Microsoft di questa installazione SSMS non trovato. Usa il download manuale.");
                var now=DateTime.UtcNow;
                var plan=new DeferredPlan{Id=Guid.NewGuid().ToString("N"),Version=UpdateClient.DisplayVersion(release.Version),Package=Path.GetFullPath(package),PackageHash=release.Sha256,Size=release.Size,
                    Host=host,Installer=installer,InstallerHash=DeferredInstall.HashFile(installer),RequestedUtc=now.Ticks,ExpiresUtc=now.AddHours(24).Ticks,State="Preparing"};
                plan.RequestHash=DeferredInstall.Fingerprint(plan);store.VerifyFiles(plan);
                string directory=Path.Combine(store.Root,plan.Id);Directory.CreateDirectory(directory);DeferredInstall.NoLinks(directory);
                string worker=Path.Combine(directory,"JoinAssist.UpdateInstaller.exe");
                using(var source=typeof(DeferredScheduler).Assembly.GetManifestResourceStream("JoinAssist.UpdateInstaller.exe"))
                {
                    if(source==null)throw new InvalidOperationException("Componente di aggiornamento mancante. Usa il download manuale.");
                    using(var target=new FileStream(worker,FileMode.CreateNew,FileAccess.Write,FileShare.None))source.CopyTo(target);
                }
                cancel.ThrowIfCancellationRequested();store.Write(plan);
                try
                {
                    using(var process=Process.Start(new ProcessStartInfo(worker,"--wait \""+store.Root+"\" "+plan.Id+" "+plan.RequestHash)
                        {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=directory}))
                    {
                        if(process==null)throw new InvalidOperationException("Il componente di aggiornamento non si è avviato.");
                        plan.WorkerId=process.Id;plan.WorkerStartedUtc=process.StartTime.ToUniversalTime().Ticks;store.Write(plan);
                    }
                }
                catch{plan.State="Failed";plan.FailureCode="JA-UPDATE-WORKER-01";store.Write(plan);throw;}
                return plan;
            }),cancel);
        }
        internal static string Describe(DeferredPlan plan)
        {
            if(plan==null)return "Nessuna installazione in attesa.";
            string version="JoinAssist "+plan.Version;
            switch(plan.State)
            {
                case "Preparing":case "Waiting":return version+" pronto: l'installer Microsoft si aprirà quando avrai chiuso tutte le finestre di SSMS. Puoi continuare a lavorare. Attesa massima: 24 ore.";
                case "Starting":return "Avvio dell'installer Microsoft in corso. L'attesa non è più annullabile.";
                case "Launched":return "Installer Microsoft avviato per "+version+". Completa la procedura; l'esito dell'installazione non viene rilevato dal plugin.";
                case "Cancelled":return plan.FailureCode=="JA-UPDATE-ALREADY-01"?"La versione programmata è già presente: nessuna installazione in attesa.":"Installazione alla chiusura annullata. Il pacchetto scaricato resta disponibile.";
                case "Expired":return "Attesa scaduta dopo 24 ore. Programma nuovamente l'aggiornamento quando vuoi.";
                default:return "Avvio automatico non completato ("+(plan.FailureCode??"JA-UPDATE-START-01")+"). Verifica l'eventuale installer aperto, poi riprova o usa il download manuale.";
            }
        }
        static bool AlreadyInstalled(DeferredPlan plan)
        {Version value;return Version.TryParse(plan.Version,out value) && new Version(value.Major,value.Minor,value.Build,Math.Max(0,value.Revision))<=UpdateClient.InstalledVersion;}
    }
}
