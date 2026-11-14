using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AutomationTest.Core.Expressions;
using AutomationTest.Core.Model;

namespace AutomationTest.Core.Excel
{
    /// <summary>The values an Excel action needs that come from expressions, worked out by the runner before the action runs.</summary>
    public sealed class ExcelArgs
    {
        /// <summary>The cell or area (the Cell field, or what its expression gave).</summary>
        public string Cell = "";
        /// <summary>SetCell: the value to write (typed when it came from an expression); Find and Evaluate: the text.</summary>
        public object Value = "";
    }

    /// <summary>
    /// The Excel the actions of a run work in: the workbook and sheet the script is using now. Workbook and Select Sheet set them (and
    /// hold the actions that work there); everything else uses them. Without a Workbook action the workbook that is active in Excel is used.
    /// </summary>
    public sealed class ExcelSession : IDisposable
    {
        /// <summary>Makes the real (COM) host; set by the program. Core itself does not know Excel.</summary>
        public static Func<IExcelHost> HostFactory;

        private static readonly object SharedLock = new object();
        private static ExcelSession _shared;

        /// <summary>The session of the program; created on first use (a later use tries again when the first one failed).</summary>
        public static ExcelSession Shared
        {
            get
            {
                lock (SharedLock)
                {
                    if (_shared == null)
                    {
                        var factory = HostFactory;
                        if (factory == null) throw new InvalidOperationException("Excel actions are not available in this program.");
                        _shared = new ExcelSession(factory());
                    }
                    return _shared;
                }
            }
        }

        /// <summary>The session when it has been created already (null before the first use); never creates it.</summary>
        public static ExcelSession SharedIfCreated
        {
            get { lock (SharedLock) return _shared; }
        }

        private readonly IExcelHost _host;
        private IExcelWorkbook _workbook;
        private IExcelSheet _sheet;

