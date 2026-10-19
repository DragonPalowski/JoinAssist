using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using JoinAssist;

static class DeferredInstallTests
{
    static int checks;
    static void Check(bool value,string label){checks++;if(!value)throw new Exception("Deferred update: "+label);}
    static void Reject(Action action,string label){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}catch(IOException){rejected=true;}Check(rejected,label);}
    internal static void Run()
    {
        using(var f=new Fixture())
        {
            var roundTrip=f.Read();Check(roundTrip.Id==f.Plan.Id && roundTrip.RequestHash==f.Plan.RequestHash,"request/status stored and validated");
            f.Store.VerifyFiles(roundTrip);Check(true,"package and installer verified before arming");
            int starts=0;
            Check(f.Store.Step(f.Plan.Id,f.Plan.RequestHash,()=>true,p=>starts++,DateTime.UtcNow) && starts==0 && f.Read().State=="Waiting","SSMS open means wait without launching");
            Check(f.Store.Step(f.Plan.Id,f.Plan.RequestHash,()=>false,p=>starts++,DateTime.UtcNow)==false && starts==1 && f.Read().State=="Launched","last SSMS closed launches once");
            Check(!f.Store.Step(f.Plan.Id,f.Plan.RequestHash,()=>false,p=>starts++,DateTime.UtcNow) && starts==1,"completed request never launches twice");
            Check(!f.Store.Cancel(f.Plan.Id),"cannot claim cancellation after launch");
        }
        using(var f=new Fixture())
        {
            Check(!f.Store.Cancel(Guid.NewGuid().ToString("N")),"another generation cannot cancel this request");
            Check(f.Store.Cancel(f.Plan.Id) && f.Read().State=="Cancelled","waiting request can be cancelled");
            bool launched=false;Check(!f.Store.Step(f.Plan.Id,f.Plan.RequestHash,()=>false,p=>launched=true,DateTime.UtcNow) && !launched,"confirmed cancellation prevents launch");
        }
        using(var f=new Fixture())
        {
            bool launched=false;Check(!f.Store.Step(f.Plan.Id,f.Plan.RequestHash,()=>false,p=>launched=true,DateTime.UtcNow.AddHours(25)) && !launched && f.Read().State=="Expired","expiry prevents launch");
        }
        foreach(bool installer in new[]{false,true})using(var f=new Fixture())
        {
            File.WriteAllText(installer?f.Plan.Installer:f.Plan.Package,"modified payload");bool launched=false;
            Check(!f.Store.Step(f.Plan.Id,f.Plan.RequestHash,()=>false,p=>launched=true,DateTime.UtcNow) && !launched && f.Read().State=="Failed","modified "+(installer?"installer":"VSIX")+" blocked at launch time");
        }
        using(var f=new Fixture())
        {
            int samples=0,starts=0;
            Check(f.Store.Step(f.Plan.Id,f.Plan.RequestHash,()=>++samples>1,p=>starts++,DateTime.UtcNow) && starts==0,"a reopened SSMS during verification blocks launch");
            Check(f.Read().State=="Waiting","reopened SSMS leaves request waiting");
        }
        using(var f=new Fixture())
        {
            bool started=false;
            Check(!f.Store.Step(f.Plan.Id,new string('0',64),()=>false,p=>started=true,DateTime.UtcNow) && !started,"worker arguments bind the immutable request fingerprint");
            f.Plan.PackageHash=new string('0',64);f.Store.Write(f.Plan);Reject(()=>f.Read(),"metadata tampering without a matching fingerprint rejected");
        }
        using(var f=new Fixture())
        {
            string expected=f.Plan.RequestHash;f.Plan.PackageHash=new string('1',64);f.Plan.RequestHash=DeferredInstall.Fingerprint(f.Plan);f.Store.Write(f.Plan);
            bool started=false;Check(!f.Store.Step(f.Plan.Id,expected,()=>false,p=>started=true,DateTime.UtcNow) && !started,"even recomputed metadata cannot replace a running worker's original request");
        }
        using(var f=new Fixture())
        {
            f.Plan.Package=Path.Combine(f.Base,"outside.vsix");f.Plan.RequestHash=DeferredInstall.Fingerprint(f.Plan);Reject(()=>f.Store.Validate(f.Plan),"package cannot escape update root");
            f.Plan.Package=Path.Combine(Directory.GetParent(f.Store.Root).FullName,"release","JoinAssist-99.0.0.vsix");
            f.Plan.Installer=Path.Combine(f.Base,"arbitrary.exe");f.Plan.RequestHash=DeferredInstall.Fingerprint(f.Plan);Reject(()=>f.Store.Validate(f.Plan),"only the host-adjacent Microsoft installer path is accepted");
            f.Plan.Installer=Path.Combine(Path.GetDirectoryName(f.Plan.Host),"VSIXInstaller.exe");f.Plan.ExpiresUtc++;f.Plan.RequestHash=DeferredInstall.Fingerprint(f.Plan);Reject(()=>f.Store.Validate(f.Plan),"request cannot extend the 24-hour wait");
        }
        using(var f=new Fixture())
        {
            using(var current=Process.GetCurrentProcess()){f.Plan.WorkerId=current.Id;f.Plan.WorkerStartedUtc=current.StartTime.ToUniversalTime().Ticks;}
            Check(DeferredInstall.WorkerAlive(f.Plan),"worker identity includes PID and start time");f.Plan.WorkerStartedUtc++;Check(!DeferredInstall.WorkerAlive(f.Plan),"PID reuse cannot revive an old pending request");
            f.Plan.State="Starting";f.Store.Write(f.Plan);bool started=false;f.Store.Step(f.Plan.Id,f.Plan.RequestHash,()=>false,p=>started=true,DateTime.UtcNow);
            Check(!started && f.Read().State=="Failed","interrupted Starting state is not retried automatically");
        }
        using(var f=new Fixture())
        {
            f.Store.Step(f.Plan.Id,f.Plan.RequestHash,()=>false,p=>{throw new System.ComponentModel.Win32Exception(5);},DateTime.UtcNow);
            Check(f.Read().State=="Failed" && f.Read().FailureCode=="JA-UPDATE-START-01","installer start failure persisted without credentials or exception details");
        }
        string harness=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DeferredWorkerHarness.exe");
        if(File.Exists(harness)){ProcessBoundary(harness,false);ProcessBoundary(harness,true);}
        Console.WriteLine("PASS: "+checks+" deferred-update checks (all-instance waiting, cancellation, expiry, restart race, fingerprint/checksum validation, at-most-once launch and worker identity). No real installer or SSMS process controlled.");
    }
    static void ProcessBoundary(string harness,bool cancel)
    {
        using(var f=new Fixture())
        {
            string first=Path.Combine(f.Store.Root,"test-first-running"),second=Path.Combine(f.Store.Root,"test-second-running"),marker=Path.Combine(f.Store.Root,"test-launch");
            File.WriteAllText(first,"open");File.WriteAllText(second,"open");
            using(var process=Process.Start(new ProcessStartInfo(harness,"\""+f.Store.Root+"\" "+f.Plan.Id+" "+f.Plan.RequestHash){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden}))
            {
                try
                {
                Check(process!=null,"isolated worker process started");
                Wait(()=>f.Read().State=="Waiting",5000,"worker reached waiting state");
                Check(!File.Exists(marker),"worker cannot start while two simulated SSMS instances are open");
                if(cancel)
                {
                    Check(f.Store.Cancel(f.Plan.Id),"another process can cancel a running worker");
                    Check(process.WaitForExit(5000) && !File.Exists(marker),"cancelled child exits without launch");
                }
                else
                {
                    File.Delete(first);Thread.Sleep(150);Check(!File.Exists(marker),"closing one of two instances is insufficient");
                    File.Delete(second);Thread.Sleep(80);File.WriteAllText(first,"reopened");Thread.Sleep(240);
                    Check(!File.Exists(marker),"quick reopen resets the quiet interval");
                    File.Delete(first);Check(process.WaitForExit(5000) && process.ExitCode==0 && File.Exists(marker),"standalone worker survives until final close and invokes only the synthetic callback");
                    Check(f.Read().State=="Launched","child persists launch result for the next SSMS startup");
                }
                }
                finally
                {
                    if(process!=null && !process.HasExited){f.Store.Cancel(f.Plan.Id);if(!process.WaitForExit(5000))throw new InvalidOperationException("Synthetic worker did not exit after cancellation; fixture retained.");}
                }
            }
        }
    }
    static void Wait(Func<bool> condition,int milliseconds,string label)
    {var watch=Stopwatch.StartNew();while(!condition() && watch.ElapsedMilliseconds<milliseconds)Thread.Sleep(30);Check(condition(),label);}
    sealed class Fixture : IDisposable
    {
        internal readonly string Base=Path.Combine(Path.GetTempPath(),"JoinAssist-deferred-"+Guid.NewGuid().ToString("N"));
        internal readonly DeferredInstall Store;
        internal readonly DeferredPlan Plan;
        internal Fixture()
        {
            string updates=Path.Combine(Base,"Updates"),host=Path.Combine(Base,"host"),release=Path.Combine(updates,"release");
            Directory.CreateDirectory(host);Directory.CreateDirectory(release);Store=new DeferredInstall(Path.Combine(updates,"Deferred"));Directory.CreateDirectory(Store.Root);
            string package=Path.Combine(release,"JoinAssist-99.0.0.vsix"),installer=Path.Combine(host,"VSIXInstaller.exe"),ssms=Path.Combine(host,"Ssms.exe");
            File.WriteAllText(package,"Synthetic package, never executed");File.WriteAllText(installer,"Not executable, never launched");File.WriteAllText(ssms,"Synthetic host");
            var now=DateTime.UtcNow;
            Plan=new DeferredPlan{Id=Guid.NewGuid().ToString("N"),Version="99.0.0",Package=package,PackageHash=DeferredInstall.HashFile(package),Size=new FileInfo(package).Length,Host=ssms,Installer=installer,InstallerHash=DeferredInstall.HashFile(installer),RequestedUtc=now.Ticks,ExpiresUtc=now.AddHours(24).Ticks,State="Preparing"};
            Plan.RequestHash=DeferredInstall.Fingerprint(Plan);Store.Locked(()=>{Store.Write(Plan);return true;});
        }
        internal DeferredPlan Read(){return Store.Locked(()=>Store.Read());}
        public void Dispose()
        {
            string path=Path.GetFullPath(Base),parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!path.StartsWith(parent,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("JoinAssist-deferred-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected test cleanup path.");
            DeferredInstall.NoLinks(path);Directory.Delete(path,true);
        }
    }
}
