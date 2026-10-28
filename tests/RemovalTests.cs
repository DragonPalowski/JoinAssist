using System;
using System.IO;
using JoinAssist;

static class RemovalTests
{
    static int checks;
    static void Check(bool value, string message)
    {
        checks++;
        if (!value)
            throw new Exception("Removal: " + message);
    }

    static void Reject(Action action, string message)
    {
        bool refused = false;
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }
        catch (ArgumentException)
        {
            refused = true;
        }

        Check(refused, message);
    }

    internal static void Run()
    {
        string root = @"C:\SyntheticSSMS\Release", host = Path.Combine(root, @"Common7\IDE\SSMS.exe");
        // Serialize paths rather than depending on shell/JSON escaping.
        string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new[] { new RemovalInstance { instanceId = "abc12345", productId = "Microsoft.VisualStudio.Product.Ssms", installationPath = root, productPath = host, installationVersion = "22.11.1234.0", isComplete = true } });
        var target = ExtensionRemoval.Resolve(host, json);
        Check(target.InstanceId == "abc12345" && target.Installer == Path.Combine(root, @"Common7\IDE\VSIXInstaller.exe"), "resolve exact current SSMS instance");
        var start = ExtensionRemoval.StartInfo(target);
        Check(start.Arguments == "/uninstall:" + ExtensionRemoval.ExtensionId + " /instanceIds:abc12345", "only fixed extension ID and exact instance");
        Check(!start.Arguments.Contains("/quiet") && !start.Arguments.Contains("/force") && !start.Arguments.Contains("/admin") && start.UseShellExecute, "normal Microsoft UI without silent removal/elevation for user extension");
        Check(start.FileName == target.Installer && !start.FileName.EndsWith("SSMS.exe", StringComparison.OrdinalIgnoreCase), "extension installer, never SSMS/setup uninstall");
        Check(!target.AllUsers, "per-user is the default scope");
        target = ExtensionRemoval.Resolve(host, json, Path.Combine(root, @"Common7\IDE\Extensions\JoinAssist\JoinAssist.dll"));
        Check(target.AllUsers && ExtensionRemoval.StartInfo(target).Arguments.EndsWith(" /admin", StringComparison.Ordinal), "machine-wide copy explicitly uses installer admin mode");
        target = ExtensionRemoval.Resolve(host, json, @"C:\Users\Example\AppData\Local\Microsoft\SSMS\Extensions\JoinAssist.dll");
        Check(!target.AllUsers, "local copy does not request machine-wide uninstall");
        Reject(() => ExtensionRemoval.Resolve(@"C:\Other\Devenv.exe", json), "other host refused");
        Reject(() => ExtensionRemoval.Resolve(host, json.Replace("abc12345", "bad /quiet")), "instance argument injection refused");
        Reject(() => ExtensionRemoval.Resolve(host, json.Replace("22.11.1234.0", "23.0.0.0")), "other SSMS major refused");
        Reject(() => ExtensionRemoval.Resolve(host, json.Replace("Product.Ssms", "Product.Community")), "Visual Studio instance refused");
        Reject(() => ExtensionRemoval.Resolve(host, json.Replace("true", "false")), "incomplete installation refused");
        Reject(() => ExtensionRemoval.Resolve(host, "[]"), "missing current instance refused without broad fallback");
        Reject(() => ExtensionRemoval.Resolve(host, "[" + json.Substring(1, json.Length - 2) + "," + json.Substring(1, json.Length - 2) + "]"), "ambiguous instance refused");
        Reject(() => ExtensionRemoval.Resolve(host, "{}"), "malformed inventory refused");
        Reject(() => ExtensionRemoval.Resolve(host, new string ('x', 1048577)), "bounded inventory");
        target.InstanceId = "abc\" /quiet";
        Reject(() => ExtensionRemoval.StartInfo(target), "revalidate target before constructing launch");
        target = ExtensionRemoval.Resolve(host, json);
        target.Installer = @"C:\Other\VSIXInstaller.exe";
        Reject(() => ExtensionRemoval.StartInfo(target), "other installation installer refused");
        target = ExtensionRemoval.Resolve(host, json);
        Reject(() => ExtensionRemoval.Launch(target), "unverified or missing files refused before process launch");
        Console.WriteLine("PASS: " + checks + " uninstall target/argument checks (exact SSMS, fixed extension identity, scope, malformed data and no real process launch).");
    }
}
