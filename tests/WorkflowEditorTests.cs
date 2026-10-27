using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Controls;
using System.Windows.Input;
using JoinAssist;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

static class WorkflowEditorTests
{
    static int checks;
    static void Check(bool value, string name)
    {
        checks++;
        if (!value)
            throw new Exception("Workflow editor: " + name);
    }

    static object Call(object owner, string method, params object[] args)
    {
        return owner.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, args);
    }

    static object Get(object owner, string field)
    {
        return owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    }

    internal static void Run(Assembly plugin)
    {
        var buffer = new Buffer("SELECT 12, 12, abc;");
        var type = plugin.GetType("JoinAssist.InlineFields");
        var fields = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { buffer.View }, null);
        var expansion = SnippetExpansion.Create("SELECT {{id:12}}, {{id}}, {{name:abc}};");
        Call(fields, "Start", 0, expansion.Fields);
        Check(buffer.SelectionStart == 7 && buffer.SelectionLength == 2, "initial field selected");
        buffer.Change(7, 2, "3456");
        Call(fields, "Synchronize", buffer.Snapshot, "id", "3456");
        Check(buffer.Text == "SELECT 3456, 3456, abc;" && buffer.Caret == 11, "longer edit mirrors exact field and preserves caret");
        Call(fields, "Key", Key.Tab, false);
        Check(buffer.Text.Substring(buffer.SelectionStart, buffer.SelectionLength) == "abc", "Tab skips repeated field and selects next name");
        Call(fields, "Key", Key.Tab, true);
        Check(buffer.Text.Substring(buffer.SelectionStart, buffer.SelectionLength) == "3456", "Shift+Tab returns to linked field");
        Call(fields, "Key", Key.Tab, false);
        Call(fields, "Key", Key.Tab, false);
        Check(!(bool)type.GetProperty("Active", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fields), "last Tab exits template");
        Call(fields, "Start", 0, new List<SnippetField> { new SnippetField { Name = "x", Start = 7, Length = 4 } });
        Call(fields, "Key", Key.Escape, false);
        Check(!(bool)type.GetProperty("Active", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fields), "Escape releases field session");
        Insertions(plugin);
        Console.WriteLine("PASS: " + checks + " workflow editor checks (linked field growth/navigation, exact insertion, one edit and stale text/caret/selection/view refusal). No SSMS window or database.");
    }

    static void Insertions(Assembly plugin)
    {
        foreach (string scenario in new[]
        {
            "insert",
            "replace",
            "text",
            "caret",
            "selection",
            "view",
            "connection",
            "template"
        }

        )
        {
            var b = new Buffer("SELECT foo;");
            b.Caret = 10;
            b.SelectionStart = 7;
            b.SelectionLength = 3;
            var auto = Activator.CreateInstance(plugin.GetType("JoinAssist.AutomaticCompletion"), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { b.View, null, null, new Action<string>(s =>
            {
            }) }, null);
            var controller = FormatterServices.GetUninitializedObject(plugin.GetType("JoinAssist.JoinEditorController"));
            controller.GetType().GetField("view", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, b.View);
            controller.GetType().GetField("automatic", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, auto);
            bool active = true;
            var context = Call(controller, "CaptureWorkflow", new Func<bool>(() => active), new Func<string, SsmsConnection>(sql => null));
            CommitConnection original = null;
            try
            {
                if (scenario == "connection")
                {
                    original = new CommitConnection();
                    var metadata = new CommitConnection();
                    var editors = Assembly.Load("SQLEditors");
                    var owner = FormatterServices.GetUninitializedObject(editors.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.SqlScriptEditorControl"));
                    GC.SuppressFinalize(owner);
                    editors.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptAndResultsEditorControl").GetField("m_connection", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, original);
                    var bound = typeof(SsmsConnection).GetMethod("Bind", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { owner, original, metadata, "TestDb" });
                    context.GetType().GetField("Connection", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(context, bound);
                    original.ClientConnectionId = Guid.NewGuid();
                }

                string expected = "SELECT Id;";
                bool replace = scenario == "replace";
                string insertion = replace ? "SELECT 1;" : "Id";
                List<SnippetField> fields = null;
                if (scenario == "text")
                    b.Change(0, 0, "-- changed\n");
                if (scenario == "caret")
                    b.Caret--;
                if (scenario == "selection")
                    b.SelectionLength = 2;
                if (scenario == "view")
                    active = false;
                if (replace)
                    expected = "SELECT 1;";
                if (scenario == "template")
                {
                    insertion = "12";
                    fields = new List<SnippetField>
                    {
                        new SnippetField
                        {
                            Name = "id",
                            Start = 0,
                            Length = 2
                        }
                    };
                    expected = "SELECT 12;";
                }

                var insert = (Action<string, bool, List<SnippetField>>)Get(context, "Insert");
                int before = b.Edits;
                bool refused = false;
                try
                {
                    insert(insertion, replace, fields);
                }
                catch (InvalidOperationException)
                {
                    refused = true;
                }

                bool stale = new[]
                {
                    "text",
                    "caret",
                    "selection",
                    "view",
                    "connection"
                }.Contains(scenario);
                Check(stale ? refused && b.Edits == before : !refused && b.Text == expected && b.Edits == before + 1, "snapshot insertion case " + scenario);
                if (scenario == "template")
                    Check(b.SelectionStart == 7 && b.SelectionLength == 2, "workflow template begins inline navigation");
            }
            finally
            {
                ((IDisposable)context).Dispose();
                ((IDisposable)auto).Dispose();
                original?.Dispose();
            }
        }
    }

    sealed class Track
    {
        internal int Start, End;
    }

    sealed class Buffer
    {
        internal string Text;
        internal int Caret, SelectionStart, SelectionLength, Edits;
        internal ITextSnapshot Snapshot;
        internal IWpfTextView View;
        readonly List<Track> spans = new List<Track>(), points = new List<Track>();
        InterfaceStub buffer;
        readonly Grid visual = new Grid();
        internal Buffer(string text)
        {
            Text = text;
            var edit = new InterfaceStub(typeof(ITextEdit), c =>
            {
                if (c.MethodName == "Replace")
                {
                    if (c.Args.Length == 2)
                    {
                        var span = (Span)c.Args[0];
                        Change(span.Start, span.Length, (string)c.Args[1]);
                    }
                    else
                        Change((int)c.Args[0], (int)c.Args[1], (string)c.Args[2]);
                    Edits++;
                    return true;
                }

                if (c.MethodName == "Apply")
                    return Snapshot;
                return null;
            });
            buffer = new InterfaceStub(typeof(ITextBuffer), c => c.MethodName == "get_CurrentSnapshot" ? (object)Snapshot : c.MethodName == "CreateEdit" ? edit.GetTransparentProxy() : null);
            Snapshot = MakeSnapshot(text);
            var mapping = (IMappingPoint)new InterfaceStub(typeof(IMappingPoint), null).GetTransparentProxy();
            Func<CaretPosition> position = () => new CaretPosition(new VirtualSnapshotPoint(new SnapshotPoint(Snapshot, Caret)), mapping, PositionAffinity.Successor);
            var caret = new InterfaceStub(typeof(ITextCaret), c =>
            {
                if (c.MethodName == "get_Position")
                    return position();
                if (c.MethodName == "MoveTo")
                {
                    Caret = ((SnapshotPoint)c.Args[0]).Position;
                    return position();
                }

                return null;
            });
            var selection = new InterfaceStub(typeof(ITextSelection), c =>
            {
                switch (c.MethodName)
                {
                    case "get_IsEmpty":
                        return SelectionLength == 0;
                    case "get_SelectedSpans":
                        return new NormalizedSnapshotSpanCollection(new SnapshotSpan(Snapshot, SelectionStart, SelectionLength));
                    case "get_StreamSelectionSpan":
                        return new VirtualSnapshotSpan(new SnapshotSpan(Snapshot, SelectionStart, SelectionLength));
                    case "Select":
                        var span = (SnapshotSpan)c.Args[0];
                        SelectionStart = span.Start.Position;
                        SelectionLength = span.Length;
                        break;
                    case "Clear":
                        SelectionStart = Caret;
                        SelectionLength = 0;
                        break;
                }

                return null;
            });
            var scroll = new InterfaceStub(typeof(IViewScroller), null);
            View = (IWpfTextView)new InterfaceStub(typeof(IWpfTextView), c =>
            {
                switch (c.MethodName)
                {
                    case "get_TextBuffer":
                        return buffer.GetTransparentProxy();
                    case "get_Caret":
                        return caret.GetTransparentProxy();
                    case "get_Selection":
                        return selection.GetTransparentProxy();
                    case "get_VisualElement":
                        return visual;
                    case "get_IsClosed":
                        return false;
                    case "get_HasAggregateFocus":
                        return false;
                    case "get_ViewScroller":
                        return scroll.GetTransparentProxy();
                }

                return null;
            }).GetTransparentProxy();
        }

        ITextSnapshot MakeSnapshot(string value)
        {
            ITextSnapshot snapshot = null;
            snapshot = (ITextSnapshot)new InterfaceStub(typeof(ITextSnapshot), c =>
            {
                if (c.MethodName == "get_Length")
                    return value.Length;
                if (c.MethodName == "get_TextBuffer")
                    return buffer.GetTransparentProxy();
                if (c.MethodName == "GetText")
                {
                    if (c.Args.Length == 0)
                        return value;
                    if (c.Args[0] is Span)
                    {
                        var span = (Span)c.Args[0];
                        return value.Substring(span.Start, span.Length);
                    }

                    return value.Substring((int)c.Args[0], (int)c.Args[1]);
                }

                if (c.MethodName == "CreateTrackingSpan")
                {
                    var t = new Track
                    {
                        Start = (int)c.Args[0],
                        End = (int)c.Args[0] + (int)c.Args[1]
                    };
                    spans.Add(t);
                    return new InterfaceStub(typeof(ITrackingSpan), a => a.MethodName == "GetSpan" ? (object)new SnapshotSpan((ITextSnapshot)a.Args[0], t.Start, t.End - t.Start) : a.MethodName == "GetText" ? ((ITextSnapshot)a.Args[0]).GetText(t.Start, t.End - t.Start) : null).GetTransparentProxy();
                }

                if (c.MethodName == "CreateTrackingPoint")
                {
                    var t = new Track
                    {
                        Start = (int)c.Args[0]
                    };
                    points.Add(t);
                    return new InterfaceStub(typeof(ITrackingPoint), a => a.MethodName == "GetPoint" ? (object)new SnapshotPoint((ITextSnapshot)a.Args[0], t.Start) : null).GetTransparentProxy();
                }

                return c.MethodBase is MethodInfo && ((MethodInfo)c.MethodBase).ReturnType.IsValueType ? Activator.CreateInstance(((MethodInfo)c.MethodBase).ReturnType) : null;
            }).GetTransparentProxy();
            return snapshot;
        }

        static int Map(int value, int start, int length, int replacement, bool positive)
        {
            if (value < start)
                return value;
            if (value > start + length)
                return value + replacement - length;
            return start + (positive ? replacement : 0);
        }

        internal void Change(int start, int length, string replacement)
        {
            foreach (var span in spans)
            {
                int a = Map(span.Start, start, length, replacement.Length, false), b = Map(span.End, start, length, replacement.Length, true);
                span.Start = a;
                span.End = b;
            }

            foreach (var point in points)
                point.Start = Map(point.Start, start, length, replacement.Length, true);
            Text = Text.Remove(start, length).Insert(start, replacement);
            Snapshot = MakeSnapshot(Text);
            Caret = start + replacement.Length;
            SelectionStart = Caret;
            SelectionLength = 0;
        }
    }
}
