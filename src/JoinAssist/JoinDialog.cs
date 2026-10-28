using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace JoinAssist
{
    public sealed class JoinDialog : Window
    {
        readonly SsmsConnection connection;
        readonly QueryContext context;
        readonly CancellationTokenSource cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        readonly TextBlock status;
        readonly TextBox search, preview;
        readonly ListBox list;
        readonly ComboBox kind;
        readonly Button insert;
        List<JoinProposal> proposals = new List<JoinProposal>();
        bool closed;
        public JoinProposal Selected { get { return list.SelectedItem as JoinProposal; } }
        public string JoinKind { get { return new[] { "INNER", "LEFT", "RIGHT", "FULL" }[Math.Max(0, kind.SelectedIndex)]; } }
        public JoinDialog(SsmsConnection capture, QueryContext queryContext)
        {
            connection = capture; context = queryContext;
            Title = "JoinAssist — JOIN da foreign key"; Width = 920; Height = 610; MinWidth = 650; MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            UiTheme.Dialog(this);
            var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            header.Children.Add(new TextBlock { Text = "Scegli il collegamento", FontSize = 23, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = capture.Server + "  /  " + capture.Database, Margin = new Thickness(0, 6, 0, 12), TextWrapping = TextWrapping.Wrap });
            var row = new DockPanel();
            kind = new ComboBox { Width = 130, Margin = new Thickness(12, 0, 0, 0), ItemsSource = new[] { "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL JOIN" }, SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "INNER", "LEFT", "RIGHT", "FULL" }, context.JoinKind)) };
            DockPanel.SetDock(kind, Dock.Right); row.Children.Add(kind);
            search = new TextBox { MinHeight = 29, Padding = new Thickness(6), ToolTip = "Filtra per tabella, foreign key o colonna" }; row.Children.Add(search); header.Children.Add(row);
            status = new TextBlock { Text = "Lettura delle relazioni dal database…", Margin = new Thickness(0, 10, 0, 10), TextWrapping = TextWrapping.Wrap }; header.Children.Add(status);
            var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            footer.Children.Add(new TextBlock { Text = "Anteprima SQL", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
            preview = new TextBox { IsReadOnly = true, Height = 100, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"), Padding = new Thickness(10) }; footer.Children.Add(preview);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            HelpGuide.Attach(this,buttons,()=>"joins");
            var dismiss = new Button { Content = "Annulla", IsCancel = true, Padding = new Thickness(20, 7, 20, 7), Margin = new Thickness(0, 0, 8, 0) };
            insert = new Button { Content = "Inserisci JOIN", IsDefault = true, IsEnabled = false, Padding = new Thickness(20, 7, 20, 7) };
            buttons.Children.Add(dismiss); buttons.Children.Add(insert); footer.Children.Add(buttons);
            list = new ListBox { ItemTemplate = ProposalPresentation.Template(), HorizontalContentAlignment = HorizontalAlignment.Stretch }; root.Children.Add(list);
            list.SelectionChanged += (s,e) => UpdatePreview();
            list.MouseDoubleClick += (s,e) => { if (Selected != null) DialogResult = true; };
            kind.SelectionChanged += (s,e) => UpdatePreview();
            search.TextChanged += (s,e) => Filter();
            insert.Click += (s,e) => { if (Selected != null) DialogResult = true; };
            ContentRendered += OnRendered;
            Closed += (s,e) => { closed = true; cancel.Cancel(); };
        }
        async void OnRendered(object sender, EventArgs e)
        {
            ContentRendered -= OnRendered;
            try
            {
                proposals=await Task.Run(async()=>{
                    var catalog=await CatalogReader.ReadAsync(connection.Connection,cancel.Token,connection.CacheScope).ConfigureAwait(false);
                    await new ShapeResolver(catalog,cancel.Token).ResolveAsync(context,true).ConfigureAwait(false);
                    var keys=await MetadataReader.ReadForPathsAsync(connection.Connection,context,catalog,cancel.Token,connection.CacheScope).ConfigureAwait(false);
                    return CompletionEngine.JoinItems(context,keys,catalog);
                });
                if (closed) return;
                if (!string.Equals(connection.Connection.Database, connection.Database, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Le relazioni appartengono a un database diverso. Operazione annullata.");
                Filter(); search.Focus();
            }
            catch (OperationCanceledException) { if (!closed) status.Text = "Tempo di attesa scaduto. Chiudi e riprova dopo aver verificato la connessione."; }
            catch (Exception ex)
            {
                if (closed) return;
                status.Text = ex is InvalidOperationException ? ex.Message : "Lettura dei metadati non riuscita (" + ex.GetType().Name + "). Verifica connessione e permessi sui metadati. Nessuna query utente è stata eseguita.";
            }
        }
        void Filter()
        {
            string term = search.Text.Trim();
            var filtered = proposals.Where(p => (p.Caption + " " + p.Predicate).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            list.ItemsSource = filtered; if (filtered.Count > 0) list.SelectedIndex = 0;
            status.Text = proposals.Count == 0 ? "Nessuna foreign key visibile per queste tabelle. Sono usati solo vincoli dichiarati nel database; verifica anche i permessi sui metadati." :
                filtered.Count + " collegamenti. Schema, parentesi e alias vengono aggiunti solo quando servono. Verifica la relazione nell'anteprima.";
            UpdatePreview();
        }
        void UpdatePreview() { if (preview == null || list == null) return; insert.IsEnabled = Selected != null; preview.Text = Selected == null ? "" : Selected.Sql(JoinKind); }
    }
}
