using System;
using System.Collections.Generic;
using Automation.Core.Input;
using Automation.Core.Model;

namespace Automation.Core.Recording
{
    public enum RecordedKind
    {
        MouseDown,
        MouseUp,
        /// <summary>The cursor moved while a mouse button was held (only these moves are recorded).</summary>
        Move,
        Wheel,
        KeyDown,
        KeyUp
    }

    /// <summary>One raw input event captured while recording. Times are milliseconds from any fixed start.</summary>
    public struct RecordedEvent
    {
        public RecordedKind Kind;
        public long TimeMs;
        public int X;
        public int Y;
        public MouseButton Button;
        /// <summary>Wheel: positive away from the user (scroll up), in notches.</summary>
        public int Notches;
        /// <summary>Keyboard: Windows virtual-key code (left/right modifiers already resolved).</summary>
        public int VirtualKey;
    }

    /// <summary>
    /// Turns recorded raw input into actions:
    /// press and release of a mouse button at about the same place = Click; elsewhere = MouseDown + Move actions along the recorded path + MouseUp (a drag);
    /// wheel turns at one place = one Scroll with the number of notches; a key = Key press (held as long as it was);
    /// a key pressed while Ctrl / Alt / Shift / Win is held = Combo key. The pause before the next action becomes Delay.
    /// Positions are desktop coordinates (no Process).
    /// </summary>
    public static class RecordConverter
    {
        /// <summary>Time the cursor takes to glide to each recorded mouse position when replayed.</summary>
        public const int MoveTimeMs = 100;

        private const int ClickDistance = 4;
        private const int ClickTimeMs = 600;
        private const int ScrollMergeMs = 400;
        private const long MaxHoldMs = 10000;

        private sealed class Item
        {
            public ActionNode Node;
            public long Start;
            public long End;
            public int Seq;   // creation order, keeps a drag's MouseDown before its MouseUp when they start together
        }

        private static readonly HashSet<int> Modifiers = new HashSet<int> { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C };

