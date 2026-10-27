using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JoinAssist;
using Microsoft.VisualStudio.PlatformUI;

static class WorkflowWindowTests
{
    static int checks;
    static void Check(bool value, string name)
    {
        checks++;
        if (!value)
            throw new Exception("Workflow UI: " + name);
    }

    static object Field(object value, string name)
    {
        return value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    }

    static Button FindButton(Panel panel, string text)
    {
        return panel.Children.OfType<Button>().Single(b => (string)b.Content == text);
    }

    internal static void Run(Assembly plugin)
    {
        var saved = PreferenceStore.Current;
        var apply = typeof(PreferenceStore).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
        try
        {
            apply.Invoke(null, new object[] { new Preferences() });
            RunCore(plugin);
        }
        finally
        {
            apply.Invoke(null, new object[] { saved });
        }
    }

    static void RunCore(Assembly plugin)
    {
        var type = plugin.GetType("JoinAssist.WorkflowDialog");
        string file = Path.Combine(Path.GetTempPath(), "JoinAssist-ui-" + Guid.NewGuid().ToString("N") + ".json");
        var window = (Window)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { null, null, null, null, null, file }, null);
        try
        {
            var list = (ListBox)Field(window, "actionList");
            var options = (StackPanel)Field(window, "options");
            var preview = (TextBox)Field(window, "preview");
            Check(list.Items.Count == 14, "all action families visible in palette");
            var actions = list.Items.Cast<object>().ToList();
            foreach (var action in actions)
            {
                list.SelectedItem = action;
                Check(options.Children.Count > 0, "action builds parameter UI: " + action);
            }

            list.SelectedItem = actions.Single(a => a.ToString() == "Elenco IN dagli appunti");
            options.Children.OfType<TextBox>().Single().Text = "O'Brien\n001";
            FindButton(options, "Prepara elenco").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(preview.Text == "IN (N'O''Brien', N'001')" && ((Button)Field(window, "insert")).IsEnabled, "clipboard preview uses generated literal SQL: " + preview.Text + " / " + ((TextBlock)Field(window, "status")).Text);
            foreach (string theme in new[]
            {
                "dark",
                "light"
            }

            )
            {
                window.Resources[EnvironmentColors.ToolWindowBackgroundBrushKey] = theme == "dark" ? new SolidColorBrush(Color.FromRgb(37, 37, 38)) : Brushes.White;
                window.Resources[EnvironmentColors.ToolWindowTextBrushKey] = theme == "dark" ? Brushes.WhiteSmoke : Brushes.Black;
                NativeMenuTests.Preview(window, "workflow-" + theme + ".png");
                Check(((SolidColorBrush)preview.Foreground).Color == (theme == "dark" ? Colors.WhiteSmoke : Colors.Black), "preview follows " + theme + " palette");
            }

            list.SelectedItem = actions.Single(a => a.ToString() == "Modello con campi");
            options.Children.OfType<TextBox>().Single().Text = "SELECT {{id:12}}, {{id}};";
            FindButton(options, "Prepara modello").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(preview.Text == "SELECT 12, 12;" && ((List<SnippetField>)Field(window, "fields")).Count == 2, "template UI retains inline field positions");
            ((TextBox)Field(window, "search")).Text = "temporale";
            Check(list.Items.Count == 1, "palette filters actions by description/name");
            Check(!PreferenceStore.Current.QuickActionsShortcut, "new shortcut is off by default");
        }
        finally
        {
            window.Close();
            if (File.Exists(file))
                File.Delete(file);
        }

        var choices = new List<ColumnChoice>
        {
            new ColumnChoice
            {
                Sql = "o.Id",
                Description = "int"
            },
            new ColumnChoice
            {
                Sql = "o.Name",
                Description = "nvarchar"
            }
        };
        var columns = (Window)Activator.CreateInstance(plugin.GetType("JoinAssist.ColumnsDialog"), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { choices }, null);
        try
        {
            var dock = (DockPanel)columns.Content;
            var rows = dock.Children.OfType<ListBox>().Single();
            ((CheckBox)rows.Items[0]).IsChecked = true;
            var search = dock.Children.OfType<StackPanel>().Single().Children.OfType<TextBox>().Single();
            search.Text = "Name";
            Check(rows.Items.Count == 1 && choices[0].Selected, "column search retains hidden selections");
            ((CheckBox)rows.Items[0]).IsChecked = true;
            Check(StarExpansion.Text(choices) == "o.Id, o.Name", "multiple selected columns retain source order");
            NativeMenuTests.Preview(columns, "workflow-columns.png");
        }
        finally
        {
            columns.Close();
        }

        var probe = (Window)Activator.CreateInstance(plugin.GetType("JoinAssist.RuleProbeDialog"), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { new PersonalRule { Table = "Orders" }, null }, null);
        NativeMenuTests.Preview(probe, "workflow-rule-probe.png");
        probe.Close();
        Console.WriteLine("PASS: " + checks + " workflow window checks (all actions, IN/template previews, themes, column selections, palette filter and opt-in shortcut). No SQL, clipboard or user files accessed.");
    }
}
