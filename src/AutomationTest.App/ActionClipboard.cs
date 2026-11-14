using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AutomationTest.Core.Model;

namespace AutomationTest.App
{
    /// <summary>
    /// Copy, cut and paste of actions between lists, scripts and runs of the program: the actions go to the Windows clipboard as the
    /// JSON text of a script under a format of their own, so other programs never receive them as text.
    /// </summary>
    internal static class ActionClipboard
    {
        public const string Format = "AutomationTest.Actions.v1";

        /// <summary>Where the text is kept. The program uses the Windows clipboard; tests put something else here so they never touch the user's.</summary>
        internal static IActionStore Store = new WindowsStore();

        internal interface IActionStore
        {
            void Put(string json);
            string Get();
        }

        private sealed class WindowsStore : IActionStore
        {
            public void Put(string json)
            {
                var data = new DataObject();
                data.SetData(Format, json);
                Retry(() => Clipboard.SetDataObject(data, true));
            }

            public string Get()
            {
                string json = null;
                Retry(() => { json = Clipboard.ContainsData(Format) ? Clipboard.GetData(Format) as string : null; });
                return json;
            }

            private static void Retry(Action action)
            {
                for (int attempt = 0; ; attempt++)
                {
                    try { action(); return; }
                    catch (ExternalException) when (attempt < 9) { Thread.Sleep(50); } // another program holds the clipboard for a moment
                }
            }
        }

        /// <summary>Puts copies of the actions (with everything inside them) on the clipboard.</summary>
        public static void Copy(IEnumerable<ActionNode> nodes)
        {
            var script = new AutomationScript { Actions = nodes.Select(n => n.Clone()).ToList() };
            Store.Put(ScriptSerializer.ToJson(script));
        }

        /// <summary>Fresh copies of the actions on the clipboard; an empty list when there are none (or something else is there).</summary>
        public static List<ActionNode> Paste()
        {
            string json = Store.Get();
            if (string.IsNullOrWhiteSpace(json)) return new List<ActionNode>();
            try { return ScriptSerializer.FromJson(json).Actions; }
            catch (Exception ex) when (ex is System.IO.InvalidDataException || ex is System.Runtime.Serialization.SerializationException)
            { return new List<ActionNode>(); }
        }

        public static bool HasActions
        {
            get
            {
                try { return !string.IsNullOrWhiteSpace(Store.Get()); }
                catch (ExternalException) { return false; }
            }
        }
    }
}