        public static List<ActionNode> Build(IList<RecordedEvent> events)
        {
            var items = new List<Item>();
            var pendingMouse = new Dictionary<MouseButton, RecordedEvent>();
            var pendingPath = new Dictionary<MouseButton, List<RecordedEvent>>(); // cursor positions seen while the button was held
            var keysDown = new Dictionary<int, long>();          // key -> time pressed
            var modifierOrder = new List<int>();                  // modifiers held, in press order
            var usedModifiers = new HashSet<int>();               // modifiers that took part in a combination
            var comboFor = new Dictionary<int, List<int>>();      // non-modifier key -> modifiers held when it went down

            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case RecordedKind.MouseDown:
                        pendingMouse[e.Button] = e;
                        pendingPath[e.Button] = new List<RecordedEvent>();
                        break;

                    case RecordedKind.Move:
                        // Moves belong to every button currently held.
                        foreach (var path in pendingPath.Values) path.Add(e);
                        break;

                    case RecordedKind.MouseUp:
                        if (!pendingMouse.TryGetValue(e.Button, out var down)) break; // its press was not recorded
                        pendingMouse.Remove(e.Button);
                        var points = pendingPath.TryGetValue(e.Button, out var p) ? p : new List<RecordedEvent>();
                        pendingPath.Remove(e.Button);
                        bool click = Math.Abs(e.X - down.X) <= ClickDistance && Math.Abs(e.Y - down.Y) <= ClickDistance && e.TimeMs - down.TimeMs <= ClickTimeMs;
                        foreach (var pt in points)
                            if (Math.Abs(pt.X - down.X) > ClickDistance || Math.Abs(pt.Y - down.Y) > ClickDistance) click = false; // wandered away and came back: still a drag
                        if (click)
                        {
                            items.Add(new Item { Node = Mouse(MouseAction.Click, down.Button, down.X, down.Y, MoveTimeMs), Start = down.TimeMs, End = e.TimeMs });
                        }
                        else
                        {
                            // A drag: press, follow the recorded path with Move actions, then release.
                            items.Add(new Item { Node = Mouse(MouseAction.MouseDown, down.Button, down.X, down.Y, MoveTimeMs), Start = down.TimeMs, End = down.TimeMs });
                            long previous = down.TimeMs;
                            foreach (var pt in Simplify(down, points, e))
                            {
                                long step = Math.Min(MaxHoldMs, Math.Max(0, pt.TimeMs - previous));
                                items.Add(new Item { Node = Mouse(MouseAction.Move, MouseButton.Left, pt.X, pt.Y, step), Start = previous, End = Math.Max(previous, pt.TimeMs) });
                                previous = Math.Max(previous, pt.TimeMs);
                            }
                            long glide = Math.Min(MaxHoldMs, Math.Max(0, e.TimeMs - previous));
                            items.Add(new Item { Node = Mouse(MouseAction.MouseUp, down.Button, e.X, e.Y, glide), Start = previous, End = Math.Max(previous, e.TimeMs) });
                        }
                        break;

                    case RecordedKind.Wheel:
                        if (e.Notches == 0) break;
                        var action = e.Notches > 0 ? MouseAction.ScrollUp : MouseAction.ScrollDown;
                        var last = items.Count > 0 ? items[items.Count - 1] : null;
                        if (last != null && last.Node.Category == ActionCategory.Mouse && last.Node.Mouse.Action == action
                            && Math.Abs(last.Node.Mouse.X - e.X) <= ClickDistance && Math.Abs(last.Node.Mouse.Y - e.Y) <= ClickDistance
                            && e.TimeMs - last.End <= ScrollMergeMs)
                        {
                            last.Node.Mouse.ScrollNotches += Math.Abs(e.Notches); // one continuous turn of the wheel
                            last.End = e.TimeMs;
                        }
                        else
                        {
                            var node = Mouse(action, MouseButton.Middle, e.X, e.Y, MoveTimeMs);
                            node.Mouse.ScrollNotches = Math.Abs(e.Notches);
                            items.Add(new Item { Node = node, Start = e.TimeMs, End = e.TimeMs });
                        }
                        break;

                    case RecordedKind.KeyDown:
                        if (keysDown.ContainsKey(e.VirtualKey)) break; // auto-repeat while held
                        keysDown[e.VirtualKey] = e.TimeMs;
                        if (Modifiers.Contains(e.VirtualKey))
                        {
                            modifierOrder.Add(e.VirtualKey);
                        }
                        else if (modifierOrder.Count > 0)
                        {
                            comboFor[e.VirtualKey] = new List<int>(modifierOrder);
                            foreach (int m in modifierOrder) usedModifiers.Add(m);
                        }
                        break;

                    case RecordedKind.KeyUp:
                        if (!keysDown.TryGetValue(e.VirtualKey, out long pressed)) break;
                        keysDown.Remove(e.VirtualKey);
                        long hold = Math.Min(MaxHoldMs, Math.Max(0, e.TimeMs - pressed));
                        if (Modifiers.Contains(e.VirtualKey))
                        {
                            modifierOrder.Remove(e.VirtualKey);
                            // A modifier pressed and released on its own is a key press of its own.
                            if (!usedModifiers.Remove(e.VirtualKey))
                                AddKey(items, KeyboardAction.KeyPress, Name(e.VirtualKey), hold, pressed, e.TimeMs);
                        }
                        else if (comboFor.TryGetValue(e.VirtualKey, out var mods))
                        {
                            comboFor.Remove(e.VirtualKey);
                            var names = new List<string>();
                            foreach (int m in mods) names.Add(Name(m));
                            names.Add(Name(e.VirtualKey));
                            AddKey(items, KeyboardAction.ComboKey, KeyCodes.Join(names), hold, pressed, e.TimeMs);
                        }
                        else
                        {
                            AddKey(items, KeyboardAction.KeyPress, Name(e.VirtualKey), hold, pressed, e.TimeMs);
                        }
                        break;
                }
            }