        public ExcelSession(IExcelHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public IExcelHost Host => _host;

        /// <summary>Forgets the workbook and sheet: the next action uses what is active in Excel then.</summary>
        public void Reset()
        {
            _workbook = null;
            _sheet = null;
        }

        /// <summary>Ends a run: Excel instances the program started hidden are quit, everything else is let go.</summary>
        public void EndRun()
        {
            Reset();
            try { _host.EndRun(); }
            catch (InvalidOperationException) { /* nothing more to do for a host that is already gone */ }
        }

        public void Dispose()
        {
            Reset();
            _host.Dispose();
        }

        // ---------------------------------------------------------------- actions

        /// <returns>What the action read (GetCell, GetInfo, Evaluate: a typed value; Find: a <see cref="CellValue"/>); null for the others.</returns>
        public object Execute(ExcelSettings x, ExcelArgs args, CancellationToken token)
        {
            _host.BusyTimeoutMs = (int)Math.Min(Math.Max(x.TimeMs, 1000), int.MaxValue);
            _host.Token = token;
            switch (x.Kind)
            {
                case ExcelKind.Workbook: OpenWorkbook(x, token); return null;
                case ExcelKind.SelectSheet: SelectSheet(x); return null;
                case ExcelKind.CreateSheet: CreateSheet(x); return null;
                case ExcelKind.DeleteSheet: DeleteSheet(x); return null;
                case ExcelKind.GetCell: return GetCell(x, args);
                case ExcelKind.SetCell: SetCell(x, args); return null;
                case ExcelKind.Find: return Find(x, args);
                case ExcelKind.GetInfo: return GetInfo(x, args);
                case ExcelKind.Evaluate: return Evaluate(args);
                case ExcelKind.Save: Save(x); return null;
                case ExcelKind.CloseWorkbook: CloseWorkbook(x); return null;
                case ExcelKind.CloseExcel: CloseExcel(x, token); return null;
            }
            return null;
        }

        private IExcelWorkbook CurrentWorkbook(CancellationToken token)
        {
            if (_workbook == null)
            {
                _workbook = _host.ActiveWorkbook((int)Math.Min(_host.BusyTimeoutMs, int.MaxValue), token);
                _sheet = null;
            }
            if (_workbook == null) throw new InvalidOperationException("No workbook is open in Excel. Add a Workbook action first.");
            return _workbook;
        }

        private IExcelSheet CurrentSheet()
        {
            var wb = CurrentWorkbook(_host.Token);
            if (_sheet == null) _sheet = wb.ActiveSheet;
            return _sheet;
        }

        private void OpenWorkbook(ExcelSettings x, CancellationToken token)
        {
            string path = string.IsNullOrWhiteSpace(x.Path) ? "" : Path.GetFullPath(Environment.ExpandEnvironmentVariables(x.Path.Trim()));
            _workbook = _host.OpenWorkbook(path, x.Name, x.ReadOnly, x.Visible, (int)Math.Min(Math.Max(x.TimeMs, 1000), int.MaxValue), token);
            _sheet = _workbook.ActiveSheet;
        }

        /// <summary>The sheet a name pattern and/or position points to.</summary>
        private IExcelSheet FindSheet(ExcelSettings x, bool emptyMeansCurrent)
        {
            var wb = CurrentWorkbook(_host.Token);
            if (emptyMeansCurrent && string.IsNullOrWhiteSpace(x.SheetName) && x.Index <= 0) return CurrentSheet();
            int position;
            try { position = SheetSelector.Choose(wb.SheetNames, x.SheetName, x.Index); }
            catch (ArgumentException ex) { throw new InvalidOperationException("The sheet name is not a valid regular expression: " + ex.Message); }
            if (position == 0)
                throw new InvalidOperationException("No sheet " + (string.IsNullOrWhiteSpace(x.SheetName) ? "found" : "with a name matching \"" + x.SheetName + "\"") + " in " + wb.Name + " (sheets: " + string.Join(", ", wb.SheetNames) + ").");
            return wb.Sheet(position);
        }

        private void SelectSheet(ExcelSettings x)
        {
            var sheet = FindSheet(x, false);
            sheet.Activate();
            _sheet = sheet;
        }

        private void CreateSheet(ExcelSettings x)
        {
            var wb = CurrentWorkbook(_host.Token);
            string name = (x.SheetName ?? "").Trim();
            if (name.Length > 31 || name.IndexOfAny(new[] { ':', '\\', '/', '?', '*', '[', ']' }) >= 0)
                throw new InvalidOperationException("\"" + name + "\" cannot be a sheet name: at most 31 characters, none of : \\ / ? * [ ].");
            if (name.Length > 0)
                foreach (string existing in wb.SheetNames)
                    if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The workbook " + wb.Name + " has a sheet named \"" + existing + "\" already.");
            var created = wb.AddSheet(name, x.Position, CurrentSheet(), x.SelectNew);
            if (x.SelectNew) _sheet = created;
        }

        private void DeleteSheet(ExcelSettings x)
        {
            var wb = CurrentWorkbook(_host.Token);
            var sheet = FindSheet(x, true);
            if (wb.SheetNames.Count <= 1) throw new InvalidOperationException("A workbook needs at least one sheet: \"" + sheet.Name + "\" is the only one, so it cannot be deleted.");
            wb.DeleteSheet(sheet.Index);
            _sheet = null; // the sheet in use may be the one that went
        }

        private static string CellAddress(ExcelArgs args, string what)
        {
            string address = (args.Cell ?? "").Trim();
            string problem = ExcelAddress.Check(address, what);
            if (problem != null) throw new InvalidOperationException(problem);
            return address;
        }

        private object GetCell(ExcelSettings x, ExcelArgs args)
        {
            string address = CellAddress(args, "cell");
            var sheet = CurrentSheet();
            switch (x.Get)
            {
                case ExcelGetKind.Text: return sheet.GetText(address) ?? "";
                case ExcelGetKind.Formula: return sheet.GetFormula(address) ?? "";
                case ExcelGetKind.NumberFormat: return sheet.GetNumberFormat(address) ?? "";
                case ExcelGetKind.IsEmpty: return sheet.IsEmpty(address);
                default: return ExcelValues.ForVariable(sheet.GetValue(address));
            }
        }

