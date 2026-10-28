using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace JoinAssist
{
    internal static class HelpGuide
    {
        internal static event Action<string, Window> Requested;
        internal static void Open(string topic, Window owner = null)
        {
            Requested?.Invoke(topic, owner);
        }

        internal static void Attach(Window window, Panel buttons, Func<string> topic)
        {
            var help = new Button
            {
                Content = "Guida",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Guida della funzione (F1)"
            };
            buttons.Children.Insert(0, help);
            help.Click += (s, e) => Open(topic(), window);
            window.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
                {
                    e.Handled = true;
                    Open(topic(), window);
                }
            };
        }
    }

    internal sealed class HelpDialog : Window
    {
        readonly Action<string> execute;
        readonly bool allowActions;
        readonly TextBox search;
        readonly ListBox topics;
        readonly StackPanel article;
        readonly TextBlock count, notice;
        readonly ScrollViewer scroll;
        HelpTopic selected;
        internal HelpDialog(Action<string> command = null, bool actionsAllowed = true)
        {
            execute = command;
            allowActions = actionsAllowed;
            Title = "JoinAssist — Guida";
            Width = 1040;
            Height = 760;
            MinWidth = 760;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            UiTheme.Dialog(this);
            var root = new DockPanel
            {
                Margin = new Thickness(18)
            };
            Content = root;
            var header = new StackPanel();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            header.Children.Add(new TextBlock { Text = "Guida a JoinAssist", FontSize = 24, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = "Istruzioni ed esempi inclusi nel plugin · consultazione offline", Margin = new Thickness(0, 4, 0, 12), TextWrapping = TextWrapping.Wrap });
            header.Children.Add(new TextBlock { Text = "Cerca una funzione o un termine (Ctrl+F)", Margin = new Thickness(0, 0, 0, 4) });
            search = new TextBox
            {
                MinHeight = 30,
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 0, 8)
            };
            System.Windows.Automation.AutomationProperties.SetName(search, "Cerca nella guida");
            header.Children.Add(search);
            var footer = new DockPanel
            {
                Margin = new Thickness(0, 12, 0, 0)
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            var close = new Button
            {
                Content = "Chiudi",
                IsCancel = true,
                Padding = new Thickness(18, 6, 18, 6)
            };
            DockPanel.SetDock(close, Dock.Right);
            footer.Children.Add(close);
            close.Click += (s, e) => Close();
            notice = new TextBlock
            {
                Text = "Gli esempi sono dimostrativi: copiarli non li esegue né li inserisce nella query.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            footer.Children.Add(notice);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270), MinWidth = 180 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 300 });
            root.Children.Add(grid);
            var navigation = new DockPanel
            {
                Margin = new Thickness(0, 0, 10, 0)
            };
            grid.Children.Add(navigation);
            count = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap
            };
            DockPanel.SetDock(count, Dock.Top);
            navigation.Children.Add(count);
            topics = new ListBox
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(topics, ScrollBarVisibility.Disabled);
            navigation.Children.Add(topics);
            var row = new FrameworkElementFactory(typeof(TextBlock));
            row.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            row.SetValue(FrameworkElement.MarginProperty, new Thickness(6));
            row.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            topics.ItemTemplate = new DataTemplate
            {
                VisualTree = row
            };
            var splitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ResizeDirection = GridResizeDirection.Columns
            };
            Grid.SetColumn(splitter, 1);
            UiTheme.Surface(splitter);
            grid.Children.Add(splitter);
            article = new StackPanel
            {
                Margin = new Thickness(18, 0, 8, 12)
            };
            scroll = new ScrollViewer
            {
                Content = article,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetColumn(scroll, 2);
            grid.Children.Add(scroll);
            topics.SelectionChanged += (s, e) => Render(topics.SelectedItem as HelpTopic);
            search.TextChanged += (s, e) => Filter();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    search.Focus();
                    search.SelectAll();
                    e.Handled = true;
                }
            };
            Filter();
        }

        internal void SelectTopic(string id)
        {
            search.Text = "";
            topics.SelectedItem = HelpContent.Find(id);
            topics.ScrollIntoView(topics.SelectedItem);
        }

        void Filter()
        {
            var previous = selected;
            var matches = HelpContent.Search(search.Text);
            topics.ItemsSource = matches;
            count.Text = matches.Count + " / " + HelpContent.Topics.Length + " argomenti";
            topics.SelectedItem = matches.Contains(previous) ? previous : matches.FirstOrDefault();
            if (matches.Count == 0)
                Render(null);
        }

        void Text(string value, double size = 14, bool bold = false)
        {
            article.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 0, 12) });
        }

        void Render(HelpTopic topic)
        {
            selected = topic;
            article.Children.Clear();
            scroll.ScrollToTop();
            if (topic == null)
            {
                Text("Nessun argomento trovato", 22, true);
                Text("Prova meno parole, un nome di comando o termini come JOIN, appunti, campi, regole, cache.");
                return;
            }

            Text(topic.Category, 12);
            Text(topic.Title, 24, true);
            Text(topic.Summary);
            Text("Come si usa", 17, true);
            for (int i = 0; i < topic.Steps.Length; i++)
                Text((i + 1) + ". " + topic.Steps[i]);
            if (!string.IsNullOrEmpty(topic.Example))
            {
                Text("Esempio", 17, true);
                article.Children.Add(new TextBox { Text = topic.Example, IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(10), TextWrapping = TextWrapping.Wrap, MaxHeight = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 8) });
                var copy = new Button
                {
                    Content = "Copia esempio",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 5, 12, 5),
                    Margin = new Thickness(0, 0, 0, 16)
                };
                copy.Click += (s, e) =>
                {
                    try
                    {
                        Clipboard.SetText(topic.Example);
                        notice.Text = "Esempio copiato. Adatta i nomi e controlla il SQL prima di usarlo.";
                    }
                    catch (ExternalException)
                    {
                        notice.Text = "Appunti occupati: riprova oppure seleziona il testo dell'esempio e copialo.";
                    }
                };
                article.Children.Add(copy);
            }

            Text("Da sapere", 17, true);
            Text(topic.Notes);
            if (topic.Action != null)
            {
                var open = new Button
                {
                    Content = topic.ActionLabel,
                    IsEnabled = execute != null && allowActions,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                open.Click += (s, e) =>
                {
                    try
                    {
                        execute?.Invoke(topic.Action);
                    }
                    catch (Exception ex)when (ex is InvalidOperationException || ex is ArgumentException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
                    {
                        notice.Text = "Impossibile aprire lo strumento. Chiudi la finestra di modifica corrente, controlla la scheda SQL e riprova dal menu JoinAssist.";
                    }
                };
                article.Children.Add(open);
                if (!allowActions)
                    Text("Chiudi prima la finestra di modifica corrente per aprire un altro strumento. La guida resta consultabile.", 12);
            }

            Text("Vedi anche", 17, true);
            var links = new WrapPanel();
            article.Children.Add(links);
            foreach (string id in topic.Related)
            {
                var related = HelpContent.Find(id);
                var link = new Button
                {
                    Content = related.Title,
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 0, 6, 6)
                };
                link.Click += (s, e) => SelectTopic(related.Id);
                links.Children.Add(link);
            }
        }
    }
}
