---
name: test-planning
description: Office role skill for the QA Tester. How to plan and automate tests for the Automation tool.
user-invocable: false
---

# Test planning (Automation)

- Map every acceptance criterion to at least one test case in `tests/test-plan.md`; include edge cases and negative tests.
- Automated tests: a test project under `tests/` (for example `tests/AutomationTest.Core.Tests`, xUnit or NUnit,
  targeting `net48` or a modern .NET that can reference the netstandard2.0 Core). Add it to `AutomationTest.sln`
  and run with `dotnet test`.
- Good automated targets: `ScriptSerializer` round trips (old files still load), expressions and coercion,
  runner control flow with fake input controllers, `ImageMatcher` on in-memory `RawImage`s, license parsing and
  expiry with a fixed clock.
- Never move the real mouse, send real keys, take real screenshots or hit the network in automated tests.
- UI-only behaviour (hotkeys, Pick overlay, recorder, dialogs) stays in the manual checklist in `docs/STATUS.md`.
