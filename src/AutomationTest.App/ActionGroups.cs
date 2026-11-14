using System;
using System.Collections.Generic;
using System.Linq;
using AutomationTest.Core.Model;

namespace AutomationTest.App
{
    /// <summary>
    /// How the Excel and Web actions are grouped in the editor: a Group (the thing the action works on: Workbook, Sheet, Range...)
    /// and a Type inside it (Select, Create...). This only changes what the editor shows; the script file still stores the kind
    /// (<see cref="ExcelKind"/> / <see cref="WebKind"/>) as before, so files and other builds are not affected.
    /// </summary>
    internal static class ActionGroups
    {
        private struct Entry
        {
            public string Group;
            public string Label;
        }

        // The order of the lists is the order in the combo boxes. Every kind appears exactly once (checked by a test).
        private static readonly Dictionary<ExcelKind, Entry> ExcelMap = Build(new[]
        {
            Tuple.Create(ExcelKind.Workbook, "Workbook", "Open"),
            Tuple.Create(ExcelKind.Save, "Workbook", "Save"),
            Tuple.Create(ExcelKind.CloseWorkbook, "Workbook", "Close"),
            Tuple.Create(ExcelKind.SelectSheet, "Sheet", "Select"),
            Tuple.Create(ExcelKind.CreateSheet, "Sheet", "Create"),
            Tuple.Create(ExcelKind.RenameSheet, "Sheet", "Rename"),
            Tuple.Create(ExcelKind.DeleteSheet, "Sheet", "Delete"),
            Tuple.Create(ExcelKind.GetCell, "Cell", "Get"),
            Tuple.Create(ExcelKind.SetCell, "Cell", "Set"),
            Tuple.Create(ExcelKind.GetRange, "Range", "Get"),
            Tuple.Create(ExcelKind.SetRange, "Range", "Set"),
            Tuple.Create(ExcelKind.Select, "Range", "Select"),
            Tuple.Create(ExcelKind.Find, "Range", "Find"),
            Tuple.Create(ExcelKind.Copy, "Range", "Copy"),
            Tuple.Create(ExcelKind.Clear, "Range", "Clear"),
            Tuple.Create(ExcelKind.Format, "Range", "Format"),
            Tuple.Create(ExcelKind.Sort, "Range", "Sort"),
            Tuple.Create(ExcelKind.Filter, "Range", "Filter"),
            Tuple.Create(ExcelKind.InsertCells, "Row/Col", "Insert"),
            Tuple.Create(ExcelKind.DeleteCells, "Row/Col", "Delete"),
            Tuple.Create(ExcelKind.GetInfo, "Excel", "Info"),
            Tuple.Create(ExcelKind.Evaluate, "Excel", "Evaluate"),
            Tuple.Create(ExcelKind.Refresh, "Excel", "Refresh"),
            Tuple.Create(ExcelKind.RunMacro, "Excel", "Macro"),
            Tuple.Create(ExcelKind.CloseExcel, "Excel", "Close"),
        });

        private static readonly Dictionary<WebKind, Entry> WebMap = Build(new[]
        {
            Tuple.Create(WebKind.NewTab, "Tab", "New"),
            Tuple.Create(WebKind.SwitchTab, "Tab", "Switch"),
            Tuple.Create(WebKind.CloseTab, "Tab", "Close"),
            Tuple.Create(WebKind.CloseBrowser, "Browser", "Close"),
            Tuple.Create(WebKind.WaitElement, "Page", "Wait"),
            Tuple.Create(WebKind.Script, "Page", "Script"),
            Tuple.Create(WebKind.Dialog, "Page", "Dialog"),
            Tuple.Create(WebKind.Screenshot, "Page", "Screenshot"),
            Tuple.Create(WebKind.Click, "Element", "Click"),
            Tuple.Create(WebKind.Hover, "Element", "Hover"),
            Tuple.Create(WebKind.Drag, "Element", "Drag"),
            Tuple.Create(WebKind.Input, "Element", "Input"),
            Tuple.Create(WebKind.SelectOption, "Element", "Option"),
            Tuple.Create(WebKind.PressKey, "Element", "Key"),
            Tuple.Create(WebKind.Upload, "Element", "Upload"),
            Tuple.Create(WebKind.GetText, "Element", "Get"),
        });

        private static Dictionary<T, Entry> Build<T>(Tuple<T, string, string>[] rows)
        {
            var map = new Dictionary<T, Entry>();
            foreach (var r in rows) map.Add(r.Item1, new Entry { Group = r.Item2, Label = r.Item3 });
            return map;
        }

        // The groups appear in the order of the first kind that uses them.
        public static IList<string> ExcelGroups { get; } = ExcelMap.Values.Select(e => e.Group).Distinct().ToList();
        public static IList<string> WebGroups { get; } = WebMap.Values.Select(e => e.Group).Distinct().ToList();

        public static string GroupOf(ExcelKind kind) { return ExcelMap[kind].Group; }
        public static string GroupOf(WebKind kind) { return WebMap[kind].Group; }
        public static string LabelOf(ExcelKind kind) { return ExcelMap[kind].Label; }
        public static string LabelOf(WebKind kind) { return WebMap[kind].Label; }

        /// <summary>How the list of actions shows the kind, for example "Sheet Select".</summary>
        public static string Display(ExcelKind kind) { return ExcelMap[kind].Group + " " + ExcelMap[kind].Label; }
        public static string Display(WebKind kind) { return WebMap[kind].Group + " " + WebMap[kind].Label; }

        public static IList<ExcelKind> KindsOf(string group) { return ExcelMap.Where(p => p.Value.Group == group).Select(p => p.Key).ToList(); }
        public static IList<WebKind> WebKindsOf(string group) { return WebMap.Where(p => p.Value.Group == group).Select(p => p.Key).ToList(); }

        /// <summary>True when every kind of both enums has a group (a new kind that is not added here would be missing from the editor).</summary>
        public static bool Covers()
        {
            return Enum.GetValues(typeof(ExcelKind)).Cast<ExcelKind>().All(ExcelMap.ContainsKey)
                && Enum.GetValues(typeof(WebKind)).Cast<WebKind>().All(WebMap.ContainsKey);
        }
    }
}
