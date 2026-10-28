using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace JoinAssist
{
    internal sealed class RemovalTarget
    {
        internal string Host, Installer, InstanceId, InstallerHash;
        internal bool AllUsers;
    }

    internal sealed class RemovalInstance
    {
        public string instanceId { get; set; }
        public string installationPath { get; set; }
        public string productId { get; set; }
        public string productPath { get; set; }
        public string installationVersion { get; set; }
        public bool isComplete { get; set; }
    }

    internal static class ExtensionRemoval
    {
        internal const string ExtensionId = "JoinAssist.FK.4d45475a-842b-42b3-b2f7-b392df87bdb8";
        static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }

        internal static RemovalTarget Resolve(string host, string inventory, string extensionLocation = null)
        {
            if (string.IsNullOrWhiteSpace(host) || !Path.IsPathRooted(host) || !string.Equals(Path.GetFileName(host), "Ssms.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Apri la disinstallazione dal menu JoinAssist dentro SSMS 22.");
            string ide = Path.GetDirectoryName(Path.GetFullPath(host));
            string root = Directory.GetParent(ide)?.Parent?.FullName;
            if (root == null || !Same(ide, Path.Combine(root, "Common7", "IDE")))
                throw new InvalidOperationException("Percorso di SSMS non riconosciuto.");
            if (string.IsNullOrWhiteSpace(inventory) || inventory.Length > 1048576)
                throw new InvalidOperationException("Installazioni SSMS non disponibili.");
            RemovalInstance[] instances;
            try
            {
                instances = new JavaScriptSerializer
                {
                    MaxJsonLength = 1048576,
                    RecursionLimit = 32
                }.Deserialize<RemovalInstance[]>(inventory);
            }
            catch (Exception ex)when (ex is ArgumentException || ex is InvalidOperationException || ex is MissingMethodException || ex is FormatException || ex is InvalidCastException)
            {
                throw new InvalidOperationException("Elenco delle installazioni non valido. Nessuna rimozione avviata.");
            }

            var matches = (instances ?? new RemovalInstance[0]).Where(i => i != null && Same(i.installationPath, root) && Same(i.productPath, host) && string.Equals(i.productId, "Microsoft.VisualStudio.Product.Ssms", StringComparison.OrdinalIgnoreCase) && i.isComplete).ToList();
            Version version;
            if (matches.Count != 1 || !Version.TryParse(matches[0].installationVersion, out version) || version.Major != 22 || !Regex.IsMatch(matches[0].instanceId ?? "", @"\A[a-zA-Z0-9_-]{1,64}\z"))
                throw new InvalidOperationException("Non è stato possibile identificare univocamente questa installazione di SSMS 22. Nessuna disinstallazione avviata.");
            return new RemovalTarget
            {
                Host = Path.GetFullPath(host),
                Installer = Path.Combine(ide, "VSIXInstaller.exe"),
                InstanceId = matches[0].instanceId,
                AllUsers = !string.IsNullOrEmpty(extensionLocation) && DeferredInstall.Under(extensionLocation, Path.Combine(ide, "Extensions"))
            };
        }

        internal static ProcessStartInfo StartInfo(RemovalTarget target)
        {
            if (target == null || !Path.IsPathRooted(target.Host ?? "") || !string.Equals(Path.GetFileName(target.Host), "Ssms.exe", StringComparison.OrdinalIgnoreCase) || !Same(target.Installer, Path.Combine(Path.GetDirectoryName(target.Host), "VSIXInstaller.exe")) || !Regex.IsMatch(target.InstanceId ?? "", @"\A[a-zA-Z0-9_-]{1,64}\z"))
                throw new InvalidOperationException("Destinazione della disinstallazione non valida.");
            // The official UI retains its own confirmations. Never target SSMS itself,
            // invoke a shell command, use quiet/force flags, or delete extension folders.
            return new ProcessStartInfo(target.Installer, "/uninstall:" + ExtensionId + " /instanceIds:" + target.InstanceId + (target.AllUsers ? " /admin" : ""))
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(target.Installer)
            };
        }

        internal static async Task<RemovalTarget> DiscoverAsync()
        {
            string host = Process.GetCurrentProcess().MainModule.FileName;
            if (!string.Equals(Path.GetFileName(host), "Ssms.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Apri questo comando dentro SSMS.");
            if (Environment.GetCommandLineArgs().Any(s => s.TrimStart('/', '-').StartsWith("rootsuffix", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("La disinstallazione guidata non gestisce profili sperimentali RootSuffix.");
            string locator = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (!File.Exists(locator))
                throw new InvalidOperationException("Strumento Microsoft per identificare SSMS non trovato. Nessuna rimozione eseguita.");
            DeferredInstall.NoLinks(locator);
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo(locator, "-all -prerelease -products Microsoft.VisualStudio.Product.SSMS -format json -utf8")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            }

            )
            {
                if (!process.Start())
                    throw new InvalidOperationException("Lettura delle installazioni SSMS non avviata.");
                var output = ReadInventory(process.StandardOutput);
                if (await Task.WhenAny(output, Task.Delay(10000)) != output)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    throw new InvalidOperationException("Identificazione di SSMS scaduta. Riprova.");
                }

                string json = await output;
                if (!process.WaitForExit(2000) || process.ExitCode != 0)
                    throw new InvalidOperationException("Identificazione di SSMS non riuscita.");
                var target = Resolve(host, json, typeof(ExtensionRemoval).Assembly.Location);
                if (!File.Exists(target.Installer))
                    throw new InvalidOperationException("Installer delle estensioni Microsoft non trovato per questa installazione SSMS.");
                target.InstallerHash = DeferredInstall.HashFile(target.Installer);
                return target;
            }
        }

        static async Task<string> ReadInventory(StreamReader reader)
        {
            var text = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                if (text.Length + count > 1048576)
                    throw new InvalidOperationException("Elenco installazioni troppo grande.");
                text.Append(buffer, 0, count);
            }

            return text.ToString();
        }

        internal static void Launch(RemovalTarget target)
        {
            var start = StartInfo(target);
            if (!File.Exists(target.Host) || string.IsNullOrEmpty(target.InstallerHash))
                throw new InvalidOperationException("Installazione cambiata. Riapri Disinstalla JoinAssist.");
            using (var locked = new FileStream(target.Installer, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (DeferredInstall.HashFile(target.Installer) != target.InstallerHash)
                    throw new InvalidOperationException("L'installer è cambiato. Riapri Disinstalla JoinAssist.");
                using (var process = Process.Start(start))
                    if (process == null)
                        throw new InvalidOperationException("Installer Microsoft non avviato.");
            }
        }
    }
}
