using System;
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
            for (int i = 0; i < 80 && p.MainWindowHandle == IntPtr.Zero; i++) { Thread.Sleep(100); p.Refresh(); }
            return p;
        }

        private static int Close(ProgramMatch match, string value, bool all = false, bool kill = false, long timeoutMs = 5000)
        {
            return ProgramActions.Close(match, value, all, kill, timeoutMs, CancellationToken.None);
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
