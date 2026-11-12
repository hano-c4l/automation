# Automation Test - Office Overview

Written by the Code Analyst (task T0). Facts come from the source, project files, README and `docs/`. Anything marked
"(guess)" was not verified in code. Existing docs (`README.md`, `docs/PROJECT.md`, `docs/STATUS.md`) are in Vietnamese;
this file is the English entry point and does not replace them.

## 1. Purpose

A Windows automation / autoclick tool for end users (README.md, docs/PROJECT.md section 1). A user builds a script as a
tree of actions (mouse, keyboard, loops, waits for windows, image detection, variables, conditions, expressions), then
runs it with a hotkey, or records real input and turns it into actions. Scripts are saved as JSON. Access is gated by a
per-machine license (signed file, optional expiry).

Target: Windows 10 1903+ and Windows 11 only. Different DPI or resolution between machines is explicitly not handled
(docs/PROJECT.md section 2). The product name is "Automation Test" (namespaces and exe use `AutomationTest`).

## 2. Tech stack

| Part | Technology | Source |
|---|---|---|
| Core library | C#, `netstandard2.0`, no NuGet packages | `src/AutomationTest.Core/AutomationTest.Core.csproj` |
| App | C#, `net48` WinForms, `WinExe`, assembly name `AutomationTest`, Per-Monitor V2 DPI, `asInvoker` | `src/AutomationTest.App/AutomationTest.App.csproj`, `app.manifest` |
| Tools | `net48` console apps (KeyGen, IconGen) | `tools/` |
| Script JSON | `DataContractJsonSerializer` (built-in) | `src/AutomationTest.Core/Model/ScriptSerializer.cs` |
| Native access | P/Invoke only: `SendInput`, low-level hooks, `RegisterHotKey`, GDI screen capture, `SetThreadExecutionState`, `timeBeginPeriod` | `Core/Native/NativeMethods.cs`, `Core/Imaging/ScreenCapture.cs`, `App/HotKeyNative.cs`, `App/StopKeyHook.cs` |
| Image matching | Own C# ZNCC template matcher (integral images, downscaled coarse pass, full-resolution refine) | `Core/Imaging/ImageMatcher.cs` |
| Crypto | .NET `System.Security.Cryptography` (ECDSA P-256, AES, HMAC-SHA256, SHA-256) | `Core/Licensing/*` |
| Build | .NET SDK 10.0+ (`dotnet build`), SDK-style projects, `LangVersion latest` | README.md, docs/STATUS.md |

There are no third-party dependencies. No `global.json`, `Directory.Build.props` or `nuget.config` exists.

## 3. Folder structure

```
AutomationTest.sln            Solution: Core, App, KeyGen (IconGen is NOT in the sln)
build-release.bat             Clean user build into Release/ (App + KeyGen), no secrets copied
README.md                     User/dev overview (Vietnamese)
CLAUDE.md                     Working rules (git flow, language, advisor, subagents)
docs/PROJECT.md               Requirements, decisions, script model, license design (Vietnamese)
docs/STATUS.md                Current state, manual test checklist, technical notes (Vietnamese)
docs/office-overview.md       This file
keys/make-license.bat         Admin helper that calls KeyGen "issue"; the private key file lives in keys/ but is git-ignored
src/AutomationTest.Core/      Engine library (about 4.6k lines)
  Model/                      AutomationScript, ActionNode (+ per-kind settings), WindowTarget, ActionTiming, ScriptSerializer
  Runner/                     ScriptRunner (recursive executor), SelectionPlan (Start Selected planning)
  Input/                      MouseController, KeyboardController (SendInput), KeyCodes, KeepAwake
  Imaging/                    ScreenCapture, RawImage, ImageMatcher (ZNCC), ImageLocator
  Expressions/                Expression parser/evaluator, Values, Coercion
  Recording/                  RecordConverter (raw events -> actions)
  Windows/                    WindowLocator (find/activate target window, client area)
  Native/                     NativeMethods (P/Invoke declarations)
  Licensing/                  LicenseData/LicenseCodec, LicenseChecker, RequestKey, LicenseState, TrustedClock, Secrets
src/AutomationTest.App/       WinForms UI (about 5.4k lines, MainForm.cs alone is 2.8k)
tools/AutomationTest.KeyGen/  Owner-only license issuing CLI (genkey, machineid, issue)
tools/AutomationTest.IconGen/ Redraws src/AutomationTest.App/app.ico
.claude/, .office/            Agent Office files (not part of the product)
```

