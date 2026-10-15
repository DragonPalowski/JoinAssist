using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

// Read-only validation using Microsoft's VSIX Installer engine. No installation API is called.
internal static class ValidatePackage
{
    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Uso: ValidatePackage.exe pacchetto.vsix cartella-engine-installer"); return 2; }
        string engineDirectory = Path.GetFullPath(args[1]);
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string fileName = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in new[] { engineDirectory, Path.Combine(engineDirectory, "bin", "lib") })
            {
                string path = Path.Combine(dir, fileName);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        try
        {
            string enginePath = Path.Combine(engineDirectory, "Microsoft.VisualStudio.ExtensionEngine.dll");
            var assembly = Assembly.LoadFrom(enginePath);
            var type = assembly.GetType("Microsoft.VisualStudio.ExtensionManager.InstallableExtensionImpl", true);
            var constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(string), typeof(CultureInfo) }, null);
            if (constructor == null) throw new MissingMethodException("Costruttore di sola lettura del pacchetto non trovato.");
            var extension = constructor.Invoke(new object[] { Path.GetFullPath(args[0]), CultureInfo.GetCultureInfo("it-IT") });
            var identifier = type.GetProperty("Identifier").GetValue(extension, null);
            if (!object.Equals(identifier, "JoinAssist.FK.4d45475a-842b-42b3-b2f7-b392df87bdb8"))
                throw new InvalidDataException("Identità inattesa nel pacchetto.");
            var disposable = extension as IDisposable;
            if (disposable != null) disposable.Dispose();
            string engineVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(enginePath).FileVersion;
            Console.WriteLine("PASS: pacchetto accettato dal lettore VSIX Microsoft (engine " + engineVersion + "). Nessuna installazione avviata.");
            return 0;
        }
        catch (Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            Console.Error.WriteLine("Pacchetto rifiutato: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }
}
