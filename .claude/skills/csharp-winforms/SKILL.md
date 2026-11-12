---
name: csharp-winforms
description: Office role skill for the Desktop Developer. Conventions for C# work in the Automation codebase.
user-invocable: false
---

# C# / WinForms in Automation

- Targets: Core is `netstandard2.0` (C# features limited to what the SDK allows for that target; no WinForms,
  no Windows-only APIs except through `Native/NativeMethods.cs`), App is `net48` WinForms.
- Build: `dotnet build -c Release` from the repo root; output `src/AutomationTest.App/bin/Release/net48/AutomationTest.exe`.
  `build-release.bat` produces the clean `Release/` folder.
- UI thread: the runner works off the UI thread; update controls only through `Invoke`/`BeginInvoke`.
- Dispose `Bitmap`, `Graphics`, `Pen`, `Brush` and native handles (`using`).
- Keep P/Invoke declarations in `NativeMethods.cs` (Core) or the existing App native helpers (`HotKeyNative.cs`).
- Settings go through `AppSettings.cs` / `IniFile.cs` into `Setting.ini`; scripts through `ScriptSerializer.cs`.
  Keep these formats compatible with the Rust edition (new fields optional, old files still load).
- Follow the existing naming and file layout; prefer a new small class over growing `MainForm.cs`.
- After a change, update `docs/STATUS.md` (and README for user-visible behaviour), in English.
