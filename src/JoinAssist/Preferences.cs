using System;
using System.IO;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace JoinAssist
{
    public sealed class Preferences
    {
        public bool Automatic { get; set; } = true;
        public bool AcceptEnter { get; set; } = true;
        public int DelayMilliseconds { get; set; } = 220;
        public int CacheMinutes { get; set; } = 2;
        public string Shortcut { get; set; } = "Ctrl+Alt+Space";
        public bool AlwaysSchema { get; set; }
        public bool QuoteNames { get; set; }
        public bool AutomaticAliases { get; set; }
        public bool UppercaseKeywords { get; set; } = true;
        public bool JoinPaths { get; set; } = true;
        public bool InsertIncludeDefaults { get; set; } = true;
        public SearchTolerance SearchTolerance { get; set; } = SearchTolerance.Balanced;
        public bool RememberChoices { get; set; } = true;
        public bool KeywordCompletion { get; set; } = true;
        public bool FilterTemplates { get; set; } = true;
        public bool QuickActionsShortcut {get;set;}
        public List<PersonalRule> Rules {get;set;} = new List<PersonalRule>();
        public Preferences Copy() { var copy=(Preferences)MemberwiseClone();copy.Rules=(Rules??new List<PersonalRule>()).Select(r=>r.Copy()).ToList();return copy; }
        public void Validate()
        {
            PersonalRules.Validate(Rules);if(Rules==null)Rules=new List<PersonalRule>();
            if (!Enum.IsDefined(typeof(SearchTolerance),SearchTolerance)) throw new ArgumentException("Livello di tolleranza non valido.");
            if (DelayMilliseconds < 70 || DelayMilliseconds > 2000) throw new ArgumentException("Il ritardo deve essere tra 70 e 2000 ms.");
            if (CacheMinutes < 1 || CacheMinutes > 15) throw new ArgumentException("La durata della cache deve essere tra 1 e 15 minuti.");
            if (Shortcut != "Ctrl+Alt+Space" && Shortcut != "Ctrl+Shift+J" && Shortcut != "Ctrl+Alt+J") throw new ArgumentException("Scorciatoia non supportata.");
        }
    }

    public static class PreferenceStore
    {
        static Preferences current = new Preferences();
        static bool initialized;
        public static Preferences Current { get { return current.Copy(); } }
        // Internal readers never mutate this snapshot. Apply replaces it atomically;
        // public callers and settings drafts still receive independent deep copies.
        internal static Preferences Read { get { return current; } }
        public static event EventHandler Changed;
        static string UserPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JoinAssist", "preferences.json"); } }
        public static string Initialize()
        {
            if (initialized) return null;
            initialized = true;
            try { current = Load(UserPath); return null; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is OverflowException)
            { return "[JA-SETTINGS-01] Preferenze non leggibili: usati i valori predefiniti."; }
        }
        public static Preferences Load(string path)
        {
            if (!File.Exists(path)) return new Preferences();
            if (new FileInfo(path).Length > 1048576) throw new InvalidOperationException("File preferenze troppo grande.");
            var value = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(path));
            if (value == null) throw new InvalidOperationException("Preferenze non valide.");
            value.Validate(); return value;
        }
        public static void Write(string path, Preferences value)
        {
            value.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(value));
                if (File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static void Save(Preferences value) { Write(UserPath,value); Apply(value); }
        internal static void Apply(Preferences value)
        { value.Validate(); current = value.Copy(); var changed = Changed; if (changed != null) changed(null,EventArgs.Empty); }
        internal static string Keyword(string word) { return current.UppercaseKeywords ? word.ToUpperInvariant() : word.ToLowerInvariant(); }
    }
}
