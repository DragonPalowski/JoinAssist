using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JoinAssist
{
    internal sealed class RuleProbeDialog : Window
    {
        internal RuleProbeDialog(PersonalRule rule, Func<SsmsConnection> capture)
        {
            Title = "JoinAssist · Prova regola: " + rule.Name;
            Width = 760;
            Height = 560;
            MinWidth = 540;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            UiTheme.Dialog(this);
            var root = new DockPanel
            {
                Margin = new Thickness(16)
            };
            Content = root;
            var sample = WorkflowDialog.Input(root, "Esempio completo · nessuna modifica alla query o alle regole attive", rule.Kind == PersonalRuleKind.Snippet ? rule.Shortcut : "SELECT * FROM " + SqlName.Quote(rule.Schema) + "." + SqlName.Quote(rule.Table));
            DockPanel.SetDock(root.Children[0], Dock.Top);
            DockPanel.SetDock(sample, Dock.Top);
            sample.Height = 130;
            sample.AcceptsReturn = true;
            sample.FontFamily = new FontFamily("Consolas");
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            var result = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 10, 0, 0)
            };
            root.Children.Add(result);
            var test = WorkflowDialog.Button(buttons, "Prova", () =>
            {
            });
            WorkflowDialog.Button(buttons, "Chiudi", Close);
            CancellationTokenSource pending = null;
            Closed += (s, e) => pending?.Cancel();
            test.Click += async (s, e) =>
            {
                test.IsEnabled = false;
                pending = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try
                {
                    if (capture == null)
                    {
                        result.Text = await PersonalRules.Probe(rule, sample.Text, null, null, null, pending.Token);
                        return;
                    }

                    using (var c = capture())
                    {
                        var catalog = await CatalogReader.ReadAsync(c.Connection, pending.Token, c.CacheScope);
                        result.Text = await PersonalRules.Probe(rule, sample.Text, catalog, c.Server, c.Database, pending.Token);
                        if (!c.IsStillActive())
                            result.Text = "Connessione cambiata: riprova dalla query desiderata.";
                    }
                }
                catch (Exception ex)
                {
                    result.Text = ex is ArgumentException || ex is InvalidOperationException ? ex.Message : "Prova non disponibile: controlla connessione e permessi sui metadati.";
                }
                finally
                {
                    pending.Dispose();
                    pending = null;
                    test.IsEnabled = true;
                }
            };
        }
    }
}
