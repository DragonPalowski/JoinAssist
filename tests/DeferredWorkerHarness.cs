using System;
using System.IO;
using JoinAssist;

// Compiled only for tests and never embedded or packaged. The callback writes a marker;
// it cannot open VSIXInstaller or inspect/close any real SSMS process.
static class DeferredWorkerHarness
{
    static int Main(string[] args)
    {
        if(args.Length!=3)return 2;
        var store=new DeferredInstall(args[0]);
        File.WriteAllText(Path.Combine(store.Root,"test-ready"),"ready");
        return DeferredWorker.Run(store,args[1],args[2],()=>File.Exists(Path.Combine(store.Root,"test-first-running")) || File.Exists(Path.Combine(store.Root,"test-second-running")),
            p=>File.WriteAllText(Path.Combine(store.Root,"test-launch"),p.Version),200,30);
    }
}