`bin/`, `obj/`, `.vs/`, `Release/`, `*.key`, `license.lic`, `Setting.ini`, `/keys/*` (except `*.bat`) and `.office/` are
git-ignored (`.gitignore`). The local `obj/` folders still hold files named `Automation.Core.*` / `Automation.App.*`
from before the rename to AutomationTest; they are stale build output (harmless).

Line counts of `*.cs` (excluding bin/obj): Core about 4,600, App about 5,400, tools about 320. Largest files:

| File | Lines |
|---|---|
| `src/AutomationTest.App/MainForm.cs` | 2,829 |
| `src/AutomationTest.Core/Model/ActionNode.cs` | 645 |
| `src/AutomationTest.Core/Expressions/Expression.cs` | 573 |
| `src/AutomationTest.Core/Runner/ScriptRunner.cs` | 494 |
| `src/AutomationTest.Core/Recording/RecordConverter.cs` | 350 |
| `src/AutomationTest.App/HelpTexts.cs` | 274 |
| `src/AutomationTest.Core/Windows/WindowLocator.cs` | 260 |
| `src/AutomationTest.Core/Imaging/ImageMatcher.cs` | 253 |

## 4. Install, run, build, test

**Install (end user).** Nothing to install beyond Windows with .NET Framework 4.8 (built into Windows 10 1903+/11).
Copy the contents of `Release/AutomationTest/` (produced by `build-release.bat`) anywhere writable. The app writes
`license.lic` and `Setting.ini` next to the exe, so the folder must be writable (`LicenseManager.cs` returns a message
suggesting to run as administrator once if it is not).

**Run.** Start `AutomationTest.exe`. Without a valid `license.lic` only the License screen appears (`App/Program.cs`).
Default hotkeys: Start F3, Start Selected F4, Record F6, Pick F8 (changeable in Settings, stored in `Setting.ini`).

**Build (developer).**
- Prerequisite: .NET SDK 10.0 or newer on Windows.
- `dotnet build -c Release` at the repo root builds Core, App and KeyGen. Output:
  `src/AutomationTest.App/bin/Release/net48/AutomationTest.exe`.
- Close the running app first, otherwise the exe is locked (docs/STATUS.md).
- `build-release.bat` makes the clean distribution: `Release\AutomationTest\` and `Release\KeyGen\` (with
  `make-license.bat`), and deletes `*.pdb` and `*.deps.json`. It ends with `pause`, so it is interactive.
- IconGen (not in the sln): `dotnet run --project tools/AutomationTest.IconGen -- src/AutomationTest.App/app.ico`.

**Issue a license (owner).** Build KeyGen, place the signing key as `license-private.key` next to `make-license.bat`
(`keys/` in the source tree, or `Release\KeyGen\`), run `keys\make-license.bat`, enter request key, UTC send date and
expiry. Output is `license.lic` next to the bat. KeyGen CLI: `genkey`, `machineid`, `issue`
(`tools/AutomationTest.KeyGen/Program.cs`).

**Test.** There are **no automated tests in the repository**: no test project, no test framework reference, no CI
configuration found. docs/STATUS.md says tests were run from a throw-away C# harness kept in a session scratchpad outside
the repo (ExprTests, VarTests, RecordTests, UiTests) and that all passed at the last run. That harness is not available to
anyone cloning the repo. Verification today is the 15-item manual checklist in docs/STATUS.md ("Checklist test tay").
The UI and runner tests drive real mouse/keyboard, so a headless CI is not straightforward (guess).

## 5. Main modules and data flow

### 5.1 Script model -> runner -> engines

```
JSON file  <->  ScriptSerializer  <->  AutomationScript { Version=2, Actions: List<ActionNode> }
                                              |
        UI (MainForm) edits tree              v
                                       ScriptRunner.Run(...)  (worker thread via Task.Run, CancellationToken)
                                         |-- ExecuteMouse    -> WindowLocator (Process/Title/Anchor -> screen XY) -> MouseController (SendInput)
                                         |-- ExecuteKeyboard -> KeyboardController (SendInput, Unicode for Text)
                                         |-- Utilities       -> Process.Start (Open) / KeepAwake (Active)
                                         |-- Control: Loop / Wait (WindowLocator) / Image (ImageLocator -> ScreenCapture + ImageMatcher)
                                         |            Variable / Condition -> Expressions (Expression, VariableStore)
