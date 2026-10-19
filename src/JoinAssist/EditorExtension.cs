using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using System.Threading;
using System.Threading.Tasks;

namespace JoinAssist
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [Name("JoinAssist.Editor")]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    public sealed class JoinEditorProvider : IWpfTextViewCreationListener
    {
        [Import] public ITextDocumentFactoryService Documents = null;
        [Import(AllowDefault=true)] public IVsEditorAdaptersFactoryService Adapters = null;
        [Import(AllowDefault=true)] public ICompletionBroker CompletionBroker = null;
        public void TextViewCreated(IWpfTextView view)
        {
            ITextDocument doc;
            bool sql=view.TextDataModel.ContentType.TypeName.IndexOf("sql",StringComparison.OrdinalIgnoreCase)>=0;
            if(!sql && Documents.TryGetTextDocument(view.TextBuffer,out doc))sql=string.Equals(Path.GetExtension(doc.FilePath),".sql",StringComparison.OrdinalIgnoreCase);
            if(sql && !view.Properties.ContainsProperty(typeof(JoinEditorController)))
                view.Properties.AddProperty(typeof(JoinEditorController),new JoinEditorController(view,Adapters,CompletionBroker));
        }
    }
    internal sealed class JoinEditorController : IDisposable
    {
        readonly IWpfTextView view;
        readonly AutomaticCompletion automatic;
        bool disposed,busy;
        CancellationTokenSource toolCancel;
        Window toolDialog;
        internal bool IsDisposed { get { return disposed; } }
        internal string LastStatus { get; private set; }
        internal static JoinEditorController FromView(IWpfTextView view)
        {
            JoinEditorController editor;
            return view!=null && !view.IsClosed && view.Properties.TryGetProperty(typeof(JoinEditorController),out editor) && !editor.IsDisposed?editor:null;
        }
        internal JoinEditorController(IWpfTextView textView,IVsEditorAdaptersFactoryService adapters,ICompletionBroker broker)
        {
            view=textView;LastStatus=PreferenceStore.Initialize()??"JoinAssist pronto.";
            automatic=new AutomaticCompletion(view,adapters,broker,text=>LastStatus=text);
            view.Closed+=OnClosed;
        }
        internal void Suggest(){if(!disposed)automatic.RequestCompletion();}
        internal void Refresh(){if(!disposed)automatic.RefreshMetadata();}
        internal async void SchemaTool(bool expand)
        {
            if(busy || disposed || view.IsClosed)return;
            busy=true;var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(20));toolCancel=cancel;
            SsmsConnection capture=null;
            try
            {
                view.VisualElement.Focus();
                var snapshot=view.TextBuffer.CurrentSnapshot;string sql=snapshot.GetText();int caret=view.Caret.Position.BufferPosition.Position;
                capture=SsmsConnection.Capture(sql);LastStatus="Lettura dei metadati…";
                var expansion=expand?await Task.Run(()=>StarExpansion.Create(sql,caret)):null;
                var context=expand?expansion.Context:await Task.Run(()=>SqlAnalysis.ReadScope(sql,caret));
                string[] reference=expand?null:await Task.Run(()=>SchemaInspector.ReferenceAt(sql,caret));
                if(expand)
                {
                    var columns=await Task.Run(()=>expansion.LoadAsync(capture.Connection,cancel.Token,capture.CacheScope));
                    capture.Dispose();
                    if(!view.HasAggregateFocus)throw new InvalidOperationException("La scheda attiva è cambiata. Riapri il comando nella query desiderata.");
                    EnsureToolCurrent(snapshot,caret,capture);
                    var dialog=new StarDialog(columns);toolDialog=dialog;UiTheme.Owner(dialog);
                    if(dialog.ShowDialog()==true)
                    {
                        EnsureToolCurrent(snapshot,caret,capture);
                        using(var edit=view.TextBuffer.CreateEdit())
                        {
                            if(!edit.Replace(context.ReplaceStart,context.ReplaceLength,dialog.Insertion) || edit.Apply()==snapshot)throw new InvalidOperationException("Modifica annullata dall'editor.");
                        }
                        view.Caret.MoveTo(new SnapshotPoint(view.TextBuffer.CurrentSnapshot,context.ReplaceStart+dialog.Insertion.Length));
                        LastStatus="Colonne inserite · Ctrl+Z per annullare";
                    }
                }
                else
                {
                    var objects=await Task.Run(()=>SchemaInspector.LoadAsync(capture.Connection,context,reference,cancel.Token,capture.CacheScope));
                    capture.Dispose();
                    if(!view.HasAggregateFocus)throw new InvalidOperationException("La scheda attiva è cambiata. Riapri il comando nella query desiderata.");
                    EnsureToolCurrent(snapshot,caret,capture);
                    if(objects.Count==0)throw new InvalidOperationException("Nessuna tabella risolvibile nel punto corrente. Posiziona il cursore su una tabella, un alias o una colonna.");
                    toolDialog=new ObjectInfoDialog(objects);UiTheme.Owner(toolDialog);toolDialog.ShowDialog();LastStatus="Informazioni aggiornate.";
                }
            }
            catch(OperationCanceledException){LastStatus="Lettura annullata o tempo di attesa scaduto.";}
            catch(Exception ex)
            {
                LastStatus=ex is InvalidOperationException?ex.Message:"Lettura dei metadati non riuscita ("+ex.GetType().Name+").";
                if(!disposed)MessageBox.Show(LastStatus,"JoinAssist",MessageBoxButton.OK,MessageBoxImage.Information);
            }
            finally{toolDialog=null;if(capture!=null)capture.Dispose();if(ReferenceEquals(toolCancel,cancel))toolCancel=null;cancel.Dispose();busy=false;if(!disposed && !view.IsClosed)view.VisualElement.Focus();}
        }
        void EnsureToolCurrent(ITextSnapshot snapshot,int caret,SsmsConnection capture)
        {
            if(disposed || view.IsClosed || !ReferenceEquals(snapshot,view.TextBuffer.CurrentSnapshot) || view.Caret.Position.BufferPosition.Position!=caret)
                throw new InvalidOperationException("La query o il cursore sono cambiati. Riapri il comando.");
            string reason=capture.InvalidationReason();if(reason!=null)throw new InvalidOperationException(reason);
        }
        internal void OpenJoin()
        {
            if (busy || disposed || view.IsClosed) return;
            busy = true;
            SsmsConnection capture = null;
            try
            {
                view.VisualElement.Focus();
                if (!view.Selection.IsEmpty) throw new InvalidOperationException("Deseleziona il testo e posiziona il cursore nel punto in cui vuoi aggiungere il JOIN.");
                var snapshot = view.TextBuffer.CurrentSnapshot;
                int caret = view.Caret.Position.BufferPosition.Position;
                var context = QueryAnalyzer.Analyze(snapshot.GetText(), caret);
                capture = SsmsConnection.Capture(snapshot.GetText());
                var dialog = new JoinDialog(capture, context);
                var handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero) new System.Windows.Interop.WindowInteropHelper(dialog).Owner = handle;
                if (dialog.ShowDialog() == true)
                {
                    if (view.IsClosed || !string.Equals(view.TextBuffer.CurrentSnapshot.GetText(), snapshot.GetText(), StringComparison.Ordinal))
                        throw new InvalidOperationException("Il testo della query è cambiato o la scheda è stata chiusa (JA-CONTEXT-01). Riapri i suggerimenti.");
                    var reason = capture.InvalidationReason();
                    if (reason != null) throw new InvalidOperationException(reason);
                    snapshot = view.TextBuffer.CurrentSnapshot;
                    view.VisualElement.Focus();
                    var insertion = JoinBuilder.InsertText(context, dialog.Selected, dialog.JoinKind, snapshot.GetText());
                    using (var edit = view.TextBuffer.CreateEdit())
                    {
                        if (!edit.Replace(context.ReplaceStart, context.ReplaceLength, insertion))
                            throw new InvalidOperationException("Il punto di inserimento non è modificabile.");
                        var applied = edit.Apply();
                        if (applied == snapshot) throw new InvalidOperationException("SSMS ha annullato la modifica. Riprova in una query modificabile.");
                    }
                    view.Caret.MoveTo(new SnapshotPoint(view.TextBuffer.CurrentSnapshot, context.ReplaceStart + insertion.Length));
                    view.VisualElement.Focus();
                }
            }
            catch (Exception ex)
            {
                while (ex is System.Reflection.TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                // No connection strings, credentials, SQL text or server error messages go to a log.
                string message = ex is InvalidOperationException || ex is ArgumentException ? ex.Message :
                    "Impossibile completare l'operazione (" + ex.GetType().Name + "). Il testo della query non è stato modificato.";
                LastStatus=message;
                MessageBox.Show(message, "JoinAssist", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally { if (capture != null) capture.Dispose(); busy = false; }
        }
        void OnClosed(object sender,EventArgs e){Dispose();}
        public void Dispose()
        {
            if(disposed)return;disposed=true;toolCancel?.Cancel();toolDialog?.Close();automatic.Dispose();view.Closed-=OnClosed;
            view.Properties.RemoveProperty(typeof(JoinEditorController));
        }
    }
}
