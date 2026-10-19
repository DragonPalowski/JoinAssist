using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;

namespace JoinAssist
{
    internal static class MenuPlacement
    {
        const string Allowed=@"Menus\AllowedExtensionMenus";
        const string Marker="MainMenuDefaultApplied";
        internal static void Initialize(IServiceProvider services)
        {
            try
            {
                var store=new ShellSettingsManager(services).GetWritableSettingsStore(SettingsScope.UserSettings);
                if(store.CollectionExists("JoinAssist") && store.PropertyExists("JoinAssist",Marker))return;
                // Match the SSMS 22 Customize Menu schema. Unknown hosts keep their native placement.
                if(!store.CollectionExists(@"Menus\UnexpectedTopLevelMenus"))return;
                Apply(new ShellStore(store));
            }
            catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException || ex is COMException || ex is System.Security.SecurityException)
            { /* Menu placement must never prevent editor completion or native commands from loading. */ }
        }
        internal interface IStore
        {
            bool Initialized {get;}
            string ReadAllowed();
            void WriteAllowed(string value);
            void MarkInitialized();
        }
        internal static void Apply(IStore store)
        {
            if(store.Initialized)return;
            string old=store.ReadAllowed()??"";
            var id=JoinAssistPackage.Commands;
            bool exists=old.Split(';').Any(entry=>{
                var parts=entry.Split(',');Guid guid;uint command;
                return parts.Length==2 && Guid.TryParse(parts[0],out guid) && guid==id && uint.TryParse(parts[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out command) && command==0x1000;
            });
            if(!exists)store.WriteAllowed(old+(old.Length==0 || old.EndsWith(";",StringComparison.Ordinal)?"":";")+id.ToString("B")+",4096");
            store.MarkInitialized();
        }
        sealed class ShellStore : IStore
        {
            readonly WritableSettingsStore store;
            internal ShellStore(WritableSettingsStore value){store=value;}
            public bool Initialized {get{return store.CollectionExists("JoinAssist") && store.PropertyExists("JoinAssist",Marker);}}
            public string ReadAllowed(){return store.CollectionExists(Allowed) && store.PropertyExists(Allowed,"")?store.GetString(Allowed,""):"";}
            public void WriteAllowed(string value){if(!store.CollectionExists(Allowed))store.CreateCollection(Allowed);store.SetString(Allowed,"",value);}
            public void MarkInitialized(){if(!store.CollectionExists("JoinAssist"))store.CreateCollection("JoinAssist");store.SetBoolean("JoinAssist",Marker,true);}
        }
    }
}