```

- `ActionNode` (`Core/Model/ActionNode.cs`) has Category (Mouse, Keyboard, Utilities, Control), Name, Disabled flag,
  children, and one settings object per kind (Mouse, Keyboard, Command, Loop, Wait, Image, Variable, Case). Control kinds
  include Loop, Wait, Image, Variable, Condition, Case, Else (enums in `Model/CoordinateMode.cs`).
- Persistence: JSON `Version` 2 only; older files are rejected with a message. All times are milliseconds. Template images
  are embedded in the script (Deflate + base64). New fields with non-zero defaults must be set in `[OnDeserializing]`
  `SetDefaults` (docs/STATUS.md technical notes).
- `ScriptRunner` is a synchronous recursive executor that runs on the calling thread; `MainForm.RunAsync` calls it inside
  `Task.Run` (`MainForm.cs` lines 2273, 2313, 2335). It keeps one `VariableStore` per run, a window cache, and sets of held
  mouse buttons and keys that are released in `finally`. It raises `NodeStarted` and `Message`. `timeBeginPeriod(1)` is set
  for the run.
- Window-relative coordinates: a Mouse action with Process set is an offset from the client area of the matched window,
  measured from an Anchor (TopLeft/TopRight/BottomLeft/BottomRight/Center); the runner activates the window and stops with
  an error if it is not found or the point is covered (`Windows/WindowLocator.cs`, docs/PROJECT.md).
- Image: `ImageLocator.Locate` is the single entry that turns Image settings into a screen search (runner and the UI Check
  button both use it). Results go into variables `Found`, `Position`, `Size` under the Image ID.
- Expressions (`Core/Expressions`): `{name}`, property access, arithmetic/comparison/logic operators, Position `{x,y}`,
  Range `{x,y,w,h}`, strings, dates; evaluated by a hand-written parser (`Expression.cs`).
- Start Selected: `SelectionPlanner` (`Runner/SelectionPlan.cs`) builds the action list and the variables that must be
  asked from the user (`App/VariableInputForm.cs`), then calls `ScriptRunner.Run(actions, variables, token)`.

### 5.2 UI -> core

- `Program.Main` -> license gate -> `new MainForm { LicenseGuard = EnsureLicensed }` (`App/Program.cs`).
- `MainForm` (2.8k lines) owns almost everything: the icon toolbar, the indented `ListView` action list (a TreeView cannot
  multi-select), the right-hand action editor panel (custom controls `HintTextBox`, `NumberBox`, `TimeBox`,
  `KeyCaptureBox`, `ToggleSwitch`, `IconButton`, `HelpIcon` + `HelpTexts`), unsaved-change prompts, collapse/expand, tray
  icon behaviour, and the orchestration of Start / Start Selected / Record / Pick.
- Hotkeys: `RegisterHotKey` while idle (`HotKeyNative.cs`); while a script runs or records, a low-level keyboard hook
  (`StopKeyHook.cs`) is used so only the key that started the run stops it, keys are not swallowed, and keys injected by the
  script itself are ignored (`LLKHF_INJECTED`).
- Record: `InputRecorder.cs` (low-level mouse+keyboard hooks) -> raw events -> `Core/Recording/RecordConverter.cs` -> actions
  inserted like Add.
- Pick / Capture: `PointPickerForm`, `RegionPickerForm` freeze a screenshot and let the user choose a point or region;
  `WindowOutlineForm` draws the red outline; `FreezeHintForm` is the hint bar for the "Advance position picker" mode.
- Settings: `AppSettings.cs` + `SettingsForm.cs`, stored through `IniFile.cs` in `Setting.ini` (sections Window, App,
  Hotkeys, Options, Columns, Files per docs/STATUS.md). Each write re-reads the file so other sections are kept.

### 5.3 Licensing flow

1. Startup (`Program.EnsureLicensed`): `LicenseManager.Check()` reads `license.lic` next to the exe and runs
   `LicenseChecker.Check` (`Core/Licensing/LicenseChecker.cs`): unpack (`LicenseCodec`), verify the ECDSA P-256/SHA-256
   signature with the embedded public key (`LicenseManager.PublicKey`), check Product, check Machine ID, check expiry.
2. Machine ID = SHA-256 of registry `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid` (64-bit view, read-only), formatted
   `XXXXX-XXXXX-XXXXX-XXXXX` (`MachineIdFormat`, `LicenseManager.MachineId`).
3. Not licensed: `LicenseForm` shows a **request key** (`RequestKey.Encode`: Machine ID + check bytes, AES with a key
   derived from an embedded secret and today's UTC date). The user sends it to the owner.
4. Owner: `keys/make-license.bat` -> `KeyGen issue` decrypts the request key (the entered UTC date must match), signs a
   JSON payload with the private key and writes `license.lic` (header + base64 payload + base64 signature).
5. User pastes or imports the file (`LicenseManager.ImportText`); on success it is written canonically next to the exe.
6. Expiring licenses additionally: activation requires network time (`TrustedClock`), and `Setting.ini [App] State` holds
   an AES-CBC + HMAC-SHA256 protected "last used" UTC time (`LicenseState`) to detect clock rollback while offline
   (tolerance 2 minutes per docs). Missing or damaged state means re-activation. `MainForm` re-checks through
   `LicenseManager.Check()` (`MainForm.cs` around lines 2236-2250, via `LicenseGuard`); docs say it runs at startup, after
   New/Open/Save/Save As/Stop, and before Start, Start Selected and Record.
7. Perpetual licenses (no `Expires`) need no network and keep no state.

KeyGen references Core, so the licensing code including `Secrets.cs` is compiled into both the app and KeyGen.

## 6. External services

The product has no backend and no telemetry. The only network use is the time lookup in `Core/Licensing/TrustedClock.cs`,
and only for licenses with an expiry date:
- HTTPS `HEAD` to `https://www.microsoft.com`, `https://www.google.com`, `https://www.cloudflare.com`, reading the `Date`
  header (3 s timeout, no redirects, user agent `Mozilla/5.0`).
