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
            if(disposed)return;disposed=true;automatic.Dispose();view.Closed-=OnClosed;
            view.Properties.RemoveProperty(typeof(JoinEditorController));
        }
    }
}
