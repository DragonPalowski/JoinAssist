using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;

namespace JoinAssist
{
    [PackageRegistration(UseManagedResourcesOnly=true,AllowsBackgroundLoading=true)]
    [ProvideMenuResource("Menus.ctmenu",1)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string,PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string,PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageId)]
    public sealed class JoinAssistPackage : AsyncPackage
    {
        public const string PackageId="9abeed86-c005-4882-b0d9-8a3ec7fd4a90";
        internal static readonly Guid Commands=new Guid("a2cba72d-020e-4a42-9000-c21be31bf419");
        IVsTextManager textManager;
        IVsEditorAdaptersFactoryService adapters;
        Window preferences,updates,diagnostic,workspace;
        protected override async Task InitializeAsync(CancellationToken cancellationToken,IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            PreferenceStore.Initialize();
            await Task.Run(()=>CompletionHistory.Initialize(),cancellationToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            MenuPlacement.Initialize(this);
            textManager=await GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
            var components=await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
            adapters=components==null?null:components.GetService<IVsEditorAdaptersFactoryService>();
            var service=await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if(service==null)return;
            Add(service,0x0100,()=>{var value=PreferenceStore.Current;value.Automatic=!value.Automatic;PreferenceStore.Save(value);},c=>c.Checked=PreferenceStore.Read.Automatic);
            Add(service,0x0101,()=>ActiveEditor()?.Suggest(),c=>{c.Enabled=ActiveEditor()!=null;c.Text="Mostra suggerimenti\t"+PreferenceStore.Read.Shortcut;});
            Add(service,0x0102,()=>ActiveEditor()?.OpenJoin(),c=>c.Enabled=ActiveEditor()!=null);
            Add(service,0x0103,()=>ActiveEditor()?.Refresh(),c=>c.Enabled=ActiveEditor()!=null);
            Add(service,0x0104,()=>Show(ref preferences,()=>new PreferencesDialog(()=>ActiveEditor()?.CaptureForRules())));
            Add(service,0x0105,()=>Show(ref updates,()=>new UpdateDialog()));
            Add(service,0x0106,()=>Show(ref diagnostic,()=>new DiagnosticDialog(()=>ActiveEditor()?.DiagnosticStatus??"Nessuna query SQL attiva.",()=>ActiveEditor()?.Suggest(),()=>ActiveEditor()?.Refresh())),c=>c.Text=Diagnostics.IsProblem(ActiveEditor()?.LastStatus)?"Diagnostica… (problema rilevato)":"Diagnostica…");
            Add(service,0x0107,()=>ActiveEditor()?.SchemaTool(true),c=>c.Enabled=ActiveEditor()!=null);
            Add(service,0x0108,()=>ActiveEditor()?.SchemaTool(false),c=>c.Enabled=ActiveEditor()!=null);
            Add(service,0x0109,()=>ActiveEditor()?.DeleteDependencies(),c=>c.Enabled=ActiveEditor()!=null);
            Add(service,0x010a,()=>OpenWorkspace(),c=>c.Enabled=ActiveEditor()!=null);
            Add(service,0x010b,()=>OpenWorkspace("Schema affiancabile"),c=>c.Enabled=ActiveEditor()!=null);
            JoinEditorController.WorkspaceRequested+=OpenWorkspaceShortcut;
        }
        void OpenWorkspaceShortcut(){OpenWorkspace();}
        void OpenWorkspace(string action=null)
        {
            Show(ref workspace,()=>new WorkflowDialog(()=>{var editor=ActiveEditor();if(editor==null)throw new InvalidOperationException("Apri una query SQL.");return editor.CaptureWorkflow(()=>ReferenceEquals(ActiveEditor(),editor));},()=>ActiveEditor()?.Suggest(),()=>ActiveEditor()?.Refresh(),path=>VsShellUtilities.OpenDocument(this,path),action));
            if(action!=null)(workspace as WorkflowDialog)?.SelectAction(action);
        }
        internal static void Add(OleMenuCommandService service,int id,Action execute,Action<OleMenuCommand> status=null)
        {
            var command=new OleMenuCommand((s,e)=>{
                try{execute();}
                catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is ArgumentException)
                {MessageBox.Show("Impossibile completare il comando JoinAssist ("+ex.GetType().Name+"). Riapri il menu e riprova.","JoinAssist",MessageBoxButton.OK,MessageBoxImage.Information);}
            },new CommandID(Commands,id));
            if(status!=null)command.BeforeQueryStatus+=(s,e)=>status(command);
            service.AddCommand(command);
        }
        JoinEditorController ActiveEditor()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if(textManager==null || adapters==null)return null;
            IVsTextView native;
            if(textManager.GetActiveView(0,null,out native)<0 || native==null)return null;
            var view=adapters.GetWpfTextView(native);
            return JoinEditorController.FromView(view);
        }
        static void Show(ref Window current,Func<Window> create)
        {
            if(current!=null && current.IsVisible){current.Activate();return;}
            current=create();UiTheme.Owner(current);current.Show();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing){JoinEditorController.WorkspaceRequested-=OpenWorkspaceShortcut;preferences?.Close();updates?.Close();diagnostic?.Close();workspace?.Close();}
            base.Dispose(disposing);
        }
    }
}