- NTP (UDP 123) to `time.windows.com`, `time.google.com`, `pool.ntp.org`.
- All six run in parallel; HTTPS answers are preferred. The result is cached for 1 hour and carried forward with a
  stopwatch; after a failure the next try waits 10 minutes (`AllowRetry` resets this on activation).

Other external touchpoints: Windows registry read of `MachineGuid`; `Process.Start` with shell execute for the Open action
(`Core/Runner/ScriptRunner.cs` line 160). A sibling Rust rewrite (`D:\Working\AutomationRust`, repo `automation_rust`, per
README/STATUS) shares the script JSON, `license.lic` and `Setting.ini` formats. Git remote:
`https://github.com/hano-c4l/automation.git`.

## 7. Current quality and risks

State: per docs/STATUS.md (last updated 2026-11-10) all planned features (milestones 1 to 5) are implemented and merged to
`preview` and `main`. The code is commented in English and Core is a clean separate library. A search for TODO/FIXME/HACK
markers found none. Exception handling is mostly narrow filters (for example `when (ex is IOException || ...)`); the one broad
`catch (Exception)` is in `Core/Windows/WindowLocator.cs` line 254 (process name lookup, deliberately returns an empty
string). Remaining work listed in STATUS.md: manual license tests (Import via UI, clean machine, offline expiring license),
manual UI tests, code signing, online activation, obfuscation.

**Testing gaps**
- No automated tests in the repo (section 4). The safety net is an external harness and a manual checklist.
- Not verified at all (docs/STATUS.md): UI at 125%/150% DPI, mixed-DPI multi-monitor, Unikey/EVKey during Text typing,
  games/DirectX (capture may be black), running against elevated apps (UIPI).
- `LicenseManager.Clock`, `LicenseManager.Folder`, the `LicenseManager.MachineId` setter and `TrustedClock.Fetcher` are
  mutable statics added as test hooks; useful for tests, but they widen the in-process attack surface of the license check.

**Code hot spots**
- `MainForm.cs` (2,829 lines, about 130 members) mixes UI layout, editor logic, tree-editing rules, run/record
  orchestration, tray handling and column sizing. Highest change risk and hardest to test. Splitting by responsibility
  would help (suggestion).
- `ActionNode.cs` (645) and `Expression.cs` (573) are large but cohesive.
- The list column auto-sizing and `ShowScrollBar` workarounds (docs/STATUS.md) rely on fragile Win32/WinForms behaviour.

