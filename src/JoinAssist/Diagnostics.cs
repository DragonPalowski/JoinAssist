using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace JoinAssist
{
    public static class Diagnostics
    {
        public static bool IsProblem(string status)
        {
            var code=Regex.Match(status??"","JA-[A-Z]+-[0-9]{2}").Value;
            return code.Length>0 && code!="JA-MATCH-01" && code!="JA-DML-01";
        }
        public static void Set(QueryContext context,string code,string text) { context.DiagnosticCode=code;context.Diagnostic="["+code+"] "+text; }
        public static void Explain(AutomaticTrigger trigger,IList<TableUse> sources,IList<JoinProposal> items)
        {
            if(items.Count>0)return;
            if(trigger.Qualifier.Any(LocalTables.IsLocal) || sources.Any(s=>s.IsLocal && s.Columns!=null && s.Columns.Count==0))
                Set(trigger.Context,"JA-LOCAL-01","Definizione locale non disponibile: dichiara la tabella prima del punto corrente nello script.");
            else if(sources.Count==0)
                Set(trigger.Context,"JA-SCOPE-01",trigger.Qualifier.Length>0?"Alias o tabella non riconosciuti nel contesto corrente.":"FROM non riconosciuto nel contesto corrente.");
            else Set(trigger.Context,"JA-COLUMNS-01","Nessuna colonna risolvibile: verifica la definizione della sorgente e i permessi sui metadati.");
        }
        public static string Report(string status)
        {
            var code=Regex.Match(status??"","JA-[A-Z]+-[0-9]{2}");
            string host="non disponibile";
            try { host=Process.GetCurrentProcess().MainModule.FileVersionInfo.ProductVersion??host; } catch (System.ComponentModel.Win32Exception) { }
            return "JoinAssist "+typeof(Diagnostics).Assembly.GetName().Version+Environment.NewLine+"Host version: "+host+Environment.NewLine+
                "Diagnostic code: "+(code.Success?code.Value:"nessuno")+Environment.NewLine+"Query, nomi server/database, account e credenziali esclusi.";
        }
    }
}
