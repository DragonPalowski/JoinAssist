using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace JoinAssist
{
    internal sealed class WorkflowContext : IDisposable
    {
        internal string Sql, Selected;
        internal int Caret;
        internal SsmsConnection Connection;
        internal Action<string, bool, List<SnippetField>> Insert;
        internal void Check()
        {
            if (Connection != null)
            {
                string reason = Connection.InvalidationReason();
                if (reason != null)
                    throw new InvalidOperationException(reason + " Premi Aggiorna dalla query.");
            }
        }

        public void Dispose()
        {
            Connection?.Dispose();
        }
    }

    internal sealed class WorkflowAction
    {
        internal string Name, Help;
        internal Action Build;
        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class WorkflowDialog : Window
    {
        readonly string libraryPath;
        readonly Func<WorkflowContext> capture;
        readonly Action retry, refresh;
        readonly Action<string> openFile;
        readonly List<WorkflowAction> actions = new List<WorkflowAction>();
        readonly List<WorkflowAction> personal = new List<WorkflowAction>();
        readonly ListBox actionList;
        readonly StackPanel options;
        readonly TextBox preview, search;
        readonly TextBlock status;
        readonly ComboBox source;
        readonly TextBox sourceSearch;
        readonly CheckBox pinned;
        readonly Button insert;
        WorkflowContext context;
        CatalogSnapshot catalog;
        WorkflowGraph graph;
        CancellationTokenSource pending;
        bool busy, replaceAll;
        List<SnippetField> fields;
        List<SavedQuery> favorites;
        string libraryStamp;
        internal WorkflowDialog(Func<WorkflowContext> read, Action tryAgain, Action reload, Action<string> open, string initial = null, string favoritesPath = null)
        {
            libraryPath = favoritesPath ?? QueryLibrary.UserPath;
            capture = read;
            retry = tryAgain;
            refresh = reload;
            openFile = open;
            Title = "JoinAssist · Azioni rapide";
            Width = 1100;
            Height = 860;
            MinWidth = 820;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            UiTheme.Dialog(this);
            var dock = new DockPanel
            {
                Margin = new Thickness(16)
            };
            Content = dock;
            var top = new StackPanel();
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
            search = Input(top, "Cerca un'azione, una regola o un preferito", "");
            var toolbar = new WrapPanel
            {
                Margin = new Thickness(0, 8, 0, 8)
            };
            top.Children.Add(toolbar);
            Button(toolbar, "Aggiorna dalla query", () => Run(Reload));
            Button(toolbar, "Riprova suggerimenti", () =>
            {
                retry();
                status.Text = "Suggerimenti richiesti nella query attiva.";
            });
            Button(toolbar, "Aggiorna metadati", () =>
            {
                refresh();
                Run(Reload);
            });
            pinned = new CheckBox
            {
                Content = "Mantieni aperto (pannello affiancabile)",
                Margin = new Thickness(12, 5, 0, 0)
            };
            toolbar.Children.Add(pinned);
            top.Children.Add(new TextBlock { Text = "Sorgente per colonne, filtri e relazioni", Margin = new Thickness(0, 0, 0, 3) });
            source = new ComboBox
            {
                MinHeight = 27,
                DisplayMemberPath = "Sql",
                IsTextSearchEnabled = true
            };
            var sources = new DockPanel();
            top.Children.Add(sources);
            sourceSearch = new TextBox
            {
                Width = 200,
                MinHeight = 27,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Filtra sorgenti per nome o schema"
            };
            System.Windows.Automation.AutomationProperties.SetName(sourceSearch, "Filtra sorgenti per nome o schema");
            DockPanel.SetDock(sourceSearch, Dock.Left);
            sources.Children.Add(sourceSearch);
            sources.Children.Add(source);
            sourceSearch.TextChanged += (s, e) => FilterSources();
            var bottom = new StackPanel();
            DockPanel.SetDock(bottom, Dock.Bottom);
            dock.Children.Add(bottom);
            status = new TextBlock
            {
                Text = "Scegli un'azione. Le query generate vengono solo preparate, mai eseguite.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 6)
            };
            bottom.Children.Add(status);
            preview = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                FontFamily = new FontFamily("Consolas"),
                Height = 160,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            bottom.Children.Add(preview);
            var buttons = new WrapPanel
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            bottom.Children.Add(buttons);
            HelpGuide.Attach(this,buttons,()=>HelpContent.WorkflowTopic((actionList.SelectedItem as WorkflowAction)?.Name));
            insert = Button(buttons, "Inserisci anteprima", Apply);
            Button(buttons, "Copia SQL", () =>
            {
                if (preview.Text.Length == 0)
                    throw new ArgumentException("Prepara prima un'anteprima.");
                Clipboard.SetText(preview.Text);
            });
            Button(buttons, "Nuova scheda (salva .sql…)", () =>
            {
                if (preview.Text.Length == 0)
                    throw new ArgumentException("Prepara prima un'anteprima.");
                var file = new SaveFileDialog
                {
                    Filter = "SQL (*.sql)|*.sql",
                    FileName = "JoinAssist.sql",
                    Title = "Salva lo script e aprilo in una nuova scheda (nessuna esecuzione)"
                };
                if (file.ShowDialog(this) == true)
                {
                    File.WriteAllText(file.FileName, preview.Text);
                    openFile?.Invoke(file.FileName);
                }
            });
            Button(buttons, "Chiudi", Close);
            var grid = new Grid
            {
                Margin = new Thickness(0, 12, 0, 0)
            };
            dock.Children.Add(grid);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionList = new ListBox
            {
                Margin = new Thickness(0, 0, 14, 0)
            };
            grid.Children.Add(actionList);
            options = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = options,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetColumn(scroll, 1);
            grid.Children.Add(scroll);
            Add("Record collegati", "Prepara una SELECT sui record raggiungibili tramite FK, filtrando la sorgente.", () => Relations(true));
            Add("Elenco IN dagli appunti", "Incolla una colonna senza intestazione. NULL rimane testo; i numeri sono validati.", ClipboardList);
            Add("Scegli più colonne", "Seleziona e cerca le colonne delle sorgenti visibili nella query, oppure della tabella scelta.", Columns);
            Add("Modello con campi", "Inserisci {{nome:valore}}; Tab passa tra i campi e aggiorna le ripetizioni.", TemplateAction);
            Add("Filtro temporale", "Intervalli con limite finale escluso; scegli esplicitamente ora locale oppure UTC.", Dates);
            Add("Percorso JOIN e cardinalità", "Fino a 5 passaggi FK, 30 alternative; le FK disabilitate sono escluse. Il testo sarà una query completa.", () => Relations(false));
            Add("Dove viene usato?", "Dipendenze registrate nel catalogo: SQL dinamico, dipendenze esterne e oggetti non visibili possono mancare.", () => MetadataSearch(true));
            Add("Cerca colonne nello schema", "Cerca i nomi delle colonne nei metadati, fino a 500 risultati.", () => MetadataSearch(false));
            Add("Query di controllo", "Prepara duplicati, NULL, conteggi per valore/giorno e record senza corrispondenza.", Controls);
            Add("Preferiti per database", "Salvataggio esplicito locale di SQL, descrizione e ambito. Nessuna cronologia automatica.", Favorites);
            Add("Schema affiancabile", "Carica colonne e relazioni, poi mantieni aperta la finestra accanto all'editor.", Schema);
            Add("Rinomina alias", "Rinomina un alias di tabella/vista/CTE in una query completa, con anteprima dell'intero script.", Rename);
            Add("Prova una regola", "Verifica ambito, tabella e colonna con un esempio, senza cambiare le regole attive.", Probe);
            Add("Diagnostica e recupero", "Mostra cause e stato locale, riprova oppure aggiorna i metadati dalla barra superiore.", Diagnostic);
            actionList.SelectionChanged += (s, e) => Build();
            search.TextChanged += (s, e) => Filter();
            source.SelectionChanged += (s, e) =>
            {
                ClearPreview();
                if (!busy)
                    Build();
            };
            Filter();
            if (initial != null)
            {
                var selected = actions.FirstOrDefault(a => a.Name == initial);
                if (selected != null)
                    actionList.SelectedItem = selected;
            }

            Loaded += (s, e) => Run(Reload);
            Closed += (s, e) =>
            {
                pending?.Cancel();
                context?.Dispose();
            };
        }

        void Add(string name, string help, Action build)
        {
            actions.Add(new WorkflowAction { Name = name, Help = help, Build = build });
        }

        void Filter()
        {
            var list = actions.Concat(personal).Where(a => NameMatching.Score(a.Name, search.Text) != int.MaxValue || a.Help.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            actionList.ItemsSource = list;
            if (list.Count > 0)
                actionList.SelectedIndex = 0;
        }

        internal void SelectAction(string name)
        {
            search.Text = "";
            actionList.SelectedItem = actions.FirstOrDefault(a => a.Name == name);
        }

        void PersonalActions()
        {
            personal.Clear();
            foreach (var rule in PreferenceStore.Current.Rules.Where(r => r.Enabled && PersonalRules.IsInScope(r, context?.Connection?.Server, context?.Connection?.Database)))
            {
                var item = rule;
                personal.Add(new WorkflowAction { Name = "Regola · " + rule.Name, Help = rule.Shortcut + " " + rule.Schema + "." + rule.Table, Build = () =>
                {
                    options.Children.Add(new TextBlock { Text = "Regola personale · " + item.Name, TextWrapping = TextWrapping.Wrap });
                    if (item.Kind == PersonalRuleKind.Snippet)
                        Button(options, "Prepara scorciatoia", () =>
                        {
                            if (!PersonalRules.IsInScope(item, context?.Connection?.Server, context?.Connection?.Database))
                                throw new ArgumentException("Regola fuori dall'ambito corrente.");
                            var expansion = item.LinkedFields ? SnippetExpansion.Create(item.Sql.Replace("$cursor$", "")) : new SnippetExpansion
                            {
                                Sql = item.Sql.Replace("$cursor$", "")
                            };
                            int cursor = item.Sql.IndexOf("$cursor$", StringComparison.Ordinal);
                            if (cursor >= 0)
                                expansion.Fields.Add(new SnippetField { Name = "cursor", Start = cursor, Length = 0 });
                            SetPreview(expansion.Sql, false, expansion.Fields);
                        });
                    else
                        Button(options, "Prova regola", () => new RuleProbeDialog(item, () => capture().Connection) { Owner = this }.ShowDialog());
                } });
            }

            try
            {
                foreach (var query in QueryLibrary.Load(libraryPath).Where(q => QueryLibrary.InScope(q, context?.Connection?.Server, context?.Connection?.Database)))
                {
                    var item = query;
                    personal.Add(new WorkflowAction { Name = "Preferito · " + item.Name + " · " + (string.IsNullOrEmpty(item.Database) ? "globale" : item.Database), Help = item.Description + " " + item.Category, Build = () =>
                    {
                        options.Children.Add(new TextBlock { Text = item.Description, TextWrapping = TextWrapping.Wrap });
                        Button(options, "Prepara preferito", () =>
                        {
                            var expansion = SnippetExpansion.Create(item.Sql);
                            SetPreview(expansion.Sql, false, expansion.Fields);
                        });
                    } });
                }
            }
            catch (Exception ex)
            {
                status.Text = Safe(ex);
            }

            Filter();
        }

        void ClearPreview()
        {
            preview.Text = "";
            fields = null;
            replaceAll = false;
            insert.IsEnabled = false;
        }

        void SetPreview(string sql, bool replace = false, List<SnippetField> placeholders = null)
        {
            if (sql.Length > 262144)
                throw new ArgumentException("Anteprima oltre 256 KiB: riduci lo script o la profondità del percorso.");
            preview.Text = sql;
            replaceAll = replace;
            fields = placeholders;
            insert.IsEnabled = sql.Length > 0;
        }

        void Draft(string sql)
        {
            SetPreview(WorkflowSql.Generated(sql));
        }

        void Build()
        {
            ClearPreview();
            options.Children.Clear();
            var action = actionList.SelectedItem as WorkflowAction;
            if (action == null)
                return;
            options.Children.Add(new TextBlock { Text = action.Help, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            try
            {
                action.Build();
            }
            catch (Exception ex)
            {
                status.Text = Safe(ex);
            }
        }

        static string Safe(Exception ex)
        {
            return ex is ArgumentException || ex is InvalidOperationException ? ex.Message : "Operazione non disponibile (" + ex.GetType().Name + "). Controlla connessione, permessi o accesso al file.";
        }

        void Run(Func<Task> action)
        {
            if (busy)
            {
                status.Text = "Attendi l'operazione in corso.";
                return;
            }

            busy = true;
            pending = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            source.IsEnabled = false;
            sourceSearch.IsEnabled = false;
            actionList.IsEnabled = false;
            search.IsEnabled = false;
            insert.IsEnabled = false;
            Execute(action);
        }

        async void Execute(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (OperationCanceledException)
            {
                status.Text = "Operazione annullata o tempo scaduto. Puoi riprovare.";
            }
            catch (Exception ex)
            {
                status.Text = Safe(ex);
            }
            finally
            {
                busy = false;
                pending.Dispose();
                pending = null;
                source.IsEnabled = true;
                sourceSearch.IsEnabled = true;
                actionList.IsEnabled = true;
                search.IsEnabled = true;
                insert.IsEnabled = preview.Text.Length > 0;
            }
        }

        async Task Reload()
        {
            var previous = context;
            context = null;
            catalog = null;
            graph = null;
            ClearPreview();
            previous?.Dispose();
            context = capture();
            if (context.Connection == null)
            {
                source.ItemsSource = null;
                status.Text = "Nessuna connessione disponibile: appunti, modelli e preferiti restano utilizzabili.";
                PersonalActions();
                Build();
                return;
            }

            catalog = await CatalogReader.ReadAsync(context.Connection.Connection, pending.Token, context.Connection.CacheScope);
            context.Check();
            source.ItemsSource = catalog.Objects.Where(o => o.Kind == "U" || o.Kind == "V").ToList();
            CatalogObject selected = null;
            try
            {
                var use = SqlAnalysis.ReadScope(context.Sql, context.Caret).Tables.FirstOrDefault();
                if (use != null)
                    selected = catalog.Find(use.Schema, use.Name);
            }
            catch (InvalidOperationException)
            {
            }

            source.SelectedItem = selected;
            FilterSources();
            status.Text = "Catalogo caricato · " + context.Connection.Database + " · " + DateTime.Now.ToString("HH:mm:ss") + " · solo metadati";
            var active = actionList.SelectedItem as WorkflowAction;
            PersonalActions();
            if (active != null && actions.Contains(active))
                actionList.SelectedItem = active;
            Build();
        }

        CatalogObject Table()
        {
            context?.Check();
            var obj = source.SelectedItem as CatalogObject;
            if (catalog == null || obj == null)
                throw new InvalidOperationException("Collega una query e premi Aggiorna dalla query, poi scegli la sorgente.");
            return obj;
        }

        void FilterSources()
        {
            if (catalog == null)
                return;
            var selected = source.SelectedItem as CatalogObject;
            string text = sourceSearch.Text;
            var matches = catalog.Objects.Where(o => (o.Kind == "U" || o.Kind == "V") && (NameMatching.Score(o.Name, text) != int.MaxValue || NameMatching.Score(o.Schema + "." + o.Name, text) != int.MaxValue)).OrderBy(o => NameMatching.Score(o.Name, text)).ThenBy(o => o.Schema).ToList();
            source.ItemsSource = matches;
            source.SelectedItem = selected != null && matches.Contains(selected) ? selected : matches.Count == 1 ? matches[0] : null;
        }

        async Task LoadColumns()
        {
            var obj = Table();
            await catalog.EnsureAsync(obj, pending.Token);
            context.Check();
        }

        async Task LoadGraph()
        {
            Table();
            if (graph == null)
                graph = await WorkflowGraph.ReadAsync(context.Connection.Connection, catalog, pending.Token);
            context.Check();
        }

        void Apply()
        {
            if (context == null)
                throw new InvalidOperationException("Premi Aggiorna dalla query.");
            if (preview.Text.Length == 0)
                throw new InvalidOperationException("Prepara prima l'anteprima.");
            context.Check();
            context.Insert(preview.Text, replaceAll, fields);
            status.Text = "Inserito, non eseguito · Ctrl+Z per annullare";
            if (pinned.IsChecked != true)
                Close();
            else
            {
                ClearPreview();
            }
        }

        internal static TextBox Input(Panel panel, string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 3) });
            var box = new TextBox
            {
                Text = value ?? "",
                MinHeight = 26
            };
            panel.Children.Add(box);
            return box;
        }

        internal static Button Button(Panel panel, string text, Action action)
        {
            var b = new Button
            {
                Content = text,
                Padding = new Thickness(9, 5, 9, 5),
                Margin = new Thickness(0, 5, 7, 5)
            };
            panel.Children.Add(b);
            b.Click += (s, e) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    var window = Window.GetWindow(b) as WorkflowDialog;
                    if (window != null)
                        window.status.Text = Safe(ex);
                    else
                        MessageBox.Show(Safe(ex), "JoinAssist");
                }
            };
            return b;
        }

        ComboBox Choice(string label, IEnumerable<string> values)
        {
            options.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 3) });
            var c = new ComboBox
            {
                ItemsSource = values.ToList(),
                MinHeight = 27
            };
            options.Children.Add(c);
            if (c.Items.Count > 0)
                c.SelectedIndex = 0;
            return c;
        }

        void ClipboardList()
        {
            var data = Input(options, "Una riga per valore (incolla qui)", "");
            data.AcceptsReturn = true;
            data.Height = 80;
            data.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Button(options, "Leggi appunti", () => data.Text = Clipboard.GetText());
            var type = Choice("Tipo", new[] { "nvarchar", "varchar", "int", "bigint", "decimal", "uniqueidentifier", "date" });
            var distinct = new CheckBox
            {
                Content = "Rimuovi duplicati",
                IsChecked = true,
                Margin = new Thickness(0, 7, 0, 5)
            };
            options.Children.Add(distinct);
            var op = Choice("Operatore", new[] { "IN", "NOT IN" });
            Button(options, "Prepara elenco", () => SetPreview(WorkflowSql.InList(data.Text, (string)type.SelectedItem, distinct.IsChecked == true, (string)op.SelectedItem == "NOT IN")));
        }

        void TemplateAction()
        {
            var text = Input(options, "SQL del modello · {{id:0}} e {{id}} condividono il valore", context?.Selected ?? "");
            text.AcceptsReturn = true;
            text.Height = 140;
            text.FontFamily = new FontFamily("Consolas");
            Button(options, "Prepara modello", () =>
            {
                var result = SnippetExpansion.Create(text.Text);
                SetPreview(result.Sql, false, result.Fields);
            });
        }

        string Reference(CatalogObject obj, ColumnShape col)
        {
            try
            {
                var tables = SqlAnalysis.ReadScope(context.Sql, context.Caret).Tables.Where(t => t.Name == obj.Name && (t.Schema == null || t.Schema == obj.Schema)).ToList();
                if (tables.Count == 1)
                    return tables[0].Reference + "." + SqlName.Format(col.Name);
            }
            catch (InvalidOperationException)
            {
            }

            return SqlName.Format(obj.Name) + "." + SqlName.Format(col.Name);
        }

        void Dates()
        {
            var period = Choice("Periodo", new[] { "Oggi", "Ieri", "Ultimi 7 giorni", "Mese corrente", "Intervallo" });
            var zone = Choice("Orologio usato dalla colonna", new[] { "Locale del server", "UTC" });
            Button(options, "Scegli colonna data", () => Run(async () =>
            {
                await LoadColumns();
                var obj = Table();
                var col = PickColumn(obj.Columns.Where(WorkflowSql.DateType));
                if (col != null)
                    Draft(WorkflowSql.DateFilter(Reference(obj, col), (string)period.SelectedItem, zone.SelectedIndex == 1));
            }));
        }

        ColumnShape PickColumn(IEnumerable<ColumnShape> columns)
        {
            var values = columns.ToList();
            if (values.Count == 0)
                throw new InvalidOperationException("Nessuna colonna compatibile.");
            var dialog = new ChoiceDialog<ColumnShape>("Scegli colonna", values, c => c.Name + " · " + c.Description);
            dialog.Owner = this;
            return dialog.ShowDialog() == true ? dialog.Selected : null;
        }

        void Columns()
        {
            Button(options, "Carica e scegli colonne", () => Run(async () =>
            {
                if (context == null || context.Connection == null)
                    throw new InvalidOperationException("Collega una query.");
                var choices = new List<ColumnChoice>();
                var scope = SqlAnalysis.ReadScope(context.Sql, context.Caret);
                await new ShapeResolver(catalog, pending.Token).ResolveAsync(scope, false);
                foreach (var use in scope.Tables)
                    foreach (var c in use.Columns ?? new List<ColumnShape>())
                        choices.Add(new ColumnChoice { Sql = use.Reference + "." + SqlName.Format(c.Name), Description = c.Description });
                if (choices.Count == 0)
                {
                    await LoadColumns();
                    choices.AddRange(Table().Columns.Select(c => new ColumnChoice { Sql = SqlName.Format(c.Name), Description = c.Description }));
                }

                var dialog = new ColumnsDialog(choices)
                {
                    Owner = this
                };
                if (dialog.ShowDialog() == true)
                    SetPreview(dialog.Sql);
            }));
        }

        void Relations(bool related)
        {
            var target = Input(options, "Tabella destinazione (nome completo o frammento)", "");
            var depth = Choice("Profondità massima", new[] { "1", "2", "3", "4", "5" });
            depth.SelectedIndex = 1;
            TextBox value = null;
            if (related)
                value = Input(options, "Valore sorgente senza apici SQL (vuoto = parametro da compilare)", context?.Selected ?? "");
            Button(options, "Trova percorsi", () => Run(async () =>
            {
                await LoadColumns();
                await LoadGraph();
                var root = Table();
                var targets = catalog.Objects.Where(o => o.Kind == "U" && o.Id != root.Id && NameMatching.Score(o.Schema + "." + o.Name, target.Text) != int.MaxValue).OrderBy(o => NameMatching.Score(o.Schema + "." + o.Name, target.Text)).Take(100).ToList();
                var selected = new ChoiceDialog<CatalogObject>("Scegli destinazione", targets, o => o.Schema + "." + o.Name)
                {
                    Owner = this
                };
                if (selected.ShowDialog() != true)
                    return;
                var paths = graph.Paths(root.Id, selected.Selected.Id, depth.SelectedIndex + 1, pending.Token);
                if (paths.Count == 0)
                    throw new InvalidOperationException("Nessun percorso FK visibile entro la profondità scelta.");
                var picker = new ChoiceDialog<RelationPath>("Scegli percorso (massimo 30 alternative)", paths, p => p.ToString() + Environment.NewLine + string.Join("; ", p.Steps.Select(graph.Cardinality)))
                {
                    Owner = this
                };
                if (picker.ShowDialog() != true)
                    return;
                if (related)
                {
                    var filter = PickColumn(root.Columns.OrderByDescending(c => c.PrimaryKey == true));
                    if (filter != null)
                        Draft(graph.RelatedSql(root.Id, picker.Selected, filter, value.Text));
                }
                else
                    Draft(graph.JoinSql(root.Id, picker.Selected));
            }));
            if (related)
                Button(options, "Prepara tutte le relazioni (massimo 30 percorsi)", () => Run(async () =>
                {
                    await LoadColumns();
                    await LoadGraph();
                    var root = Table();
                    var filter = PickColumn(root.Columns.OrderByDescending(c => c.PrimaryKey == true));
                    if (filter == null)
                        return;
                    var paths = graph.Paths(root.Id, 0, depth.SelectedIndex + 1, pending.Token);
                    if (paths.Count == 0)
                        throw new InvalidOperationException("Nessuna relazione FK visibile.");
                    Draft(string.Join(Environment.NewLine + Environment.NewLine, paths.Select(p => "-- " + p.ToString().Replace("\r", " ").Replace("\n", " ") + Environment.NewLine + graph.RelatedSql(root.Id, p, filter, value.Text))));
                    status.Text = "Preparate " + paths.Count + " query, massimo 30 percorsi; non eseguite. Percorsi diversi possono raggiungere la stessa tabella.";
                }));
        }

        void MetadataSearch(bool dependencies)
        {
            TextBox term = null;
            if (!dependencies)
                term = Input(options, "Nome o frammento di colonna", "");
            var list = new ListBox
            {
                MaxHeight = 240,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Button(options, "Cerca nei metadati", () => Run(async () =>
            {
                var obj = Table();
                var hits = await WorkflowSearch.Find(context.Connection.Connection, term == null ? "" : term.Text, dependencies ? (int? )obj.Id : null, pending.Token);
                context.Check();
                list.ItemsSource = hits;
                status.Text = hits.Count + " risultati visibili (massimo 500); SQL dinamico e permessi possono limitare l'elenco.";
            }));
            options.Children.Add(list);
        }

        void Controls()
        {
            var operation = Choice("Controllo", new[] { "Duplicati", "Valori NULL", "Conta per valore", "Per giorno", "Senza corrispondenza FK" });
            Button(options, "Scegli colonne / relazione", () => Run(async () =>
            {
                await LoadColumns();
                var obj = Table();
                if (operation.SelectedIndex == 4)
                {
                    await LoadGraph();
                    var keys = graph.Keys.Where(k => k.ChildId == obj.Id && !k.Disabled).ToList();
                    var picker = new ChoiceDialog<ForeignKey>("Scegli FK", keys, k => k.Name)
                    {
                        Owner = this
                    };
                    if (picker.ShowDialog() == true)
                        Draft(graph.Orphans(obj.Id, picker.Selected.Id));
                    return;
                }

                var choices = obj.Columns.Select(c => new ColumnChoice { Sql = SqlName.Format(c.Name), Description = c.Description }).ToList();
                var dialog = new ColumnsDialog(choices)
                {
                    Owner = this
                };
                if (dialog.ShowDialog() == true)
                    Draft(WorkflowSql.ControlQuery(obj, obj.Columns.Where((c, i) => choices[i].Selected), (string)operation.SelectedItem));
            }));
        }

        void Schema()
        {
            Button(options, "Carica scheda schema", () => Run(async () =>
            {
                await LoadColumns();
                await LoadGraph();
                var obj = Table();
                var text = new TextBox
                {
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Height = 260,
                    Text = string.Join(Environment.NewLine, obj.Columns.Select(c => c.Name + " · " + c.Description)) + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, graph.Keys.Where(k => k.ChildId == obj.Id || k.ParentId == obj.Id).Select(k => k.Name + " · " + graph.Cardinality(new RelationStep { Key = k, ToParent = k.ChildId == obj.Id })))
                };
                options.Children.Add(text);
                pinned.IsChecked = true;
            }));
        }

        void Rename()
        {
            var old = Input(options, "Alias attuale", "");
            var name = Input(options, "Nuovo alias", "");
            Button(options, "Mostra rinomina", () =>
            {
                if (context == null)
                    throw new InvalidOperationException("Aggiorna dalla query.");
                SetPreview(WorkflowSql.RenameAlias(context.Sql, context.Caret, old.Text, name.Text), true);
            });
        }

        void Favorites()
        {
            libraryStamp = QueryLibrary.Stamp(libraryPath);
            favorites = QueryLibrary.Load(libraryPath);
            var term = Input(options, "Cerca per nome, descrizione o categoria", "");
            var list = new ListBox
            {
                MaxHeight = 130
            };
            options.Children.Add(list);
            Action filter = () =>
            {
                list.ItemsSource = favorites.Where(q => QueryLibrary.InScope(q, context?.Connection?.Server, context?.Connection?.Database) && (q.Name + " " + q.Description + " " + q.Category).IndexOf(term.Text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            };
            term.TextChanged += (s, e) => filter();
            filter();
            var name = Input(options, "Nome", "");
            var category = Input(options, "Categoria", "");
            var description = Input(options, "Descrizione", "");
            var sql = Input(options, "SQL da salvare (testo selezionato oppure query corrente)", context == null ? "" : string.IsNullOrWhiteSpace(context.Selected) ? context.Sql : context.Selected);
            sql.AcceptsReturn = true;
            sql.Height = 75;
            sql.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var global = new CheckBox
            {
                Content = "Disponibile in tutti i database",
                IsChecked = context?.Connection == null,
                Margin = new Thickness(0, 6, 0, 0)
            };
            options.Children.Add(global);
            SavedQuery deleted = null;
            list.SelectionChanged += (s, e) =>
            {
                var q = list.SelectedItem as SavedQuery;
                if (q != null)
                {
                    name.Text = q.Name;
                    category.Text = q.Category;
                    description.Text = q.Description;
                    sql.Text = q.Sql;
                    global.IsChecked = string.IsNullOrEmpty(q.Server) && string.IsNullOrEmpty(q.Database);
                }
            };
            Button(options, "Salva nuovo preferito", () =>
            {
                var q = new SavedQuery
                {
                    Name = name.Text,
                    Category = category.Text,
                    Description = description.Text,
                    Sql = sql.Text,
                    Server = global.IsChecked == true ? "" : context?.Connection?.Server ?? "",
                    Database = global.IsChecked == true ? "" : context?.Connection?.Database ?? ""
                };
                var updated = favorites.Concat(new[] { q }).ToList();
                QueryLibrary.Save(libraryPath, updated, libraryStamp);
                libraryStamp = QueryLibrary.Stamp(libraryPath);
                favorites = updated;
                filter();
                status.Text = "Preferito salvato localmente, con SQL e ambito in chiaro.";
            });
            Button(options, "Aggiorna selezionato", () =>
            {
                var q = list.SelectedItem as SavedQuery;
                if (q == null)
                    throw new ArgumentException("Scegli un preferito.");
                var updated = favorites.Select(x => x.Id == q.Id ? new SavedQuery { Id = q.Id, Name = name.Text, Category = category.Text, Description = description.Text, Sql = sql.Text, Server = global.IsChecked == true ? "" : context?.Connection?.Server ?? q.Server, Database = global.IsChecked == true ? "" : context?.Connection?.Database ?? q.Database } : x).ToList();
                QueryLibrary.Save(libraryPath, updated, libraryStamp);
                libraryStamp = QueryLibrary.Stamp(libraryPath);
                favorites = updated;
                filter();
            });
            Button(options, "Prepara preferito", () =>
            {
                var result = SnippetExpansion.Create(sql.Text);
                SetPreview(result.Sql, false, result.Fields);
            });
            Button(options, "Elimina selezionato", () =>
            {
                var q = list.SelectedItem as SavedQuery;
                if (q == null)
                    return;
                deleted = q;
                var updated = favorites.Where(x => x.Id != q.Id).ToList();
                QueryLibrary.Save(libraryPath, updated, libraryStamp);
                libraryStamp = QueryLibrary.Stamp(libraryPath);
                favorites = updated;
                filter();
            });
            Button(options, "Ripristina ultima eliminazione", () =>
            {
                if (deleted == null)
                    return;
                var updated = favorites.Concat(new[] { deleted }).ToList();
                QueryLibrary.Save(libraryPath, updated, libraryStamp);
                libraryStamp = QueryLibrary.Stamp(libraryPath);
                favorites = updated;
                deleted = null;
                filter();
            });
        }

        void Probe()
        {
            var rules = PreferenceStore.Current.Rules;
            var list = new ListBox
            {
                ItemsSource = rules,
                MaxHeight = 130
            };
            options.Children.Add(list);
            if (rules.Count > 0)
                list.SelectedIndex = 0;
            var sample = Input(options, "Query di esempio completa", context?.Sql ?? "");
            sample.AcceptsReturn = true;
            sample.Height = 90;
            Button(options, "Prova senza modificare", () => Run(async () =>
            {
                var rule = list.SelectedItem as PersonalRule;
                if (rule == null)
                    throw new ArgumentException("Crea prima una regola nelle impostazioni.");
                if (catalog != null)
                    await LoadColumns();
                status.Text = await PersonalRules.Probe(rule, sample.Text, catalog, context?.Connection?.Server, context?.Connection?.Database, pending.Token);
            }));
        }

        void Diagnostic()
        {
            var text = new TextBlock
            {
                Text = "Se mancano proposte: verifica sorgente/alias, database, permessi e regole personali. Riprova suggerimenti ricrea la sessione; Aggiorna metadati invalida la cache. Le nuove azioni non eseguono le query generate.",
                TextWrapping = TextWrapping.Wrap
            };
            options.Children.Add(text);
            if (context?.Connection != null)
                options.Children.Add(new TextBlock { Text = "Connessione: " + context.Connection.Database + " · oggetti caricati: " + (catalog == null ? 0 : catalog.Objects.Count) + " · cache configurata: " + PreferenceStore.Read.CacheMinutes + " min", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        }
    }

    internal sealed class ChoiceDialog<T> : Window where T : class
    {
        internal T Selected { get; private set; }

        internal ChoiceDialog(string title, IEnumerable<T> source, Func<T, string> label)
        {
            Title = "JoinAssist · " + title;
            Width = 740;
            Height = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            UiTheme.Dialog(this);
            var dock = new DockPanel
            {
                Margin = new Thickness(16)
            };
            Content = dock;
            var search = WorkflowDialog.Input(dock, "Cerca", "");
            DockPanel.SetDock(search, Dock.Top);
            // Input adds its label separately; pin it above the searchable list too.
            DockPanel.SetDock(dock.Children[0], Dock.Top);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            dock.Children.Add(buttons);
            HelpGuide.Attach(this,buttons,()=>"workspace");
            var list = new ListBox();
            var values = source.ToList();
            Action filter = () =>
            {
                list.ItemsSource = values.Where(x => label(x).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).Select(x => new ChoiceRow<T> { Value = x, Text = label(x) }).ToList();
                if (list.Items.Count > 0)
                    list.SelectedIndex = 0;
            };
            search.TextChanged += (s, e) => filter();
            WorkflowDialog.Button(buttons, "Scegli", () =>
            {
                var row = list.SelectedItem as ChoiceRow<T>;
                if (row == null)
                    return;
                Selected = row.Value;
                DialogResult = true;
            });
            WorkflowDialog.Button(buttons, "Annulla", Close);
            dock.Children.Add(list);
            filter();
        }
    }

    internal sealed class ChoiceRow<T>
    {
        public T Value;
        public string Text;
        public override string ToString()
        {
            return Text;
        }
    }

    internal sealed class ColumnsDialog : Window
    {
        internal string Sql { get; private set; }

        internal ColumnsDialog(List<ColumnChoice> choices)
        {
            Title = "JoinAssist · Scegli colonne";
            Width = 720;
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            UiTheme.Dialog(this);
            var root = new DockPanel
            {
                Margin = new Thickness(16)
            };
            Content = root;
            var top = new StackPanel();
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
            var search = WorkflowDialog.Input(top, "Cerca colonne (le scelte restano selezionate)", "");
            var buttons = new WrapPanel();
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            HelpGuide.Attach(this,buttons,()=>"columns");
            var list = new ListBox();
            root.Children.Add(list);
            Action render = () =>
            {
                list.Items.Clear();
                foreach (var choice in choices.Where(c => c.Sql.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var item = choice;
                    var box = new CheckBox
                    {
                        Content = item.Sql,
                        ToolTip = item.Description,
                        IsChecked = item.Selected,
                        Margin = new Thickness(5)
                    };
                    box.Checked += (s, e) => item.Selected = true;
                    box.Unchecked += (s, e) => item.Selected = false;
                    list.Items.Add(box);
                }
            };
            search.TextChanged += (s, e) => render();
            WorkflowDialog.Button(buttons, "Seleziona visibili", () =>
            {
                foreach (var c in choices.Where(c => c.Sql.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0))
                    c.Selected = true;
                render();
            });
            WorkflowDialog.Button(buttons, "Nessuna", () =>
            {
                foreach (var c in choices)
                    c.Selected = false;
                render();
            });
            WorkflowDialog.Button(buttons, "Usa colonne", () =>
            {
                Sql = StarExpansion.Text(choices);
                DialogResult = true;
            });
            WorkflowDialog.Button(buttons, "Annulla", Close);
            render();
        }
    }
}