        private void SetCell(ExcelSettings x, ExcelArgs args)
        {
            string address = CellAddress(args, "cell");
            var sheet = CurrentSheet();
            object value = args.Value;
            switch (x.As)
            {
                case ExcelSetKind.Formula:
                    string formula = Format.Display(value).Trim();
                    sheet.SetFormula(address, formula.StartsWith("=") ? formula : "=" + formula);
                    break;
                case ExcelSetKind.Text:
                    sheet.SetText(address, Format.Display(value));
                    break;
                default:
                    if (value is double || value is bool || value is DateTime || value is string) sheet.SetValue(address, value);
                    else sheet.SetValue(address, Format.Display(value));
                    break;
            }
        }

        private object Find(ExcelSettings x, ExcelArgs args)
        {
            string what = Format.Display(args.Value);
            if (what.Length == 0) throw new InvalidOperationException("Find: there is nothing to look for.");
            string area = (args.Cell ?? "").Trim();
            if (area.Length > 0)
            {
                string problem = ExcelAddress.Check(area, "area");
                if (problem != null) throw new InvalidOperationException(problem);
            }
            var match = CurrentSheet().Find(area, what, x.MatchCase, x.WholeCell);
            return match == null ? new CellValue(false, 0, 0, "") : new CellValue(true, match.Row, match.Column, match.Address);
        }

        private object GetInfo(ExcelSettings x, ExcelArgs args)
        {
            switch (x.Info)
            {
                case ExcelInfoKind.LastRow:
                    return (double)CurrentSheet().LastRow(CellAddress(args, "cell in the column"));
                case ExcelInfoKind.LastColumn:
                    return (double)CurrentSheet().LastColumn(CellAddress(args, "cell in the row"));
                case ExcelInfoKind.UsedRange: return CurrentSheet().UsedRange() ?? "";
                case ExcelInfoKind.SheetCount: return (double)CurrentWorkbook(_host.Token).SheetNames.Count;
                case ExcelInfoKind.SheetName: return CurrentSheet().Name;
                case ExcelInfoKind.WorkbookName: return CurrentWorkbook(_host.Token).Name;
                case ExcelInfoKind.WorkbookPath: return CurrentWorkbook(_host.Token).FullPath;
                default:
                    return _host.TryGetSelection(out string sheetName, out string address) ? address : "";
            }
        }

        private object Evaluate(ExcelArgs args)
        {
            string formula = Format.Display(args.Value).Trim();
            if (formula.Length == 0) throw new InvalidOperationException("Evaluate: there is no formula.");
            if (formula.StartsWith("=")) formula = formula.Substring(1);
            return ExcelValues.ForVariable(CurrentSheet().Evaluate(formula));
        }

        private static readonly HashSet<string> SaveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".xlsx", ".xlsm", ".xlsb", ".xls", ".csv", ".pdf"
        };

        private void Save(ExcelSettings x)
        {
            var wb = CurrentWorkbook(_host.Token);
            if (!x.SaveAs)
            {
                if (!wb.HasFile) throw new InvalidOperationException("The workbook " + wb.Name + " has no file yet: turn Save As on and give a path.");
                wb.Save();
                return;
            }
            if (string.IsNullOrWhiteSpace(x.SavePath)) throw new InvalidOperationException("Save As: enter the path of the new file.");
            string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(x.SavePath.Trim()));
            string extension = Path.GetExtension(path);
            if (!SaveExtensions.Contains(extension))
                throw new InvalidOperationException("Save As: the file name must end with .xlsx, .xlsm, .xlsb, .xls, .csv or .pdf (not \"" + extension + "\").");
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) throw new InvalidOperationException("Save As: the folder " + folder + " does not exist.");
            wb.SaveAs(path);
        }

        private void CloseWorkbook(ExcelSettings x)
        {
            var wb = CurrentWorkbook(_host.Token);
            wb.Close(x.SaveChanges);
            Reset();
        }

        private void CloseExcel(ExcelSettings x, CancellationToken token)
        {
            _host.CloseExcel(_workbook, x.SaveChanges, (int)Math.Min(Math.Max(x.TimeMs, 1000), int.MaxValue), token);
            Reset();
        }
    }
}
