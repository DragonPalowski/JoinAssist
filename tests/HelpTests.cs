using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using JoinAssist;

static class HelpTests
{
    static int checks;
    static void Check(bool condition, string name)
    {
        checks++;
        if (!condition)
            throw new Exception("Help: " + name);
    }

    static object Field(object value, string name)
    {
        return value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value);
    }

    static object Call(object value, string method, params object[] args)
    {
        return value.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(value, args);
    }

    static string Value(object topic, string name)
    {
        return (string)Field(topic, name);
    }

    static IEnumerable<object> Search(Type type, string query)
    {
        return ((IEnumerable)type.GetMethod("Search", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { query })).Cast<object>();
    }

    internal static void Run(Assembly plugin)
    {
        var type = plugin.GetType("JoinAssist.HelpContent");
        var topics = ((IEnumerable)type.GetField("Topics", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Cast<object>().ToList();
        var ids = topics.Select(t => Value(t, "Id")).ToList();
        Check(topics.Count >= 29 && ids.Distinct().Count() == ids.Count, "unique topics cover completion, settings and every workspace action");
        foreach (var topic in topics)
        {
            string id = Value(topic, "Id");
            Check(!string.IsNullOrWhiteSpace(Value(topic, "Title")) && !string.IsNullOrWhiteSpace(Value(topic, "Summary")) && !string.IsNullOrWhiteSpace(Value(topic, "Notes")), id + " includes purpose and limits");
            Check(((string[])Field(topic, "Steps")).All(s => !string.IsNullOrWhiteSpace(s)) && ((string[])Field(topic, "Steps")).Length >= 2, id + " contains practical steps");
            Check(((string[])Field(topic, "Related")).All(ids.Contains), id + " has valid related links");
            string action = Value(topic, "Action");
            Check(action == null || action == "updates" || action == "diagnostics" || action.StartsWith("settings:", StringComparison.Ordinal) || action.StartsWith("workflow:", StringComparison.Ordinal), id + " only routes known plugin windows");
        }

        Check(Search(type, "").Count() == topics.Count, "empty search lists all topics");
        Check(Search(type, "  \t ").Count() == topics.Count, "whitespace search lists all topics");
        Check(Search(type, "cardinalita").Any(t => Value(t, "Id") == "paths"), "accent insensitive search");
        Check(Search(type, "rollback DELETE").Any(t => Value(t, "Id") == "delete"), "AND search across full text, case insensitive");
        Check(Search(type, "key value type").Any(t => Value(t, "Id") == "sources"), "terms found in steps, not just headings");
        Check(!Search(type, "qzxvnomatch").Any(), "no matches is explicit");
        var dialogType = plugin.GetType("JoinAssist.HelpDialog");
        string routed = null;
        var window = (Window)Activator.CreateInstance(dialogType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { new Action<string>(s => routed = s), true }, null);
        try
        {
            var list = (ListBox)Field(window, "topics");
            var article = (StackPanel)Field(window, "article");
            var search = (TextBox)Field(window, "search");
            Check(list.Items.Count == topics.Count, "guide works without query/metadata capture");
            foreach (var topic in topics)
            {
                Call(window, "SelectTopic", Value(topic, "Id"));
                Check(ReferenceEquals(list.SelectedItem, topic) && article.Children.OfType<TextBlock>().Any(b => b.Text == Value(topic, "Title")), "topic renders " + Value(topic, "Id"));
                string action = Value(topic, "Action");
                if (action != null)
                {
                    routed = null;
                    article.Children.OfType<Button>().Single(b => (string)b.Content == Value(topic, "ActionLabel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(routed == action, "explicit action routes without inserting/executing SQL: " + Value(topic, "Id"));
                }
            }

            Call(window, "SelectTopic", "snippets");
            search.Text = "zzzznonesiste";
            Check(list.Items.Count == 0 && article.Children.OfType<TextBlock>().Any(b => b.Text == "Nessun argomento trovato"), "no-result article replaces previous instructions");
            Call(window, "SelectTopic", "snippets");
            Check(search.Text == "" && Value(list.SelectedItem, "Id") == "snippets", "context entry clears old search and selects exact topic");
            article.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Value(list.SelectedItem, "Id") == "templates", "related topic navigation");
            foreach (string theme in new[]
            {
                "dark",
                "light"
            }

            )
            {
                var background = theme == "dark" ? new SolidColorBrush(Color.FromRgb(37, 37, 38)) : Brushes.White;
                var foreground = theme == "dark" ? Brushes.WhiteSmoke : Brushes.Black;
                window.Resources[EnvironmentColors.ToolWindowBackgroundBrushKey] = background;
                window.Resources[EnvironmentColors.ToolWindowTextBrushKey] = foreground;
                Call(window, "SelectTopic", "snippets");
                NativeMenuTests.Preview(window, "help-" + theme + ".png");
                var code = article.Children.OfType<TextBox>().Single();
                Check(((SolidColorBrush)code.Foreground).Color == foreground.Color, "code text follows " + theme + " host theme");
                Check(((SolidColorBrush)code.Background).Color == ((SolidColorBrush)background).Color, "code surface follows " + theme + " host theme");
            }

            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            Call(window, "SelectTopic", "delete");
            NativeMenuTests.Preview(window, "help-small.png");
            Check(((ScrollViewer)Field(window, "scroll")).ScrollableHeight > 0, "long topics remain scrollable in compact window");
        }
        finally
        {
            window.Close();
        }

        var blocked = (Window)Activator.CreateInstance(dialogType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { new Action<string>(s => routed = s), false }, null);
        try
        {
            Call(blocked, "SelectTopic", "rules");
            var panel = (StackPanel)Field(blocked, "article");
            Check(!panel.Children.OfType<Button>().Single(b => (string)b.Content == "Apri regole personali").IsEnabled, "modal editing does not open conflicting tools");
        }
        finally
        {
            blocked.Close();
        }

        ContextButtons(plugin);
        WorkspaceRoutes(plugin);
        Console.WriteLine("PASS: " + checks + " help checks (offline topic coverage, search, links, context actions, themes, compact layout and no query/database access).");
    }

    static void ContextButtons(Assembly plugin)
    {
        var guide = plugin.GetType("JoinAssist.HelpGuide");
        var requested = guide.GetEvent("Requested", BindingFlags.Static | BindingFlags.NonPublic);
        string topic = null;
        Window owner = null;
        Action<string, Window> handler = (id, source) =>
        {
            topic = id;
            owner = source;
        };
        requested.GetAddMethod(true).Invoke(null, new object[] { handler });
        var settings = new PreferencesDialog();
        try
        {
            var tabs = ((DockPanel)settings.Content).Children.OfType<TabControl>().Single();
            var footer = ((DockPanel)settings.Content).Children.OfType<StackPanel>().Single();
            var help = footer.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single(b => (string)b.Content == "Guida");
            var expected = new[]
            {
                "cache",
                "format",
                "cache",
                "completion",
                "rules"
            };
            for (int i = 0; i < 5; i++)
            {
                tabs.SelectedIndex = i;
                help.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(topic == expected[i] && ReferenceEquals(owner, settings), "settings tab routes contextual help without saving: " + i);
            }

            Call(settings, "SelectPage", "Regole");
            Check(tabs.SelectedIndex == 4, "help can select settings page");
            var map = plugin.GetType("JoinAssist.HelpContent").GetMethod("WorkflowTopic", BindingFlags.Static | BindingFlags.NonPublic);
            Check((string)map.Invoke(null, new object[] { "Diagnostica e recupero" }) == "troubleshooting" && (string)map.Invoke(null, new object[] { "Prova una regola" }) == "probe", "recovery and probe have specific context topics");
        }
        finally
        {
            settings.Close();
            requested.GetRemoveMethod(true).Invoke(null, new object[] { handler });
        }
    }

    static void WorkspaceRoutes(Assembly plugin)
    {
        var type = plugin.GetType("JoinAssist.WorkflowDialog");
        var window = (Window)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { null, null, null, null, null, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "JoinAssist-help-" + Guid.NewGuid().ToString("N") + ".json") }, null);
        try
        {
            var actions = (ListBox)Field(window, "actionList");
            var map = plugin.GetType("JoinAssist.HelpContent").GetMethod("WorkflowTopic", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var action in actions.Items)
            {
                string id = (string)map.Invoke(null, new object[] { action.ToString() });
                Check(id != "workspace", "specific help for workspace action: " + action);
            }
        }
        finally
        {
            window.Close();
        }
    }
}
