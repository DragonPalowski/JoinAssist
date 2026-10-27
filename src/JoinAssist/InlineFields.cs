using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace JoinAssist
{
    internal sealed class InlineFields
    {
        sealed class Slot
        {
            internal string Name;
            internal ITrackingSpan Span;
        }

        readonly IWpfTextView view;
        readonly List<Slot> slots = new List<Slot>();
        List<string> order;
        int index, generation;
        bool updating;
        internal bool Active
        {
            get
            {
                return slots.Count > 0;
            }
        }

        internal InlineFields(IWpfTextView owner)
        {
            view = owner;
        }

        internal void Start(int offset, List<SnippetField> fields)
        {
            Stop();
            if (fields == null || fields.Count == 0)
                return;
            var snapshot = view.TextBuffer.CurrentSnapshot;
            foreach (var f in fields)
            {
                if (f.Start < 0 || f.Length < 0 || offset + f.Start + f.Length > snapshot.Length)
                {
                    Stop();
                    return;
                }

                slots.Add(new Slot { Name = f.Name, Span = snapshot.CreateTrackingSpan(offset + f.Start, f.Length, SpanTrackingMode.EdgeInclusive) });
            }

            order = slots.Select(s => s.Name).Distinct(StringComparer.Ordinal).ToList();
            index = 0;
            Select();
        }

        internal void Stop()
        {
            generation++;
            slots.Clear();
        }

        void Select()
        {
            var span = slots.First(s => s.Name == order[index]).Span.GetSpan(view.TextBuffer.CurrentSnapshot);
            view.Caret.MoveTo(span.End);
            view.Selection.Select(span, false);
            view.ViewScroller.EnsureSpanVisible(span);
        }

        internal bool Key(Key key, bool backwards = false)
        {
            if (!Active)
                return false;
            if (key == System.Windows.Input.Key.Escape)
            {
                Stop();
                view.Selection.Clear();
                return true;
            }

            if (key != System.Windows.Input.Key.Tab)
                return false;
            if (backwards)
            {
                index = Math.Max(0, index - 1);
                Select();
                return true;
            }

            if (++index < order.Count)
                Select();
            else
            {
                var end = slots.Max(s => s.Span.GetSpan(view.TextBuffer.CurrentSnapshot).End.Position);
                Stop();
                view.Selection.Clear();
                view.Caret.MoveTo(new SnapshotPoint(view.TextBuffer.CurrentSnapshot, end));
            }

            return true;
        }

        internal void Changed(TextContentChangedEventArgs e)
        {
            if (!Active || updating)
                return;
            var active = slots.First(s => s.Name == order[index]);
            var before = active.Span.GetSpan(e.Before);
            if (e.Changes.Count != 1 || e.Changes[0].OldPosition < before.Start.Position || e.Changes[0].OldEnd > before.End.Position)
            {
                Stop();
                return;
            }

            int pending = generation;
            var after = e.After;
            string value = active.Span.GetText(after);
            view.VisualElement.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!Active || generation != pending || view.IsClosed || !ReferenceEquals(after, view.TextBuffer.CurrentSnapshot))
                    return;
                Synchronize(after, active.Name, value);
            }), DispatcherPriority.Background);
        }

        internal void Synchronize(ITextSnapshot after, string name, string value)
        {
            if (!Active || !ReferenceEquals(after, view.TextBuffer.CurrentSnapshot))
                return;
            var active = slots.First(s => s.Name == name);
            var targets = slots.Where(s => s.Name == active.Name && !ReferenceEquals(s, active)).Select(s => s.Span.GetSpan(after)).OrderByDescending(s => s.Start.Position).ToList();
            if (targets.Any(s => s.IntersectsWith(active.Span.GetSpan(after))))
            {
                Stop();
                return;
            }

            var caret = after.CreateTrackingPoint(view.Caret.Position.BufferPosition.Position, PointTrackingMode.Positive);
            updating = true;
            try
            {
                if (targets.Count > 0)
                    using (var edit = view.TextBuffer.CreateEdit())
                    {
                        foreach (var span in targets)
                            if (!edit.Replace(span, value))
                                throw new InvalidOperationException("Campo non modificabile.");
                        edit.Apply();
                    }

                view.Caret.MoveTo(caret.GetPoint(view.TextBuffer.CurrentSnapshot));
            }
            catch
            {
                Stop();
            }
            finally
            {
                updating = false;
            }
        }
    }
}
