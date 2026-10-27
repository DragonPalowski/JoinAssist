using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Security.Cryptography;
using System.Threading;

namespace JoinAssist
{
    public sealed class SavedQuery
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Query";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Sql { get; set; } = "";

        public override string ToString()
        {
            return Name + " · " + Category + " · " + (string.IsNullOrEmpty(Database) ? "tutti i database" : Database);
        }
    }

    public static class QueryLibrary
    {
        internal static string UserPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JoinAssist", "favorites.json");
            }
        }

        public static List<SavedQuery> Load(string path)
        {
            if (!File.Exists(path))
                return new List<SavedQuery>();
            if (new FileInfo(path).Length > 2097152)
                throw new ArgumentException("Raccolta troppo grande (2 MiB).");
            var list = new JavaScriptSerializer
            {
                MaxJsonLength = 2097152
            }.Deserialize<List<SavedQuery>>(File.ReadAllText(path));
            Validate(list);
            return list;
        }

        public static void Validate(List<SavedQuery> list)
        {
            if (list == null || list.Count > 200 || list.Any(q => q == null) || list.Select(q => q.Id).Distinct().Count() != list.Count)
                throw new ArgumentException("Massimo 200 preferiti con identità distinte.");
            foreach (var q in list)
                if (string.IsNullOrWhiteSpace(q.Id) || q.Id.Length > 64 || string.IsNullOrWhiteSpace(q.Name) || q.Name.Length > 100 || (q.Description ?? "").Length > 500 || (q.Category ?? "").Length > 60 || (q.Server ?? "").Length > 256 || (q.Database ?? "").Length > 128 || string.IsNullOrWhiteSpace(q.Sql) || q.Sql.Length > 65536)
                    throw new ArgumentException("Preferito non valido: nome fino a 100 caratteri e SQL fino a 64 KiB.");
            if (list.Sum(q => (long)q.Sql.Length) > 524288)
                throw new ArgumentException("Massimo 512 KiB totali di SQL nei preferiti.");
        }

        public static string Stamp(string path)
        {
            if (!File.Exists(path))
                return "missing";
            using (var sha = SHA256.Create())
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return BitConverter.ToString(sha.ComputeHash(file));
        }

        public static void Save(string path, List<SavedQuery> list, string expected = null)
        {
            Validate(list);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (var sha = SHA256.Create())
            using (var mutex = new Mutex(false, "Local\\JoinAssist.Favorites." + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-", "")))
            {
                bool held = false;
                try
                {
                    try
                    {
                        held = mutex.WaitOne(1000);
                    }
                    catch (AbandonedMutexException)
                    {
                        held = true;
                    }

                    if (!held)
                        throw new IOException("Raccolta occupata: riprova.");
                    if (expected != null && Stamp(path) != expected)
                        throw new InvalidOperationException("La raccolta è cambiata in un'altra finestra. Riapri Preferiti prima di salvare.");
                    string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(temp, new JavaScriptSerializer { MaxJsonLength = 2097152 }.Serialize(list), new UTF8Encoding(false));
                        if (File.Exists(path))
                            File.Replace(temp, path, null);
                        else
                            File.Move(temp, path);
                    }
                    finally
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }
                }
                finally
                {
                    if (held)
                        mutex.ReleaseMutex();
                }
            }
        }

        public static bool InScope(SavedQuery q, string server, string database)
        {
            return (string.IsNullOrEmpty(q.Server) || string.Equals(q.Server, server, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrEmpty(q.Database) || q.Database == database);
        }
    }
}
