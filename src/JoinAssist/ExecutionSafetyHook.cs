using System;
using System.Runtime.InteropServices;
using EnvDTE;

namespace JoinAssist
{
    internal sealed class ExecutionSafetyHook : IDisposable
    {
        readonly CommandEvents events;
        readonly Func<bool> allow;
        readonly Func<string,int,bool> matches;
        bool checking;
        internal ExecutionSafetyHook(CommandEvents source,Func<string,int,bool> isExecution,Func<bool> check)
        {events=source;matches=isExecution;allow=check;events.BeforeExecute+=BeforeExecute;}
        internal void BeforeExecute(string group,int id,object input,object output,ref bool cancel)
        {
            if(cancel || !PreferenceStore.Read.ExecutionSafety)return;
            if(!matches(group,id))return;
            if(checking){cancel=true;return;}
            checking=true;
            // Never launch/replay a command ourselves: authorize only the original dispatch.
            try{cancel=!allow();}
            catch(Exception)
            {cancel=true;System.Windows.MessageBox.Show("Controllo JoinAssist non completato: esecuzione annullata. Riprova o verifica l'impostazione di controllo UPDATE/DELETE.","JoinAssist");}
            finally{checking=false;}
        }
        public void Dispose(){try{events.BeforeExecute-=BeforeExecute;}catch(COMException){}}
    }
}
