using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using AutomationTest.Core.Model;
using AutomationTest.Core.Runner;

namespace AutomationTest.Tests.Suites
{
    /// <summary>
    /// Program Close, against windows of the tests' own dummy program only (AtDummyApp.exe, next to the test program).
    /// Nothing here ever looks for or closes another program.
    /// </summary>
    internal static class ProgramSuite
    {
        private static string DummyPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AtDummyApp.exe"); }
        }

        private static string UniqueTitle(string prefix)
        {
            return prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static Process Start(string title, bool refuse)
        {
            var p = Process.Start(DummyPath, "\"" + title + "\"" + (refuse ? " refuse" : ""));
            // The dummy window is not in the taskbar, so .NET does not see it as the main window: look for it the way the actions do.
            for (int i = 0; i < 80 && ProgramActions.FindWindows(ProgramMatch.Id, p.Id.ToString(), false).Count == 0; i++) Thread.Sleep(100);
            return p;
        }

        private static IntPtr HandleOf(Process p)
        {
            var found = ProgramActions.FindWindows(ProgramMatch.Id, p.Id.ToString(), false);
            return found.Count == 0 ? IntPtr.Zero : found[0];
        }

        private static int Close(ProgramMatch match, string value, bool all = false, bool kill = false, long timeoutMs = 5000)
        {
            return ProgramActions.Close(match, value, all, kill, timeoutMs, CancellationToken.None);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);

        private static ActionNode Window(CommandKind kind, ProgramMatch match, string value, bool all = false, int x = 0, int y = 0, int width = 0, int height = 0)
        {
            return ActionNode.NewCommand(new CommandSettings { Kind = kind, Match = match, Value = value, All = all, X = x, Y = y, Width = width, Height = height });
        }

        private static void Run(params ActionNode[] actions)
        {
            new ScriptRunner(new AutomationTest.Core.Input.MouseController()).Run(new List<ActionNode>(actions), null, CancellationToken.None);
        }

        [Suite("program.windows")]
        public static void Windows(T t)
        {
            var started = new List<Process>();
            try
            {
                string title = UniqueTitle("AtWin");
                var p = Start(title, false); started.Add(p);
                IntPtr hwnd = HandleOf(p);
                string pattern = "^" + title + "$";

                Run(Window(CommandKind.WindowMove, ProgramMatch.Title, pattern, x: 120, y: 130));
                int x, y, w, h;
                ProgramActions.GetBounds(hwnd, out x, out y, out w, out h);
                t.True("Move puts the top left corner there (" + x + ", " + y + ")", x == 120 && y == 130);

                Run(Window(CommandKind.WindowResize, ProgramMatch.Title, pattern, width: 420, height: 260));
                ProgramActions.GetBounds(hwnd, out x, out y, out w, out h);
                t.True("Resize gives the width and height (" + w + " x " + h + ")", w == 420 && h == 260);
                t.True("Resize keeps the place", x == 120 && y == 130);

                Run(Window(CommandKind.WindowMinimize, ProgramMatch.Title, pattern));
                t.True("Minimize", IsIconic(hwnd));
                Run(Window(CommandKind.WindowRestore, ProgramMatch.Title, pattern));
                t.False("Restore after Minimize", IsIconic(hwnd));
                Run(Window(CommandKind.WindowMaximize, ProgramMatch.Title, pattern));
                t.True("Maximize", IsZoomed(hwnd));
                Run(Window(CommandKind.WindowRestore, ProgramMatch.Title, pattern));
                t.False("Restore after Maximize", IsZoomed(hwnd));

                // a minimized window is restored to be moved
                Run(Window(CommandKind.WindowMinimize, ProgramMatch.Title, pattern));
                Run(Window(CommandKind.WindowMove, ProgramMatch.Title, pattern, x: 200, y: 210));
                ProgramActions.GetBounds(hwnd, out x, out y, out w, out h);
                t.True("a minimized window can be moved (it is restored first)", !IsIconic(hwnd) && x == 200 && y == 210);

                // Activate does not fail (whether Windows lets it to the front depends on who has the focus)
                Run(Window(CommandKind.WindowActivate, ProgramMatch.Title, pattern));
                t.True("Activate runs", true);

                // found by process id and by process name, and all windows of a name
                Run(Window(CommandKind.WindowMove, ProgramMatch.Id, p.Id.ToString(), x: 50, y: 60));
                ProgramActions.GetBounds(hwnd, out x, out y, out w, out h);
                t.True("by process id", x == 50 && y == 60);
                var second = Start(UniqueTitle("AtWin2"), false); started.Add(second);
                Run(Window(CommandKind.WindowMove, ProgramMatch.Name, "AtDummyApp", all: true, x: 300, y: 310));
                int x2, y2, w2, h2;
                ProgramActions.GetBounds(HandleOf(second), out x2, out y2, out w2, out h2);
                ProgramActions.GetBounds(hwnd, out x, out y, out w, out h);
                t.True("All moves every window of the name", x == 300 && y == 310 && x2 == 300 && y2 == 310);
                Run(Window(CommandKind.WindowMove, ProgramMatch.Name, "AtDummyApp", x: 10, y: 20));
                ProgramActions.GetBounds(hwnd, out x, out y, out w, out h);
                ProgramActions.GetBounds(HandleOf(second), out x2, out y2, out w2, out h2);
                t.True("without All only one window moves", (x == 10 && y == 20) != (x2 == 10 && y2 == 20));

                // by path
                Run(Window(CommandKind.WindowMove, ProgramMatch.Path, DummyPath, all: true, x: 70, y: 80));
                ProgramActions.GetBounds(hwnd, out x, out y, out w, out h);
                t.True("by path", x == 70 && y == 80);

                // problems are said clearly
                t.Throws("no such window", () => Run(Window(CommandKind.WindowMinimize, ProgramMatch.Title, "^NoSuchWindow-" + Guid.NewGuid().ToString("N") + "$")), "No window found");
                t.Throws("nothing to look for", () => Run(Window(CommandKind.WindowMinimize, ProgramMatch.Title, "  ")), "Enter the window");
                t.Throws("a size of 0", () => Run(Window(CommandKind.WindowResize, ProgramMatch.Title, pattern, width: 0, height: 100)), "at least 1");
                t.Throws("a bad regular expression", () => Run(Window(CommandKind.WindowMinimize, ProgramMatch.Title, "(")), "regular expression");

                // the window value can be an expression
                var expr = Window(CommandKind.WindowMove, ProgramMatch.Title, "\"^\" + {t} + \"$\"", x: 15, y: 25);
                expr.Command.ValueIsExpression = true;
                var runner = new ScriptRunner(new AutomationTest.Core.Input.MouseController());
                runner.Run(new List<ActionNode> { expr }, new Dictionary<string, object> { { "t", title } }, CancellationToken.None);
                ProgramActions.GetBounds(hwnd, out x, out y, out w, out h);
                t.True("a window found by an expression", x == 15 && y == 25);
            }
            finally
            {
                foreach (var p in started)
                {
                    try { if (!p.HasExited) p.Kill(); } catch (Exception) { }
                    p.Dispose();
                }
            }
        }

        [Suite("program.close")]
        public static void CloseSuite(T t)
        {
            t.True("the dummy program is next to the tests", File.Exists(DummyPath));
            var started = new System.Collections.Generic.List<Process>();
            try
            {
                string title = UniqueTitle("AtDummy");
                var byTitle = Start(title, false); started.Add(byTitle);
                t.Eq("by title", Close(ProgramMatch.Title, "^" + title + "$"), 1);
                t.True("by title: it ended", byTitle.WaitForExit(3000));

                var byId = Start(UniqueTitle("AtDummy"), false); started.Add(byId);
                t.Eq("by id", Close(ProgramMatch.Id, byId.Id.ToString()), 1);
                t.True("by id: it ended", byId.WaitForExit(3000));

                var first = Start(UniqueTitle("AtDummy-n1"), false); started.Add(first);
                var second = Start(UniqueTitle("AtDummy-n2"), false); started.Add(second);
                t.Eq("by name closes only the first", Close(ProgramMatch.Name, "AtDummyApp.exe"), 1);
                Thread.Sleep(800);
                t.Eq("by name: exactly one is left", new[] { first, second }.Count(x => !x.HasExited), 1);
                t.Eq("by name closes the last one too", Close(ProgramMatch.Name, "atdummyapp", all: true), 1);
                t.True("by name: both ended", first.WaitForExit(3000) & second.WaitForExit(3000));

                var byPath = Start(UniqueTitle("AtDummy"), false); started.Add(byPath);
                t.Eq("by path", Close(ProgramMatch.Path, DummyPath), 1);
                t.True("by path: it ended", byPath.WaitForExit(3000));

                string stubborn = UniqueTitle("AtDummy");
                var refuses = Start(stubborn, true); started.Add(refuses);
                t.Throws("a program that refuses to close", () => Close(ProgramMatch.Title, stubborn, timeoutMs: 1500), "did not close");
                t.False("it is still running after the refusal", refuses.HasExited);
                t.Eq("kill ends it", Close(ProgramMatch.Title, stubborn, kill: true), 1);
                t.True("kill: it ended", refuses.WaitForExit(3000));

                t.Eq("no match is 0, not an error", Close(ProgramMatch.Title, "^NoSuchWindow-" + Guid.NewGuid().ToString("N") + "$"), 0);
                t.Throws("a bad regular expression", () => Close(ProgramMatch.Title, "("), "regular expression");
                t.Throws("an id that is not a number", () => Close(ProgramMatch.Id, "abc"), "process id");
                t.Throws("nothing to look for", () => Close(ProgramMatch.Name, "  "), "enter what to look for");
                t.Eq("this program is never matched", ProgramActions.Find(ProgramMatch.Id, Process.GetCurrentProcess().Id.ToString()).Count, 0);
            }
            finally
            {
                foreach (var p in started)
                {
                    try { if (!p.HasExited) p.Kill(); } catch (Exception) { }
                    p.Dispose();
                }
            }
        }
    }
}