**Licensing security**
- **The embedded secret is in the repository.** `src/AutomationTest.Core/Licensing/Secrets.cs` (value not reproduced here)
  holds the symmetric secret used to derive the request-key encryption. The file is a normal source file, it is not matched
  by `.gitignore`, and it was not listed as untracked in the git status at the start of this task, so it is tracked in git.
  The repo has merged GitHub PRs (#30, #31 in the log) and a remote at `hano-c4l/automation`, so the secret is in remote
  history and visible to everyone with read access. Rotating it would invalidate all issued licenses and request keys
  (STATUS.md says the related identifiers are frozen on purpose). Whether the remote is private is not known to me.
- Protection is deterrence only (the docs say so): .NET assemblies are easy to decompile and patch. The public key and the
  check are in the same assembly, so a patched exe bypasses it. No obfuscation, no code signing, no online revocation.
- Anyone with the binary can derive request keys from the secret; the `Setting.ini` state key is also derived from the
  machine and the license text (`LicenseState.cs`), so a determined attacker with the binary can probably forge it (guess).
  Request keys use AES in ECB mode on a single block (`RequestKey.cs`, justified in a code comment because the key changes
  daily).
- The signing private key is kept out of git by design (`*.key`, `/keys/*` ignored). Backup is the owner's job; loss means
  re-releasing with a new public key.
- Offline clock-rollback defence is a "last used" timestamp in a file next to the exe; deleting it forces re-activation,
  which needs the network. Perpetual licenses have no time logic at all.
- Network time relies on HTTPS `Date` headers and NTP; a machine with an intercepting trusted root or NTP spoofing could feed
  false time (low likelihood; guess).
- `license.lic` and `Setting.ini` live next to the exe, so installing under `Program Files` fails unless run as administrator
  once.

**Safety and behaviour risks**
- The Open action runs any path via shell execute: opening someone else's script can launch anything in it (acknowledged in
  STATUS.md). No warning is shown (not verified in the UI code).
- The tool injects global input. While idle it registers Start, Start Selected and Record (F3/F4/F6 by default) as
  system-wide hotkeys, so other programs do not receive those keys; the Pick key (F8) is registered only while waiting for
  a capture (docs/STATUS.md).
- The exe is unsigned: SmartScreen warns, and **Smart App Control on Windows 11 blocks it entirely**. The fix is signing with a
  CA-trusted certificate (not done; docs/PROJECT.md section 4.7).
- The emergency stop on mouse-to-corner is not implemented.
- No migration tool for version 1 files or for the changed Image Anchor semantics (STATUS.md).
- Doc drift: Record is F7 in docs/PROJECT.md section 5 and one STATUS.md note, but F6 in README, STATUS and Settings
  defaults; STATUS.md has a broken path `D:WorkingAutomationRust`; docs are Vietnamese although CLAUDE.md requires English
  in the repo.
- `ImageMatcher` is custom code with no tests in the repo; performance on large regions is not measured here (guess:
  acceptable because of the downscaled coarse pass).

## 8. Open questions

1. Is the GitHub remote `hano-c4l/automation` private? Given that `Secrets.cs` is in git history, should the embedded secret
   be rotated (accepting that issued licenses must be re-issued) or the request-key scheme moved to asymmetric crypto
   (docs/PROJECT.md suggests it) and the secret moved out of the repo?
2. Where is the test harness (ExprTests, VarTests, RecordTests, UiTests) now? Should it be recovered and committed as a
   test project?
3. Should `README.md`, `docs/PROJECT.md` and `docs/STATUS.md` be translated to English to follow CLAUDE.md, or is Vietnamese
   intended for them?
4. Which Record key is correct: F6 (README, Settings defaults) or F7 (PROJECT.md section 5, one STATUS.md note)?
5. Is the Rust rewrite (`automation_rust`) the future product, with this C# version in maintenance, or do both evolve?
   Format compatibility between the two is a standing constraint.
6. Release plan: is code signing planned before wider distribution? Online activation and obfuscation are "later" in the
   docs; is there a date or trigger?
7. Is any CI, installer or packaging expected, or is `build-release.bat` the whole release process?
8. Should `tools/AutomationTest.IconGen` be added to the solution (it is currently built only by `dotnet run`)?
9. Is a refactor of `MainForm.cs` in scope, or only feature work?
10. `.claude/agents/code_analyst.md` and `pm.md` are untracked on `preview` (git status at task start); PM should decide
    whether to commit them.
