using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using AutomationTest.Core.Excel;
using AutomationTest.Core.Model;

namespace AutomationTest.App
{
    /// <summary>
    /// Drives Microsoft Excel through COM (late bound: no Office assemblies are needed, only Excel installed). The Excel the user has
    /// open is used first; Excel is started only when none runs, and an Excel started hidden is quit when the run ends. In isolated mode
    /// (tests) only Excel instances started by this host are seen, so the user's own Excel is never touched.
    /// </summary>
    public sealed class ExcelComHost : IExcelHost
    {
        // Excel is busy (a cell is being edited, a dialog is open): the call is tried again until BusyTimeoutMs.
        private static readonly HashSet<uint> BusyCodes = new HashSet<uint> { 0x80010001, 0x8001010A, 0x800AC472 };
        private const uint ServerGone = 0x800706BA;   // RPC server unavailable: Excel was closed
        private const uint Disconnected = 0x80010108; // the object has gone

        private sealed class Instance
        {
            public dynamic App;
            public int Hwnd;
            public bool Owned;    // started by this host
            public bool Hidden;
        }

        private readonly bool _isolated;
        private readonly List<Instance> _owned = new List<Instance>();

        public ExcelComHost(bool isolated = false)
        {
            _isolated = isolated;
        }

        public int BusyTimeoutMs { get; set; } = 30000;
        public CancellationToken Token { get; set; }

        // ---------------------------------------------------------------- busy handling

        /// <summary>Runs a COM call; while Excel is busy it is tried again, then it fails with a message that says what to do.</summary>
        internal T Com<T>(Func<T> call)
        {
            var until = DateTime.UtcNow.AddMilliseconds(Math.Max(1000, BusyTimeoutMs));
            while (true)
            {
                Token.ThrowIfCancellationRequested();
                try { return call(); }
                catch (COMException ex)
                {
                    uint code = unchecked((uint)ex.HResult);
                    if (BusyCodes.Contains(code))
                    {
                        if (DateTime.UtcNow >= until)
                            throw new InvalidOperationException("Excel is busy (is a cell being edited, or a dialog open in Excel?). Close it and run again.");
                        Thread.Sleep(150);
                        continue;
                    }
                    if (code == ServerGone || code == Disconnected)
                        throw new InvalidOperationException("Excel was closed while the script was using it.");
                    throw new InvalidOperationException("Excel: " + CleanMessage(ex.Message) + " (0x" + code.ToString("X8") + ")");
                }
                catch (InvalidComObjectException)
                {
                    throw new InvalidOperationException("The workbook or sheet is no longer there (Excel closed it). Use a Workbook action again.");
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex)
                {
                    throw new InvalidOperationException("Excel did not understand the call: " + CleanMessage(ex.Message));
                }
            }
        }

        internal void Com(Action call)
        {
            Com<object>(() => { call(); return null; });
        }

        private static string CleanMessage(string text)
        {
            return (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        // ---------------------------------------------------------------- finding Excel

        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.IDispatch)] out object result);

        private static string ClassOf(IntPtr hwnd)
        {
            var sb = new StringBuilder(64);
            GetClassName(hwnd, sb, 64);
            return sb.ToString();
        }

