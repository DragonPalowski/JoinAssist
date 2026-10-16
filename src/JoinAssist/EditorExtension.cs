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
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name("JoinAssist.Margin")]
    [Order(Before = PredefinedMarginNames.HorizontalScrollBar)]
    [MarginContainer(PredefinedMarginNames.Bottom)]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    public sealed class JoinMarginProvider : IWpfTextViewMarginProvider
    {
        [Import] public ITextDocumentFactoryService Documents = null;
        [Import(AllowDefault = true)] public IVsEditorAdaptersFactoryService Adapters = null;
        [Import(AllowDefault = true)] public ICompletionBroker CompletionBroker = null;
        public IWpfTextViewMargin CreateMargin(IWpfTextViewHost host, IWpfTextViewMargin container)
        {
            ITextDocument doc;
            bool sql = host.TextView.TextDataModel.ContentType.TypeName.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!sql && Documents.TryGetTextDocument(host.TextView.TextBuffer, out doc))
                sql = string.Equals(Path.GetExtension(doc.FilePath), ".sql", StringComparison.OrdinalIgnoreCase);
            return sql ? new JoinMargin(host.TextView, Adapters, CompletionBroker) : null;
        }
    }
    public sealed class JoinMargin : IWpfTextViewMargin
    {
        readonly IWpfTextView view;
        readonly StackPanel panel;
        readonly Button button;
        readonly Button updates;
        readonly AutomaticCompletion automatic;
        readonly CheckBox autoSwitch;
        readonly TextBlock status;
        UpdateDialog updateDialog;
        PreferencesDialog preferencesDialog;
        bool disposed, busy, syncing;
        public JoinMargin(IWpfTextView textView, IVsEditorAdaptersFactoryService adapters, ICompletionBroker broker)
        {
            view = textView;
            string settingsError=PreferenceStore.Initialize();
            panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            button = new Button { Content = "JOIN da foreign key…", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 0, 8, 0),
                ToolTip = "Apri manualmente l'elenco dei JOIN basati sulle foreign key" };
            panel.Children.Add(button);
            status = new TextBlock { Text = settingsError??"Scrivi FROM, JOIN oppure tabella.colonna", VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.Gray, MaxWidth = 650, TextTrimming = TextTrimming.CharacterEllipsis };
            automatic = new AutomaticCompletion(view, adapters, broker, text => { status.Text = text; status.ToolTip = text; });
            autoSwitch = new CheckBox { Content = "Automatico", IsChecked = PreferenceStore.Current.Automatic, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,14,0), Foreground = System.Windows.Media.Brushes.Gray };
            autoSwitch.Checked += ToggleAutomatic;
            autoSwitch.Unchecked += ToggleAutomatic;
            panel.Children.Add(autoSwitch);
            ActionButton("Impostazioni…",()=>OpenPreferences(),"Formato, tastiera, attesa e durata della cache");
            ActionButton("Aggiorna metadati",()=>automatic.RefreshMetadata(),"Rileggi lo schema della connessione di questa scheda alla prossima proposta");
            ActionButton("Copia diagnostica",()=>CopyDiagnostic(),"Copia solo versioni e codice del problema, senza query o dati della connessione");
            updates = new Button { Content = "Aggiornamenti…", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0,0,10,0),
                ToolTip = "JoinAssist " + UpdateClient.DisplayVersion(UpdateClient.InstalledVersion) + " — controlla e scarica le nuove versioni da GitHub" };
            panel.Children.Add(updates); panel.Children.Add(status);
            button.Click += OnClick;
            updates.Click += OnUpdates;
            view.Closed += OnClosed;
            PreferenceStore.Changed+=OnPreferencesChanged;
        }
        void ActionButton(string label,Action action,string tip)
        {
            var item=new Button{Content=label,Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,8,0),ToolTip=tip};item.Click+=(s,e)=>action();panel.Children.Add(item);
        }
        void ToggleAutomatic(object sender,RoutedEventArgs e)
        {
            if(syncing)return;
            try{var value=PreferenceStore.Current;value.Automatic=autoSwitch.IsChecked==true;PreferenceStore.Save(value);}
            catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) {OnPreferencesChanged(null,null);status.Text="[JA-SETTINGS-02] Impossibile salvare le preferenze.";}
        }
        void OnPreferencesChanged(object sender,EventArgs e) {if(disposed)return;syncing=true;try{autoSwitch.IsChecked=PreferenceStore.Current.Automatic;}finally{syncing=false;}}
        void CopyDiagnostic()
        {
            try{Clipboard.SetText(Diagnostics.Report(status.Text));status.Text="Diagnostica copiata: puoi incollarla in una segnalazione su GitHub.";}
            catch(System.Runtime.InteropServices.ExternalException){status.Text="[JA-CLIPBOARD-01] Appunti occupati: riprova.";}
        }
        void OpenPreferences()
        {
            if(disposed || view.IsClosed)return;
            if(preferencesDialog!=null){preferencesDialog.Activate();return;}
            var dialog=new PreferencesDialog();var handle=System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if(handle!=IntPtr.Zero)new System.Windows.Interop.WindowInteropHelper(dialog).Owner=handle;
            preferencesDialog=dialog;dialog.Closed+=(s,e)=>preferencesDialog=null;dialog.Show();
        }
        void OnClick(object sender, RoutedEventArgs e) { Open(); }
        void OnUpdates(object sender, RoutedEventArgs e)
        {
            if (disposed || view.IsClosed) return;
            if (updateDialog != null) { updateDialog.Activate(); return; }
            try
            {
                var dialog = new UpdateDialog();
                var handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero) new System.Windows.Interop.WindowInteropHelper(dialog).Owner = handle;
                updateDialog = dialog;
                dialog.Closed += (s,args) => updateDialog = null;
                dialog.Show();
            }
            catch (Exception ex)
            {
                updateDialog = null;
                MessageBox.Show("Impossibile aprire gli aggiornamenti (" + ex.GetType().Name + ").", "JoinAssist", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        void Open()
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
                MessageBox.Show(message, "JoinAssist", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally { if (capture != null) capture.Dispose(); busy = false; }
        }
        void OnClosed(object sender, EventArgs e) { Dispose(); }
        public FrameworkElement VisualElement { get { return panel; } }
        public double MarginSize { get { return panel.ActualHeight; } }
        public bool Enabled { get { return !disposed; } }
        public ITextViewMargin GetTextViewMargin(string name) { return string.Equals(name, "JoinAssist.Margin", StringComparison.OrdinalIgnoreCase) ? this : null; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            button.Click -= OnClick;
            updates.Click -= OnUpdates;
            if (updateDialog != null) updateDialog.Close();
            if(preferencesDialog!=null)preferencesDialog.Close();
            PreferenceStore.Changed-=OnPreferencesChanged;
            autoSwitch.Checked-=ToggleAutomatic;autoSwitch.Unchecked-=ToggleAutomatic;
            automatic.Dispose();
            view.Closed -= OnClosed;
        }
    }
}
