using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;

namespace JoinAssist
{
    // This is the only adapter that depends on SSMS internals. It fails closed on unknown versions.
    public sealed class SsmsConnection : IDisposable
    {
        public DbConnection Connection;
        public string Server, Database;
        object editor;
        DbConnection originalConnection;
        object nativeSessionId;
        public static SsmsConnection Capture(string bufferText)
        {
            var editorAssembly = Load("SQLEditors");
            var type = editorAssembly.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptEditorControl", true);
            var active = type.GetMethod("GetActiveScriptEditorControl", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
            if (active == null || !active.GetType().FullName.EndsWith(".SqlScriptEditorControl", StringComparison.Ordinal))
                throw new InvalidOperationException("Apri una scheda di query SQL collegata a un database.");
            if (Bool(active, "IsSqlCmdModeEnabled") || Bool(active, "IsMultipleServerConnection"))
                throw new InvalidOperationException("Disattiva SQLCMD e usa una connessione a un solo server per questa prova.");
            var editorText = Get(active, "EditorText") as string;
            if (editorText == null || editorText.Replace("\r\n", "\n") != bufferText.Replace("\r\n", "\n"))
                throw new InvalidOperationException("L'editor attivo non corrisponde al testo della query. Clicca nella query SQL e riprova.");
            string database = Get(active, "CurrentDB") as string;
            if (string.IsNullOrEmpty(database)) throw new InvalidOperationException("Seleziona il database nella scheda della query.");
            var ui = Get(active, "Connection");
            if (ui == null) throw new InvalidOperationException("Collega la scheda della query al server e riprova.");
            // This is the actual query-tab connection. UIConnectionInfo is only a description and may
            // omit the Entra token held by the live SqlClient instance (SSMS 22).
            var original = GetEditorConnection(active);
            if (original == null) throw new InvalidOperationException("JoinAssist non trova la connessione SQL della scheda in questa versione di SSMS (JA-CONN-01).");
            var connection = CloneEditorConnection(original, ui);
            if (connection == null) throw new InvalidOperationException("Questa connessione non espone un provider SQL compatibile. Nessun tentativo di connessione alternativo è stato effettuato.");
            try
            {
                if (connection.State != ConnectionState.Closed) throw new InvalidOperationException("SSMS non ha restituito una nuova connessione chiusa. Operazione annullata.");
                ConfigureConnection(connection, database);
                return Bind(active, original, connection, database);
            }
            catch { connection.Dispose(); throw; }
        }
        internal static SsmsConnection Bind(object sourceEditor, DbConnection original, DbConnection metadata, string database)
        {
            return new SsmsConnection { Connection = metadata, Server = original.DataSource, Database = database,
                editor = sourceEditor, originalConnection = original, nativeSessionId = Get(original, "ClientConnectionId") };
        }
        internal static DbConnection GetEditorConnection(object active)
        {
            // Read only the known protected field declared by the SSMS base editor class.
            for (var type = active == null ? null : active.GetType(); type != null; type = type.BaseType)
            {
                if (type.FullName != "Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptAndResultsEditorControl") continue;
                var field = type.GetField("m_connection", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                return field == null ? null : field.GetValue(active) as DbConnection;
            }
            return null;
        }
        internal static DbConnection CloneEditorConnection(DbConnection original, object ui)
        {
            if (original == null || original.GetType().FullName != "Microsoft.Data.SqlClient.SqlConnection")
                throw new InvalidOperationException("Il provider della scheda non è supportato da JoinAssist (JA-CONN-02).");
            // SqlClient.Clone preserves credentials, the access token and its callback, including values
            // hidden from ConnectionString after Open. Never open, change or dispose the original.
            var clone = ((ICloneable)original).Clone() as DbConnection;
            if (clone == null || ReferenceEquals(clone, original))
                throw new InvalidOperationException("Impossibile copiare separatamente la connessione SQL (JA-CONN-03).");
            try
            {
                var builder = new DbConnectionStringBuilder { ConnectionString = clone.ConnectionString };
                object auth;
                bool nativeEntra = builder.TryGetValue("Authentication", out auth) &&
                    Convert.ToString(auth).Replace(" ", "").StartsWith("ActiveDirectory", StringComparison.OrdinalIgnoreCase);
                bool tokenAvailable = !string.IsNullOrEmpty(Get(clone, "AccessToken") as string) || Get(clone, "AccessTokenCallback") != null;
                var authType = Get(ui, "AuthenticationType");
                if (authType is int && (int)authType > 1 && !nativeEntra && !tokenAvailable)
                    throw new InvalidOperationException("La connessione effettiva della scheda non espone l'autenticazione Entra a JoinAssist (JA-CONN-04). Segnala questo codice: non occorre ripetere il login.");
                return clone;
            }
            catch { clone.Dispose(); throw; }
        }
        internal static void ConfigureConnection(DbConnection connection, string database)
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connection.ConnectionString };
            builder["Initial Catalog"] = database;
            builder["Application Name"] = "JoinAssist";
            builder["Connect Timeout"] = 10;
            connection.ConnectionString = builder.ConnectionString;
        }
        public bool IsStillActive()
        {
            return InvalidationReason() == null;
        }
        public string InvalidationReason()
        {
            // Validate the editor that owns the buffer, not the global foreground window: a modal
            // dialog/popup may own focus. Callers separately check the view lifetime and its SQL text.
            // UIConnectionInfo.Copy assigns a fresh Id on EVERY read, so that Id is not a session key.
            if (!ReferenceEquals(GetEditorConnection(editor), originalConnection))
                return "La connessione della scheda è stata sostituita (JA-CONTEXT-02). Riapri i suggerimenti.";
            if (originalConnection == null || originalConnection.State != ConnectionState.Open)
                return "La scheda SQL è stata scollegata (JA-CONTEXT-03). Riapri i suggerimenti dopo il collegamento.";
            if (!object.Equals(Get(originalConnection, "ClientConnectionId"), nativeSessionId))
                return "La sessione SQL è stata riaperta (JA-CONTEXT-04). Ricarica le relazioni.";
            if (!string.Equals(originalConnection.Database, Database, StringComparison.Ordinal) ||
                !string.Equals(originalConnection.DataSource, Server, StringComparison.Ordinal))
                return "Il server o il database della scheda è cambiato (JA-CONTEXT-05). Ricarica le relazioni.";
            return null;
        }
        static Assembly Load(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name) ?? Assembly.Load(name);
        }
        static object Get(object obj, string name)
        {
            if (obj == null) return null;
            var property = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property == null ? null : property.GetValue(obj, null);
        }
        static bool Bool(object obj, string name) { var value = Get(obj, name); return value is bool && (bool)value; }
        public void Dispose() { Connection.Dispose(); }
    }
}
