using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace JoinAssist
{
    internal sealed class AutomaticCompletion : IDisposable
    {
        readonly IWpfTextView view;
        readonly IVsEditorAdaptersFactoryService adapters;
        readonly ICompletionBroker completionBroker;
        readonly Action<string> report;
        readonly DispatcherTimer delay;
        readonly Popup popup;
        readonly ListBox list;
        readonly TextBlock title, preview, hint;
        Session current;
        EditorCommandFilter filter;
        string suppressedKey;
        bool disposed, committing, enabled = true, manualRequested, analysisRunning, analysisAgain;
        int analysisGeneration;
        internal bool Enabled { get { return enabled; } set { enabled = value; if (!value) CloseSession(); } }
        internal bool IsOpen { get { return popup.IsOpen; } }
        internal bool HasItems { get { return IsOpen && list.Items.Count > 0; } }
        internal AutomaticCompletion(IWpfTextView textView, IVsEditorAdaptersFactoryService factory, ICompletionBroker broker, Action<string> status)
        {
            view = textView; adapters = factory; completionBroker = broker; report = status;
            enabled=PreferenceStore.Current.Automatic;
            delay = new DispatcherTimer(DispatcherPriority.Background, view.VisualElement.Dispatcher) { Interval = TimeSpan.FromMilliseconds(PreferenceStore.Current.DelayMilliseconds) };
            delay.Tick += OnTick;
            var body = new StackPanel();
            title = new TextBlock { FontWeight = FontWeights.Medium,MaxWidth=610, Margin = new Thickness(10,8,10,8), TextWrapping = TextWrapping.Wrap };
            body.Children.Add(title);
            list = new ListBox { MinWidth = 340, MaxWidth = 650, MaxHeight = 230, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch,Focusable = false };
            UiTheme.Surface(list);
            ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
            list.ItemTemplate = ProposalPresentation.CompactTemplate();
            var style = new Style(typeof(ListBoxItem),list.ItemContainerStyle??list.TryFindResource(typeof(ListBoxItem)) as Style); style.Setters.Add(new Setter(UIElement.FocusableProperty, false)); list.ItemContainerStyle = style;
            body.Children.Add(list);
            preview = new TextBlock { FontFamily = new FontFamily("Consolas"),FontSize = 12, MaxWidth = 610, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10,8,10,8) };
            body.Children.Add(preview);
            hint=new TextBlock { FontSize=11,Margin = new Thickness(10,0,10,8) };body.Children.Add(hint);
            var popupBody=new Border{BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(3),Child=body};UiTheme.Surface(popupBody);
            popupBody.SetResourceReference(TextBlock.ForegroundProperty,Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowTextBrushKey);
            popup = new Popup { PlacementTarget = view.VisualElement, Placement = PlacementMode.Relative, AllowsTransparency = true,
                StaysOpen = true, Focusable = false, Child = popupBody };
            list.SelectionChanged += OnSelection;
            list.MouseDoubleClick += OnDoubleClick;
            view.TextBuffer.Changed += OnBufferChanged;
            view.Caret.PositionChanged += OnCaretChanged;
            view.LostAggregateFocus += OnLostFocus;
            view.LayoutChanged += OnLayoutChanged;
            view.VisualElement.Loaded += OnLoaded;
            view.VisualElement.PreviewKeyDown += OnPreviewKey;
            PreferenceStore.Changed+=OnPreferences;
            OnPreferences(null,null);
            AttachFilter();
        }
        void OnPreferences(object sender,EventArgs e)
        {
            if(disposed)return;
            analysisGeneration++;
            var value=PreferenceStore.Current;enabled=value.Automatic;manualRequested=false;
            delay.Interval=TimeSpan.FromMilliseconds(value.DelayMilliseconds);
            hint.Text="↑ ↓ scegli   ·   "+(value.AcceptEnter?"Tab / Invio":"Tab")+" inserisci   ·   Esc chiudi";
            CloseSession();
        }
        internal void RequestCompletion() { if(disposed)return;manualRequested=true;suppressedKey=null;view.VisualElement.Focus();Evaluate(); }
        internal void RefreshMetadata()
        {
            try
            {
                CloseSession();
                analysisGeneration++;
                using(var connection=SsmsConnection.Capture(view.TextBuffer.CurrentSnapshot.GetText()))MetadataCache.Invalidate(connection.CacheScope);
                SqlAnalysis.ClearCache();report("Metadati invalidati: la prossima proposta leggerà lo schema aggiornato.");
            }
            catch(Exception ex){report(Friendly(ex));}
        }
        void AttachFilter()
        {
            if (filter != null || disposed || adapters == null) return;
            try
            {
                var adapter = adapters.GetViewAdapter(view);
                if (adapter != null) filter = new EditorCommandFilter(adapter, this);
            }
            catch { report("Usa JoinAssist → JOIN da foreign key se la tastiera dei suggerimenti non risponde."); }
        }
        void OnLoaded(object sender, RoutedEventArgs e) { AttachFilter(); }
        void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            if (committing || disposed || !(enabled||manualRequested)) return;
            // Buffer notifications occur before SSMS has moved the caret; examine it on the next UI turn.
            view.VisualElement.Dispatcher.BeginInvoke(new Action(() => {
                if (disposed || committing || !(enabled||manualRequested)) return;
                delay.Stop();
                if (current != null) Evaluate(); else delay.Start();
            }), DispatcherPriority.Background);
        }
        void OnCaretChanged(object sender, CaretPositionChangedEventArgs e)
        {
            if (committing || disposed) return;
            // Delay until caret and buffer refer to the same post-edit snapshot.
            if (current != null) view.VisualElement.Dispatcher.BeginInvoke(new Action(() => { if (!disposed && current != null) Evaluate(); }), DispatcherPriority.Background);
        }
        void OnTick(object sender, EventArgs e) { delay.Stop(); Evaluate(); }
        async void Evaluate()
        {
            if(disposed || committing || !(enabled||manualRequested) || view.IsClosed || !view.HasAggregateFocus || !view.Selection.IsEmpty) { CloseSession();return; }
            analysisAgain=true;
            if(analysisRunning)return;
            analysisRunning=true;
            try
            {
                // One parser at a time. Changes during analysis coalesce into the latest editor snapshot.
                while(analysisAgain && !disposed && !view.IsClosed && (enabled||manualRequested))
                {
                    analysisAgain=false;
                    var snapshot=view.TextBuffer.CurrentSnapshot;int caret=view.Caret.Position.BufferPosition.Position,generation=analysisGeneration;
                    string text=snapshot.GetText();
                    string diagnostic=null;
                    var trigger=await Task.Run(()=>AutomaticTrigger.TryCreate(text,caret,out diagnostic));
                    if(generation!=analysisGeneration)continue;
                    if(disposed || view.IsClosed || !view.HasAggregateFocus || !view.Selection.IsEmpty) {CloseSession();break;}
                    if(!ReferenceEquals(snapshot,view.TextBuffer.CurrentSnapshot) || caret!=view.Caret.Position.BufferPosition.Position) {analysisAgain=true;continue;}
                    if(!(enabled||manualRequested))break;
                    ApplyTrigger(trigger,snapshot,diagnostic);
                }
            }
            catch (Exception ex) { CloseSession(); report(Friendly(ex)); }
            finally {analysisRunning=false;}
        }
        void ApplyTrigger(AutomaticTrigger trigger,ITextSnapshot snapshot,string diagnostic)
        {
            if (trigger == null) { suppressedKey = null; CloseSession(); report(diagnostic??(manualRequested?"Punto non riconosciuto: usa FROM, JOIN, tabella.colonna oppure una posizione di colonna SQL.":"JoinAssist pronto."));return; }
            if (trigger.BaseKey == suppressedKey) return;
            if (current != null && current.Trigger.BaseKey == trigger.BaseKey)
            {
                if (!current.Trigger.Prefix.SequenceEqual(trigger.Prefix, StringComparer.OrdinalIgnoreCase)) current.ManualSelection = false;
                current.Trigger = trigger;
                if (current.Finished) ShowResults(current);
                return;
            }
            CloseSession();
            var session = new Session { Trigger = trigger };
            current = session;
            try
            {
                session.Connection = SsmsConnection.Capture(snapshot.GetText());
                ShowMessage(session.Trigger.Label + " · lettura dei metadati…", "");
                Load(session);
            }
            catch (Exception ex)
            {
                session.Finished = true;
                session.Error = Friendly(ex);
                ShowResults(session);
            }
        }
        async void Load(Session session)
        {
            var token = session.Cancel.Token;
            var sourceTrigger = session.Trigger;
            try
            {
                session.Proposals = await Task.Run(() => CompletionEngine.LoadAsync(session.Connection.Connection, sourceTrigger, token,session.Connection.CacheScope));
                session.Diagnostic=sourceTrigger.Context.Diagnostic;
            }
            catch (OperationCanceledException) { session.Error = "[JA-TIMEOUT-01] Tempo di attesa scaduto. Verifica la connessione e riprova."; }
            catch (Exception ex) { session.Error = Friendly(ex); }
            finally
            {
                session.Finished = true;
                // The identity check uses only the captured editor/server/database; the metadata connection can close now.
                if (session.Connection != null) { try { session.Connection.Dispose(); } catch { } }
                if (session.Closed) session.Cancel.Dispose(); else session.Cancel.CancelAfter(Timeout.Infinite);
            }
            if (!disposed && ReferenceEquals(current, session) && !session.Closed) Evaluate();
        }
        void ShowResults(Session session)
        {
            if (session.Connection != null)
            {
                var reason = session.Connection.InvalidationReason();
                if (reason != null) { CloseSession(); report(reason); return; }
            }
            if (session.Error != null) { popup.IsOpen = false; report(session.Error + " Cancella e riscrivi il prefisso per riprovare."); return; }
            var selected = list.SelectedItem as JoinProposal;
            var allMatches = session.Trigger.OrderMatches(session.Proposals);
            var matches = allMatches.Take(200).ToList();
            if (matches.Count == 0)
            {
                popup.IsOpen = false; list.ItemsSource = null;
                report(session.Proposals.Count == 0 ? session.Diagnostic??session.Trigger.Context.Diagnostic??"[JA-EMPTY-01] Nessuna proposta disponibile per questo contesto" : "[JA-MATCH-01] Nessuna proposta corrisponde al nome digitato");
                return;
            }
            list.ItemsSource = matches;
            if (session.ManualSelection && selected != null && matches.Contains(selected)) list.SelectedItem = selected;
            else if (matches.Count > 0) list.SelectedIndex = 0;
            title.Text = matches.Count == 0 ? (session.Proposals.Count == 0 ? "Nessuna foreign key visibile per le tabelle della query." : "Nessun collegamento corrisponde al nome digitato.") :
                session.Trigger.Label + " · " + allMatches.Count + " proposte" + (allMatches.Count > 200 ? " (prime 200: continua a scrivere)" : "") + " · " + session.Connection.Database;
            report(session.Diagnostic??session.Trigger.Context.Diagnostic??allMatches.Count + " proposte · " + session.Trigger.Label);
            OnSelection(null, null);
            ShowPopup();
        }
        void ShowMessage(string text, string detail)
        {
            list.ItemsSource = null; title.Text = text; preview.Text = detail; ShowPopup();
        }
        void ShowPopup()
        {
            if (!view.HasAggregateFocus || view.IsClosed) return;
            // Suppress competing standard lists only while the FK list owns this JOIN completion.
            if (completionBroker != null) completionBroker.DismissAllSessions(view);
            Position(); popup.IsOpen = true;
        }
        void Position() { popup.HorizontalOffset = Math.Max(0, view.Caret.Left - view.ViewportLeft); popup.VerticalOffset = view.Caret.Bottom - view.ViewportTop + 3; }
        void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e) { if (IsOpen) { try { Position(); } catch { CloseSession(); } } }
        void OnLostFocus(object sender, EventArgs e) { analysisGeneration++;manualRequested=false;CloseSession(); }
        void OnSelection(object sender, SelectionChangedEventArgs e)
        {
            var proposal = list.SelectedItem as JoinProposal;
            preview.Text = proposal == null || current == null ? "" : current.Trigger.Preview(proposal);
            if(proposal!=null && proposal.IsInsertTemplate)
            {
                var lines=preview.Text.Replace("\r\n","\n").Split('\n');
                if(lines.Length>12)preview.Text=string.Join(Environment.NewLine,lines.Take(12))+Environment.NewLine+"… (anteprima ridotta; vengono inserite tutte le colonne)";
            }
            preview.Visibility=proposal==null || proposal.IsName && !proposal.IsInsertTemplate?Visibility.Collapsed:Visibility.Visible;
        }
        void OnDoubleClick(object sender, MouseButtonEventArgs e) { if (HasItems) { e.Handled = true; Commit(); } }
        void OnPreviewKey(object sender, KeyEventArgs e)
        {
            Key key=e.Key==Key.System?e.SystemKey:e.Key;
            if(IsShortcut(key,Keyboard.Modifiers)) {e.Handled=true;RequestCompletion();return;}
            if (Keyboard.Modifiers == ModifierKeys.None && HandleKey(key)) e.Handled = true;
        }
        internal static bool IsShortcut(Key key,ModifierKeys modifiers)
        {
            string shortcut=PreferenceStore.Current.Shortcut;
            return shortcut=="Ctrl+Alt+Space" && key==Key.Space && modifiers==(ModifierKeys.Control|ModifierKeys.Alt) ||
                shortcut=="Ctrl+Alt+J" && key==Key.J && modifiers==(ModifierKeys.Control|ModifierKeys.Alt) ||
                shortcut=="Ctrl+Shift+J" && key==Key.J && modifiers==(ModifierKeys.Control|ModifierKeys.Shift);
        }
        internal bool HandleKey(Key key)
        {
            try { return HandleKeyCore(key); }
            catch (Exception ex) { CloseSession(); report(Friendly(ex)); return false; }
        }
        bool HandleKeyCore(Key key)
        {
            if (!IsOpen) return false;
            if (key == Key.Escape) { if (current != null) suppressedKey = current.Trigger.BaseKey; manualRequested=false;CloseSession(); return true; }
            if (!HasItems) return false;
            if (key == Key.Down || key == Key.Up)
            {
                if (current != null) current.ManualSelection = true;
                list.SelectedIndex = (list.SelectedIndex + (key == Key.Down ? 1 : list.Items.Count - 1)) % list.Items.Count;
                list.ScrollIntoView(list.SelectedItem); return true;
            }
            if (key == Key.Tab || key == Key.Return && PreferenceStore.Current.AcceptEnter) { Commit(); return true; }
            return false;
        }
        void Commit()
        {
            var session = current;
            var selected = list.SelectedItem as JoinProposal;
            if (session == null || selected == null) return;
            var snapshot = view.TextBuffer.CurrentSnapshot;
            var trigger = AutomaticTrigger.TryCreate(snapshot.GetText(), view.Caret.Position.BufferPosition.Position);
            if (trigger == null || trigger.BaseKey != session.Trigger.BaseKey || !trigger.Matches(selected) || !session.Connection.IsStillActive())
            { CloseSession(); report("La query o la connessione sono cambiate: riscrivi JOIN per aggiornare le proposte."); return; }
            committing = true;
            try
            {
                string insertion = trigger.Insertion(selected);
                using (var edit = view.TextBuffer.CreateEdit())
                {
                    if (!edit.Replace(trigger.Context.ReplaceStart, trigger.Context.ReplaceLength, insertion)) throw new InvalidOperationException("Punto di inserimento non modificabile.");
                    if (edit.Apply() == snapshot) throw new InvalidOperationException("Modifica annullata dall'editor.");
                }
                view.Caret.MoveTo(new SnapshotPoint(view.TextBuffer.CurrentSnapshot, trigger.Context.ReplaceStart + trigger.InsertionCaret(selected,insertion)));
                report("Completamento inserito · Ctrl+Z per annullare");
            }
            catch (Exception ex) { report(Friendly(ex)); }
            finally { CloseSession(); committing = false; view.VisualElement.Focus(); }
        }
        static string Friendly(Exception ex)
        {
            while (ex is System.Reflection.TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return "[JA-METADATA-01] "+(ex is InvalidOperationException ? ex.Message : "Lettura dei metadati non riuscita (" + ex.GetType().Name + "). Verifica connessione e permessi.");
        }
        void CloseSession()
        {
            delay.Stop(); popup.IsOpen = false; list.ItemsSource = null;
            var old = current; current = null;
            if (old != null && !old.Closed)
            {
                old.Closed = true; old.Cancel.Cancel();
                if (old.Finished) old.Cancel.Dispose();
            }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; CloseSession();
            delay.Tick -= OnTick; view.TextBuffer.Changed -= OnBufferChanged; view.Caret.PositionChanged -= OnCaretChanged;
            view.LostAggregateFocus -= OnLostFocus; view.LayoutChanged -= OnLayoutChanged;
            view.VisualElement.Loaded -= OnLoaded; view.VisualElement.PreviewKeyDown -= OnPreviewKey;
            PreferenceStore.Changed-=OnPreferences;
            list.SelectionChanged -= OnSelection; list.MouseDoubleClick -= OnDoubleClick;
            if (filter != null) filter.Dispose();
        }
        sealed class Session
        {
            public AutomaticTrigger Trigger;
            public SsmsConnection Connection;
            public List<JoinProposal> Proposals = new List<JoinProposal>();
            public CancellationTokenSource Cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            public bool Finished, Closed, ManualSelection;
            public string Error,Diagnostic;
        }
    }

    internal static class ProposalPresentation
    {
        internal static DataTemplate CompactTemplate()
        {
            var panel=new FrameworkElementFactory(typeof(StackPanel));panel.SetValue(FrameworkElement.MarginProperty,new Thickness(5,4,5,4));
            var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new Binding("PopupTitle"));name.SetValue(TextBlock.FontWeightProperty,FontWeights.Medium);name.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);panel.AppendChild(name);
            var detail=new FrameworkElementFactory(typeof(TextBlock));detail.SetBinding(TextBlock.TextProperty,new Binding("PopupDetail"));detail.SetValue(TextBlock.FontSizeProperty,12.0);detail.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);panel.AppendChild(detail);
            return new DataTemplate{VisualTree=panel};
        }
        internal static DataTemplate Template()
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding("Caption"));
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            text.SetValue(FrameworkElement.MarginProperty, new Thickness(4,3,4,3));
            text.SetValue(FrameworkElement.MaxWidthProperty, 800.0);
            return new DataTemplate { VisualTree = text };
        }
    }

    [ComVisible(true)]
    internal sealed class EditorCommandFilter : IOleCommandTarget, IDisposable
    {
        // VSStd2K command IDs are defined by the editor's command protocol.
        static readonly Guid EditorGroup = new Guid("1496A755-94DE-11D0-8C3F-00C04FC2AAE2");
        readonly IVsTextView adapter;
        readonly AutomaticCompletion completion;
        IOleCommandTarget next;
        bool installed;
        public EditorCommandFilter(IVsTextView view, AutomaticCompletion controller)
        {
            adapter = view; completion = controller;
            int result = adapter.AddCommandFilter(this, out next);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            installed = true;
        }
        static Key Translate(uint id)
        {
            switch (id) { case 3: return Key.Return; case 4: return Key.Tab; case 11: return Key.Up; case 13: return Key.Down; case 103: return Key.Escape; default: return Key.None; }
        }
        public int Exec(ref Guid group, uint command, uint options, IntPtr input, IntPtr output)
        {
            if (group == EditorGroup && Keyboard.Modifiers==ModifierKeys.None && completion.HandleKey(Translate(command))) return 0;
            return next == null ? unchecked((int)0x80040100) : next.Exec(ref group, command, options, input, output);
        }
        public int QueryStatus(ref Guid group, uint count, OLECMD[] commands, IntPtr text)
        {
            if (group == EditorGroup && completion.IsOpen)
            {
                bool all = true;
                for (int i = 0; i < count; i++)
                {
                    Key key = Translate(commands[i].cmdID);
                    if (key == Key.Escape || (completion.HasItems && key != Key.None)) commands[i].cmdf = 3;
                    else all = false;
                }
                if (all) return 0;
            }
            return next == null ? unchecked((int)0x80040100) : next.QueryStatus(ref group, count, commands, text);
        }
        public void Dispose() { if (installed) { installed = false; try { adapter.RemoveCommandFilter(this); } catch { } } }
    }
}
