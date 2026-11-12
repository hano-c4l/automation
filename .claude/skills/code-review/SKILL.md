---
name: code-review
description: Office role skill for the Tech Leader. How to review C# changes in the Automation codebase.
user-invocable: false
---

# Code review (Automation)

- Review the diff against the task and the design; read the surrounding code, not only the changed lines.
- Prioritize findings: **must fix** (bugs, data loss, format breaks, security), **should fix**, **nice to have**.
  Give `file:line` and a concrete suggestion for each.
- Check: thread safety between the runner thread and the UI thread (use `Invoke`/`BeginInvoke`), disposal of
  GDI objects and bitmaps, P/Invoke signatures, cancellation of long runs (stop keys), exceptions surfaced to the user.
- Compatibility: script JSON, `license.lic` and `Setting.ini` are shared with the Rust edition. Any format
  change must be backward compatible or explicitly approved.
- Keep Core free of WinForms; new logic that can be tested belongs in Core.
- Do not let `MainForm.cs` grow further without reason; suggest extraction when a change adds a lot there.
- Verify with `dotnet build -c Release` (and `dotnet test` if tests exist).
