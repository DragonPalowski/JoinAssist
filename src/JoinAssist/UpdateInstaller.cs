using System;
using System.Diagnostics;
using System.IO;

namespace JoinAssist
{
    internal static class UpdateInstaller
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Only the bundled worker entry point. No generic command execution or test switch.
            try
            {
                if(args.Length!=4 || args[0]!="--wait" || !string.Equals(Path.GetFullPath(args[1]),Path.GetFullPath(DeferredInstall.DefaultRoot),StringComparison.OrdinalIgnoreCase))return 2;
                var store=new DeferredInstall(args[1]);
                var plan=store.Locked(()=>store.Read());Version version;
                if(plan==null || plan.Id!=args[2] || plan.RequestHash!=args[3] || !Version.TryParse(plan.Version,out version) || version<=typeof(UpdateInstaller).Assembly.GetName().Version)return 2;
                return DeferredWorker.Run(store,args[2],args[3],DeferredInstall.SsmsRunning,p=>{
                    using(var installer=Process.Start(new ProcessStartInfo(p.Installer,"\""+p.Package+"\""){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(p.Installer)}))
                    {if(installer==null)throw new InvalidOperationException("Installer Microsoft non avviato.");}
                });
            }
            catch(Exception ex) when(ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is System.Security.SecurityException)
            {
                return 1;
            }
        }
    }
}
