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
    /// press and release of a mouse button at about the same place = Click; elsewhere = MouseDown + MouseUp (a drag);
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
                        break;

                    case RecordedKind.MouseUp:
                        if (!pendingMouse.TryGetValue(e.Button, out var down)) break; // its press was not recorded
                        pendingMouse.Remove(e.Button);
                        bool click = Math.Abs(e.X - down.X) <= ClickDistance && Math.Abs(e.Y - down.Y) <= ClickDistance && e.TimeMs - down.TimeMs <= ClickTimeMs;
                        if (click)
                        {
                            items.Add(new Item { Node = Mouse(MouseAction.Click, down.Button, down.X, down.Y, MoveTimeMs), Start = down.TimeMs, End = e.TimeMs });
                        }
                        else
                        {
                            // A drag: press, then glide to the release point in about the time it took, then release.
                            items.Add(new Item { Node = Mouse(MouseAction.MouseDown, down.Button, down.X, down.Y, MoveTimeMs), Start = down.TimeMs, End = down.TimeMs });
                            long glide = Math.Min(MaxHoldMs, Math.Max(0, e.TimeMs - down.TimeMs));
                            items.Add(new Item { Node = Mouse(MouseAction.MouseUp, down.Button, e.X, e.Y, glide), Start = down.TimeMs, End = e.TimeMs });
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
