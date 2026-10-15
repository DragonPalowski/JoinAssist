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
        readonly AutomaticCompletion automatic;
        bool disposed, busy;
        public JoinMargin(IWpfTextView textView, IVsEditorAdaptersFactoryService adapters, ICompletionBroker broker)
        {
            view = textView;
            panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            button = new Button { Content = "JOIN da foreign key…", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 0, 8, 0),
                ToolTip = "Apri manualmente l'elenco dei JOIN basati sulle foreign key" };
            panel.Children.Add(button);
            var status = new TextBlock { Text = "Scrivi FROM, JOIN oppure tabella.colonna", VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.Gray, MaxWidth = 650, TextTrimming = TextTrimming.CharacterEllipsis };
            automatic = new AutomaticCompletion(view, adapters, broker, text => { status.Text = text; status.ToolTip = text; });
            var autoSwitch = new CheckBox { Content = "Completamento automatico", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,14,0), Foreground = System.Windows.Media.Brushes.Gray };
            autoSwitch.Checked += (s,e) => automatic.Enabled = true;
            autoSwitch.Unchecked += (s,e) => automatic.Enabled = false;
            panel.Children.Add(autoSwitch); panel.Children.Add(status);
            button.Click += OnClick;
            view.Closed += OnClosed;
        }
        void OnClick(object sender, RoutedEventArgs e) { Open(); }
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
            automatic.Dispose();
            view.Closed -= OnClosed;
        }
    }
}
