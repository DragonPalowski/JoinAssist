using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JoinAssist
{
    // Explicit proposals only: never execute the INSERT or synthesize values from row data.
    internal static class InsertTemplates
    {
        internal static List<JoinProposal> Items(AutomaticTrigger trigger,CatalogSnapshot catalog)
        {
            var result=new List<JoinProposal>();
            bool full=trigger.Mode==CompletionMode.InsertTemplate;
            if(!full && !trigger.FillInsertColumns)return result;
            var target=trigger.Context.Tables.SingleOrDefault();
            if(target==null || target.Columns==null || target.Columns.Count==0)return result;
            var physical=target.IsLocal?null:catalog.Find(target.Schema,target.Name);
            if(target.Definition!=null || !target.IsLocal && (physical==null || physical.Kind!="U"))
            {if(full)Diagnostics.Set(trigger.Context,"JA-INSERT-01","La struttura automatica richiede una tabella con proprietà di scrittura note.");return result;}
            // A generated list must not silently include columns with unknown writeability.
            if(target.Columns.Any(c=>!c.Identity.HasValue || !c.Computed.HasValue || !c.Generated.HasValue || !c.HasDefault.HasValue))
            {if(full)Diagnostics.Set(trigger.Context,"JA-INSERT-01","Proprietà delle colonne non disponibili: usa il completamento delle singole colonne.");return result;}
            if(target.Columns.GroupBy(c=>c.Name,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()!=1))return result;
            var columns=target.Columns.Where(c=>c.Identity!=true && c.Computed!=true && c.Generated!=true &&
                !string.Equals(c.TypeName,"timestamp",StringComparison.OrdinalIgnoreCase) && !string.Equals(c.TypeName,"rowversion",StringComparison.OrdinalIgnoreCase) &&
                (PreferenceStore.Read.InsertIncludeDefaults || c.HasDefault!=true) && !trigger.Context.AssignedColumns.Contains(c.Name)).ToList();
            if(columns.Count==0)
            {if(full)Diagnostics.Set(trigger.Context,"JA-INSERT-02","Nessuna colonna da includere: controlla colonne generate e preferenza sui valori predefiniti.");return result;}
            string text;int? caret=null;
            if(full)
            {
                string nl=trigger.InsertNewLine,indent=trigger.InsertIndent??"",pad=indent+"    ";
                string first="("+nl+pad+string.Join(","+nl+pad,columns.Select(c=>SqlName.Format(c.Name)))+nl+indent+")"+nl+indent+PreferenceStore.Keyword("VALUES")+" ("+nl+pad;
                var parameters=Parameters(columns.Select(c=>c.Name));
                text=first+string.Join(","+nl+pad,parameters)+nl+indent+")"+(trigger.InsertTerminator?";":"");
                caret=first.Length;
            }
            else text=string.Join(", ",columns.Select(c=>SqlName.Format(c.Name)));
            string title=full?"Prepara INSERT":"Inserisci colonne mancanti";
            result.Add(new JoinProposal{IsInsertTemplate=true,CompletionText=text,CaretOffset=caret,SearchName=title,Category="Struttura INSERT",ContextRank=-100,
                DisplayText=title+"  |  "+columns.Count+" colonne · ordine della tabella"+(full?" · parametri @ da compilare":" · valori esistenti mantenuti")});
            return result;
        }
        static List<string> Parameters(IEnumerable<string> names)
        {
            var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var result=new List<string>();
            foreach(string name in names)
            {
                var text=new StringBuilder();
                foreach(char c in name)text.Append(char.IsLetterOrDigit(c)||c=='_'?c:'_');
                string stem=text.Length==0?"value":text.ToString();
                if(!char.IsLetter(stem[0]) && stem[0]!='_')stem="col_"+stem;
                if(stem.Length>110)stem=stem.Substring(0,110);
                string parameter="@"+stem;int suffix=2;
                while(!used.Add(parameter))parameter="@"+stem+"_"+suffix++;
                result.Add(parameter);
            }
            return result;
        }
    }
}