        /// <summary>The Excel applications that run: the registered one, and every one that has a workbook window.</summary>
        private List<Instance> Instances()
        {
            var list = new List<Instance>();
            foreach (var own in _owned)
            {
                try { own.Hwnd = (int)own.App.Hwnd; list.Add(own); } catch (COMException) { } catch (InvalidComObjectException) { }
            }
            if (_isolated) return list;

            var seen = new HashSet<int>();
            foreach (var own in list) seen.Add(own.Hwnd);
            try
            {
                dynamic registered = Marshal.GetActiveObject("Excel.Application");
                int hwnd = (int)registered.Hwnd;
                if (seen.Add(hwnd)) list.Add(new Instance { App = registered, Hwnd = hwnd });
            }
            catch (COMException) { /* none registered */ }
            catch (InvalidComObjectException) { }

            Guid iid = new Guid("00020400-0000-0000-C000-000000000046"); // IDispatch
            EnumWindows((top, l) =>
            {
                if (ClassOf(top) != "XLMAIN") return true;
                EnumChildWindows(top, (child, l2) =>
                {
                    if (ClassOf(child) != "EXCEL7") return true;
                    try
                    {
                        if (AccessibleObjectFromWindow(child, 0xFFFFFFF0, ref iid, out object window) == 0 && window != null)
                        {
                            dynamic app = ((dynamic)window).Application;
                            int hwnd = (int)app.Hwnd;
                            if (seen.Add(hwnd)) list.Add(new Instance { App = app, Hwnd = hwnd });
                        }
                    }
                    catch (COMException) { }
                    catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                    return true;
                }, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        private Instance StartExcel(bool visible)
        {
            Type type = Type.GetTypeFromProgID("Excel.Application");
            if (type == null) throw new InvalidOperationException("Microsoft Excel is not installed on this computer.");
            dynamic app = Com(() => Activator.CreateInstance(type));
            var instance = new Instance { App = app, Owned = true, Hidden = !visible };
            Com(() =>
            {
                app.Visible = visible;
                if (visible) app.UserControl = true; // a visible Excel is the user's: it stays open when the program lets go of it
                app.DisplayAlerts = true;
            });
            instance.Hwnd = Com(() => (int)app.Hwnd);
            _owned.Add(instance);
            return instance;
        }

        private Instance FirstOrStart(bool visible)
        {
            var existing = Instances();
            return existing.Count > 0 ? existing[0] : StartExcel(visible);
        }

        // ---------------------------------------------------------------- IExcelHost

        public IExcelWorkbook OpenWorkbook(string path, string namePattern, bool readOnly, bool visible, bool allowMacros, int timeoutMs, CancellationToken token)
        {
            Token = token;
            if (path.Length == 0 && !string.IsNullOrWhiteSpace(namePattern))
                return SwitchToByName(namePattern);

            if (path.Length > 0)
            {
                var open = FindOpenByPath(path);
                if (open != null) { Com(() => open.Workbook.Activate()); return open; }
            }

            // What can be checked without Excel is checked before Excel is started, so a mistake never leaves a new Excel behind.
            if (path.Length > 0 && !File.Exists(path)) throw new InvalidOperationException("The file " + path + " does not exist.");

            var instance = FirstOrStart(visible);
            if (path.Length == 0)
            {
                dynamic fresh = Com(() => instance.App.Workbooks.Add());
                return Wrap(instance, fresh);
            }

            string fileName = Path.GetFileName(path);
            foreach (var clash in WorkbooksOf(instance))
                if (string.Equals(clash.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A workbook named \"" + clash.Name + "\" is open already from " + clash.FullPath +
                        ". Excel cannot open two workbooks with the same name: close it first (Close Workbook).");

            dynamic wb = Com(() =>
            {
                dynamic app = instance.App;
                var security = app.AutomationSecurity;
                var alerts = app.DisplayAlerts;
                try
                {
                    // 3 = msoAutomationSecurityForceDisable: macros of a file opened by a script do not run;
                    // 2 = msoAutomationSecurityByUI: the Trust Center of the user decides (a Workbook with Allow macros ON)
                    app.AutomationSecurity = allowMacros ? 2 : 3;
                    app.DisplayAlerts = false;
                    // UpdateLinks 0: no question about external links.
                    return app.Workbooks.Open(path, 0, readOnly);
                }
                finally
                {
                    app.DisplayAlerts = alerts;
                    app.AutomationSecurity = security;
                }
            });
            if (wb == null) throw new InvalidOperationException("Excel did not open " + path + " (is it open under another name, or damaged?).");
            Com(() => wb.Activate());
            return Wrap(instance, wb);
        }

        private ComWorkbook Wrap(Instance instance, dynamic workbook)
        {
            return new ComWorkbook(this, instance.App, workbook);
        }

        private List<ComWorkbook> WorkbooksOf(Instance instance)
        {
            var list = new List<ComWorkbook>();
            dynamic books = Com(() => instance.App.Workbooks);
            int count = Com(() => (int)books.Count);
            for (int i = 1; i <= count; i++)
            {
                int index = i;
                dynamic wb = Com(() => books[index]);
                list.Add(Wrap(instance, wb));
            }
            return list;
        }

        private ComWorkbook FindOpenByPath(string path)
        {
            foreach (var instance in Instances())
                foreach (var wb in WorkbooksOf(instance))
                    if (string.Equals(wb.FullPath, path, StringComparison.OrdinalIgnoreCase)) return wb;
            return null;
        }

        private IExcelWorkbook SwitchToByName(string pattern)
        {
            Regex regex;
            try { regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(1000)); }
            catch (ArgumentException ex) { throw new InvalidOperationException("The workbook name is not a valid regular expression: " + ex.Message); }
            var names = new List<string>();
            foreach (var instance in Instances())
                foreach (var wb in WorkbooksOf(instance))
                {
                    names.Add(wb.Name);
                    if (regex.IsMatch(wb.Name)) { Com(() => wb.Workbook.Activate()); return wb; }
                }
            throw new InvalidOperationException("No open workbook has a name matching \"" + pattern + "\"" + (names.Count == 0 ? " (Excel has no workbook open)." : " (open: " + string.Join(", ", names) + ")."));
        }

        public IExcelWorkbook ActiveWorkbook(int timeoutMs, CancellationToken token)
        {
            Token = token;
            foreach (var instance in Instances())
            {
                dynamic wb = Com(() => instance.App.ActiveWorkbook);
                if (wb != null) return Wrap(instance, wb);
            }
            return null;
        }

        public bool CloseExcel(IExcelWorkbook workbook, bool saveChanges, int timeoutMs, CancellationToken token)
        {
            Token = token;
            dynamic app = null;
            if (workbook is ComWorkbook own) app = own.App;
            else
            {
                var list = Instances();
                if (list.Count > 0) app = list[0].App;
            }
            if (app == null) return false;
            Com(() =>
            {
                app.DisplayAlerts = false;
                if (saveChanges)
                {
                    foreach (dynamic wb in app.Workbooks)
                        if (!string.IsNullOrEmpty((string)wb.Path) && !(bool)wb.ReadOnly) wb.Save(); // a workbook that has no file yet cannot be saved without a dialog
                }
                app.Quit();
            });
            _owned.RemoveAll(i => { try { return (int)i.App.Hwnd == 0 || ReferenceEquals(i.App, app); } catch { return true; } });
            Release();
            return true;
        }

        public bool TryGetSelection(out string sheetName, out string address)
        {
            sheetName = ""; address = "";
            foreach (var instance in Instances())
            {
                try
                {
                    dynamic app = instance.App;
                    if (app.ActiveWorkbook == null) continue;
                    sheetName = (string)app.ActiveSheet.Name;
                    address = (string)app.Selection.Address(false, false);
                    return true;
                }
                catch (COMException) { }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { } // a picture or chart is selected
            }
            return false;
        }

        /// <summary>Brings the Excel that has the active workbook to the front (for picking a cell there).</summary>
        public bool BringToFront()
        {
            foreach (var instance in Instances())
            {
                try
                {
                    if (instance.App.ActiveWorkbook == null) continue;
                    IntPtr hwnd = new IntPtr((int)instance.App.Hwnd);
                    ShowWindow(hwnd, 9); // SW_RESTORE
                    SetForegroundWindow(hwnd);
                    return true;
                }
                catch (COMException) { }
            }
            return false;
        }

        public void EndRun()
        {
            foreach (var instance in _owned.ToArray())
            {
                if (!instance.Hidden) continue; // a visible Excel stays open for the user
                try
                {
                    instance.App.DisplayAlerts = false;
                    instance.App.Quit();
                }
                catch (COMException) { }
                catch (InvalidComObjectException) { }
                _owned.Remove(instance);
            }
            Release();
        }

        /// <summary>Lets go of the COM objects so Excel can exit (the ones that were used are cleaned up by the garbage collector).</summary>
        private static void Release()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public void Dispose()
        {
            EndRun();
            foreach (var instance in _owned.ToArray())
            {
                try { Marshal.ReleaseComObject((object)instance.App); } catch { }
            }
            _owned.Clear();
            Release();
        }

        // ---------------------------------------------------------------- workbook and sheet

        private sealed class ComWorkbook : IExcelWorkbook
        {
            private readonly ExcelComHost _host;
            public readonly dynamic App;
            public readonly dynamic Workbook;

            public ComWorkbook(ExcelComHost host, dynamic app, dynamic workbook)
            {
                _host = host;
                App = app;
                Workbook = workbook;
            }

            public string Name => _host.Com(() => (string)Workbook.Name);
            public string FullPath => _host.Com(() => (string)Workbook.FullName);
            public bool HasFile => _host.Com(() => !string.IsNullOrEmpty((string)Workbook.Path));

            public IReadOnlyList<string> SheetNames
            {
                get
                {
                    var names = new List<string>();
                    dynamic sheets = _host.Com(() => Workbook.Worksheets);
                    int count = _host.Com(() => (int)sheets.Count);
                    for (int i = 1; i <= count; i++)
                    {
                        int index = i;
                        names.Add(_host.Com(() => (string)sheets[index].Name));
                    }
                    return names;
                }
            }

            public IExcelSheet ActiveSheet
            {
                get
                {
                    dynamic sheet = _host.Com(() => Workbook.ActiveSheet);
                    if (sheet == null) sheet = _host.Com(() => Workbook.Worksheets[1]);
                    return new ComSheet(_host, sheet);
                }
            }

            public IExcelSheet Sheet(int index)
            {
                return new ComSheet(_host, _host.Com(() => Workbook.Worksheets[index]));
            }

            public IExcelSheet AddSheet(string name, ExcelSheetPosition position, IExcelSheet current, bool select)
            {
                dynamic previous = _host.Com(() => Workbook.ActiveSheet);
                dynamic added = _host.Com(() =>
                {
                    dynamic sheets = Workbook.Worksheets;
                    switch (position)
                    {
                        case ExcelSheetPosition.Start: return sheets.Add(sheets[1]);
                        case ExcelSheetPosition.BeforeCurrent: return sheets.Add(((ComSheet)current).Sheet);
                        case ExcelSheetPosition.AfterCurrent: return sheets.Add(Type.Missing, ((ComSheet)current).Sheet);
                        default: return sheets.Add(Type.Missing, sheets[sheets.Count]);
                    }
                });
                if (!string.IsNullOrEmpty(name)) _host.Com(() => added.Name = name);
                if (!select && previous != null) _host.Com(() => previous.Activate()); // Excel selects the new sheet; go back
                return new ComSheet(_host, added);
            }

            public void DeleteSheet(int index)
            {
                _host.Com(() =>
                {
                    var alerts = App.DisplayAlerts;
                    try
                    {
                        App.DisplayAlerts = false; // no "delete permanently?" question
                        Workbook.Worksheets[index].Delete();
                    }
                    finally { App.DisplayAlerts = alerts; }
                });
            }

            public void Activate()
            {
                _host.Com(() => Workbook.Activate());
            }

            public void RefreshAll()
            {
                _host.Com(() =>
                {
                    Workbook.RefreshAll();
                    App.CalculateUntilAsyncQueriesDone(); // background queries finish before the next action
                });
            }

            public object RunMacro(string macro, object argument, bool hasArgument)
            {
                return _host.Com(() => hasArgument ? (object)App.Run(macro, argument) : (object)App.Run(macro));
            }

            public void Save()
            {
                _host.Com(() => Workbook.Save());
            }

            public void SaveAs(string path)
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                _host.Com(() =>
                {
                    var alerts = App.DisplayAlerts;
                    try
                    {
                        App.DisplayAlerts = false; // overwrite without asking
                        switch (extension)
                        {
                            case ".pdf": Workbook.ExportAsFixedFormat(0, path); break; // xlTypePDF
                            case ".csv": Workbook.SaveAs(path, 62); break;             // xlCSVUTF8: Vietnamese text stays right
                            case ".xlsm": Workbook.SaveAs(path, 52); break;
                            case ".xlsb": Workbook.SaveAs(path, 50); break;
                            case ".xls": Workbook.SaveAs(path, 56); break;
                            default: Workbook.SaveAs(path, 51); break;                 // xlOpenXMLWorkbook
                        }
                    }
                    finally { App.DisplayAlerts = alerts; }
                });
            }

            public void Close(bool save)
            {
                if (save && !HasFile) throw new InvalidOperationException("The workbook " + Name + " has no file yet, so it cannot be saved by closing it: use Save As first.");
                _host.Com(() =>
                {
                    var alerts = App.DisplayAlerts;
                    try
                    {
                        App.DisplayAlerts = false;
                        Workbook.Close(save);
                    }
                    finally { try { App.DisplayAlerts = alerts; } catch (COMException) { } }
                });
            }
        }

        private sealed class ComSheet : IExcelSheet
        {
            private readonly ExcelComHost _host;
            public readonly dynamic Sheet;

            public ComSheet(ExcelComHost host, dynamic sheet)
            {
                _host = host;
                Sheet = sheet;
            }

            public string Name => _host.Com(() => (string)Sheet.Name);
            public int Index => _host.Com(() => (int)Sheet.Index);

            public void Activate()
            {
                _host.Com(() => Sheet.Activate());
            }

            public void Rename(string newName)
            {
                _host.Com(() => Sheet.Name = newName);
            }

            public void Select(string area)
            {
                _host.Com(() =>
                {
                    Sheet.Activate();
                    Sheet.Range[area].Select();
                });
            }

            private static string Lines(ExcelTarget target, string at, int count)
            {
                if (target == ExcelTarget.Rows)
                {
                    int row = ExcelAddress.RowOf(at);
                    return row + ":" + (row + count - 1);
                }
                int column = ExcelAddress.ColumnNumber(ExcelAddress.ColumnOf(at));
                return ExcelAddress.ColumnName(column) + ":" + ExcelAddress.ColumnName(column + count - 1);
            }

            public void InsertCells(ExcelTarget target, string at, int count)
            {
                _host.Com(() => Sheet.Range[Lines(target, at, count)].Insert());
            }

            public void DeleteCells(ExcelTarget target, string at, int count)
            {
                _host.Com(() => Sheet.Range[Lines(target, at, count)].Delete());
            }

            public void Clear(string area, ExcelClearKind what)
            {
                _host.Com(() =>
                {
                    dynamic range = Sheet.Range[area];
                    switch (what)
                    {
                        case ExcelClearKind.Formats: range.ClearFormats(); break;
                        case ExcelClearKind.All: range.Clear(); break;
                        default: range.ClearContents(); break;
                    }
                });
            }

            public void Copy(string source, string destination, bool valuesOnly)
            {
                string first = destination.Split(':')[0]; // the paste starts at the first cell
                _host.Com(() =>
                {
                    dynamic from = Sheet.Range[source];
                    dynamic to = Sheet.Range[first];
                    if (valuesOnly)
                    {
                        from.Copy();
                        to.PasteSpecial(-4163); // xlPasteValues
                        Sheet.Application.CutCopyMode = false;
                    }
                    else from.Copy(to);
                });
            }

            public void FormatCells(string area, ExcelFormat format)
            {
                _host.Com(() =>
                {
                    dynamic range = Sheet.Range[area];
                    if (!string.IsNullOrEmpty(format.NumberFormat)) range.NumberFormat = format.NumberFormat;
                    if (format.Bold.HasValue) range.Font.Bold = format.Bold.Value;
                    if (format.Italic.HasValue) range.Font.Italic = format.Italic.Value;
                    if (format.FontColor.HasValue) range.Font.Color = format.FontColor.Value;
                    if (format.FillColor.HasValue) range.Interior.Color = format.FillColor.Value;
                    if (!string.IsNullOrEmpty(format.ColumnWidth))
                    {
                        if (string.Equals(format.ColumnWidth, "auto", StringComparison.OrdinalIgnoreCase)) range.Columns.AutoFit();
                        else range.ColumnWidth = double.Parse(format.ColumnWidth, System.Globalization.CultureInfo.InvariantCulture);
                    }
                });
            }

            public void Sort(string area, string key, bool descending, bool hasHeader)
            {
                _host.Com(() =>
                {
                    dynamic range = Sheet.Range[area];
                    int index = ExcelAddress.ColumnNumber(key) - (int)range.Column + 1;
                    if (index < 1 || index > (int)range.Columns.Count)
                        throw new InvalidOperationException("Sort by column " + key + " is not inside the area " + area + ".");
                    dynamic keyRange = range.Columns[index];
                    // Key1, Order1 (1 ascending, 2 descending), Key2, Type, Order2, Key3, Order3, Header (1 yes, 2 no)
                    range.Sort(keyRange, descending ? 2 : 1, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, hasHeader ? 1 : 2);
                });
            }

            public void Filter(string area, int field, string criteria, bool clear)
            {
                _host.Com(() =>
                {
                    if (clear) Sheet.AutoFilterMode = false; // the filter of the sheet is removed and every row shows again
                    else Sheet.Range[area].AutoFilter(field, criteria);
                });
            }

            private dynamic Range(string address)
            {
                return _host.Com(() => Sheet.Range[address]);
            }

            private dynamic SingleCell(string address)
            {
                dynamic range = Range(address);
                long count = _host.Com(() => (long)range.CountLarge);
                if (count != 1) throw new InvalidOperationException(address + " is " + count + " cells: use one cell here.");
                return range;
            }

            public object GetValue(string address)
            {
                dynamic cell = SingleCell(address);
                return _host.Com(() => (object)cell.Value);
            }

            public string GetText(string address)
            {
                dynamic cell = SingleCell(address);
                string text = _host.Com(() => (string)cell.Text) ?? "";
                // In a column that is too narrow Excel shows ####; the text is made with the number format instead.
                if (text.Length > 0 && text.Trim('#').Length == 0)
                {
                    object value = _host.Com(() => (object)cell.Value);
                    if (!(value is int)) // an error cell such as #N/A is a real text
                    {
                        string format = _host.Com(() => (string)cell.NumberFormat) ?? "General";
                        object made = _host.Com(() => (object)Sheet.Evaluate("TEXT(" + address + ",\"" + format.Replace("\"", "\"\"") + "\")"));
                        if (made is string s && s.Trim('#').Length > 0) return s;
                    }
                }
                return text;
            }

            public string GetFormula(string address)
            {
                dynamic cell = SingleCell(address);
                return Convert.ToString(_host.Com(() => (object)cell.Formula), System.Globalization.CultureInfo.InvariantCulture);
            }

            public string GetNumberFormat(string address)
            {
                dynamic cell = SingleCell(address);
                return _host.Com(() => (object)cell.NumberFormat) as string ?? "";
            }

            public bool IsEmpty(string address)
            {
                dynamic cell = SingleCell(address);
                return _host.Com(() => (object)cell.Value) == null;
            }

            public void SetValue(string address, object value)
            {
                dynamic range = Range(address);
                _host.Com(() => range.Value = value);
            }

            public void SetText(string address, string text)
            {
                dynamic range = Range(address);
                _host.Com(() =>
                {
                    range.NumberFormat = "@";
                    range.Value = text;
                });
            }

            public void SetFormula(string address, string formula)
            {
                dynamic range = Range(address);
                _host.Com(() => range.Formula = formula); // English function names and commas, as in the formula bar of any Excel
            }

            public object[,] GetRange(string area, ExcelGetKind kind)
            {
                if (kind != ExcelGetKind.Value && kind != ExcelGetKind.Text && kind != ExcelGetKind.Formula)
                    throw new InvalidOperationException("Get Range reads the Value, the Text or the Formula of the cells (not " + kind + ").");
                return _host.Com(() =>
                {
                    dynamic range = Sheet.Range[area];
                    int rows = (int)range.Rows.Count, columns = (int)range.Columns.Count;
                    var result = new object[rows, columns];
                    if (kind == ExcelGetKind.Text)
                    {
                        // Excel gives the text of one cell at a time only.
                        if ((long)rows * columns > 100000)
                            throw new InvalidOperationException("Get Range with Text reads cell by cell: at most 100000 cells (" + area + " is " + ((long)rows * columns) + "). Use Value or Formula for a large area.");
                        for (int r = 1; r <= rows; r++)
                            for (int c = 1; c <= columns; c++)
                                result[r - 1, c - 1] = (string)range.Cells[r, c].Text;
                        return result;
                    }
                    object data = kind == ExcelGetKind.Formula ? (object)range.Formula : (object)range.Value; // one call for the whole area
                    if (data is Array array && array.Rank == 2)
                    {
                        int r0 = array.GetLowerBound(0), c0 = array.GetLowerBound(1);
                        for (int r = 0; r < rows; r++)
                            for (int c = 0; c < columns; c++)
                                result[r, c] = array.GetValue(r0 + r, c0 + c);
                    }
                    else result[0, 0] = data; // an area of one cell comes as the value itself
                    return result;
                });
            }

            public void SetRange(string topLeft, object[,] values, ExcelSetKind how)
            {
                int rows = values.GetLength(0), columns = values.GetLength(1);
                _host.Com(() =>
                {
                    dynamic start = Sheet.Range[topLeft];
                    dynamic target = start.Resize[rows, columns];
                    if (how == ExcelSetKind.Text) target.NumberFormat = "@"; // before the values go in, so 007 stays 007
                    if (how == ExcelSetKind.Formula) target.Formula = values;
                    else target.Value = values;                                  // one call for the whole block
                });
            }

            public CellMatch Find(string area, string what, bool matchCase, bool wholeCell)
            {
                return _host.Com(() =>
                {
                    dynamic range = string.IsNullOrEmpty(area) ? Sheet.Cells : Sheet.Range[area];
                    // Searching "after" the last cell starts at the first one, so the match is the first in reading order.
                    dynamic last = range.Cells[range.Rows.Count, range.Columns.Count];
                    // xlValues = -4163, xlWhole = 1, xlPart = 2, xlByRows = 1, xlNext = 1
                    dynamic found = range.Find(what, last, -4163, wholeCell ? 1 : 2, 1, 1, matchCase);
                    if (found == null) return null;
                    return new CellMatch((int)found.Row, (int)found.Column, (string)found.Address(false, false));
                });
            }

            public int LastRow(string column)
            {
                string letters = ExcelAddress.ColumnOf(column);
                if (letters == null) throw new InvalidOperationException("\"" + column + "\" has no column letters (use a cell such as B2, or B:B).");
                int col = ExcelAddress.ColumnNumber(letters);
                return _host.Com(() =>
                {
                    dynamic bottom = Sheet.Cells[Sheet.Rows.Count, col];
                    int row = (int)bottom.End[-4162].Row; // xlUp
                    if (row == 1 && Sheet.Cells[1, col].Value == null) return 0;
                    return row;
                });
            }

            public int LastColumn(string rowReference)
            {
                int row = ExcelAddress.RowOf(rowReference);
                if (row <= 0) throw new InvalidOperationException("\"" + rowReference + "\" has no row number (use a cell such as B2, or 2:2).");
                return _host.Com(() =>
                {
                    dynamic right = Sheet.Cells[row, Sheet.Columns.Count];
                    int col = (int)right.End[-4159].Column; // xlToLeft
                    if (col == 1 && Sheet.Cells[row, 1].Value == null) return 0;
                    return col;
                });
            }

            public string UsedRange()
            {
                return _host.Com(() =>
                {
                    dynamic used = Sheet.UsedRange;
                    if ((int)used.Rows.Count == 1 && (int)used.Columns.Count == 1 && used.Value == null) return "";
                    return (string)used.Address(false, false);
                });
            }

            public object Evaluate(string formula)
            {
                return _host.Com(() => (object)Sheet.Evaluate(formula));
            }
        }
    }
}