            // Order by when each action started; the pause until the next one becomes this one's Delay.
            for (int i = 0; i < items.Count; i++) items[i].Seq = i;
            items.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Seq.CompareTo(b.Seq));
            var result = new List<ActionNode>();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                long delay = 0;
                if (i + 1 < items.Count)
                {
                    var next = items[i + 1];
                    long nextLead = next.Node.Category == ActionCategory.Mouse && next.Node.Mouse.Action != MouseAction.MouseUp ? next.Node.Mouse.TimeMs : 0;
                    delay = Math.Max(0, next.Start - item.End - nextLead);
                }
                SetDelay(item.Node, delay);
                result.Add(item.Node);
            }
            return result;
        }

        /// <summary>A drag path may deviate this many pixels from the straight line between kept points.</summary>
        public const double PathTolerance = 1.0;

        /// <summary>
        /// Ramer-Douglas-Peucker: keeps the points that give the path its shape (the corners and curves) and drops those
        /// that lie within <see cref="PathTolerance"/> of the line between their neighbours. The start and end are fixed.
        /// </summary>
        private static List<RecordedEvent> Simplify(RecordedEvent start, List<RecordedEvent> middle, RecordedEvent end)
        {
            var all = new List<RecordedEvent>(middle.Count + 2) { start };
            all.AddRange(middle);
            all.Add(end);
            var keep = new bool[all.Count];
            keep[0] = keep[all.Count - 1] = true;
            var stack = new Stack<KeyValuePair<int, int>>();
            stack.Push(new KeyValuePair<int, int>(0, all.Count - 1));
            while (stack.Count > 0)
            {
                var range = stack.Pop();
                int far = -1;
                double max = PathTolerance;
                for (int i = range.Key + 1; i < range.Value; i++)
                {
                    double d = DistanceToLine(all[i], all[range.Key], all[range.Value]);
                    if (d > max) { max = d; far = i; }
                }
                if (far < 0) continue;
                keep[far] = true;
                stack.Push(new KeyValuePair<int, int>(range.Key, far));
                stack.Push(new KeyValuePair<int, int>(far, range.Value));
            }
            var result = new List<RecordedEvent>();
            for (int i = 1; i < all.Count - 1; i++) if (keep[i]) result.Add(all[i]);
            return result;
        }

        private static double DistanceToLine(RecordedEvent p, RecordedEvent a, RecordedEvent b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-9) return Math.Sqrt((p.X - a.X) * (double)(p.X - a.X) + (p.Y - a.Y) * (double)(p.Y - a.Y));
            return Math.Abs(dy * (p.X - a.X) - dx * (p.Y - a.Y)) / length;
        }

        private static string Name(int virtualKey)
        {
            return KeyCodes.TryGetName(virtualKey, out string name) ? name : null;
        }

        private static void AddKey(List<Item> items, KeyboardAction action, string value, long hold, long start, long end)
        {
            if (string.IsNullOrEmpty(value) || value.Contains("++") || value.StartsWith("+") || value.EndsWith("+")) return; // a key without a name cannot be replayed
            var node = ActionNode.NewKeyboard(new KeyboardSettings { Action = action, Value = value, TimeMs = hold, DelayMs = 0 });
            items.Add(new Item { Node = node, Start = start, End = end });
        }

        private static ActionNode Mouse(MouseAction action, MouseButton button, int x, int y, long timeMs)
        {
            return ActionNode.NewMouse(new MouseSettings { Action = action, Button = button, X = x, Y = y, TimeMs = timeMs, DelayMs = 0 });
        }

        private static void SetDelay(ActionNode node, long delay)
        {
            if (node.Category == ActionCategory.Mouse) node.Mouse.DelayMs = delay;
            else if (node.Category == ActionCategory.Keyboard) node.Keyboard.DelayMs = delay;
        }
    }
}
