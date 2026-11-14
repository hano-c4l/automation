## Advisor (Opus 5.5)
Consult the advisor at these three points:
- Before finalizing a plan.
- When the same error occurs a second time.
- Before reporting that work is done.

## Subagents
- Use `search` for repo maps, `grep` for call sites, and `docs` for API documentation. All three are read-only Haiku agents.

## Background tasks
- Before reporting work as done, check for background tasks you started. If one is not needed for what comes next, stop it, and say in the report which ones you stopped and which you kept (with the reason).

## Build for the user to test
- The program the user tests is ALWAYS built into `D:\Working\Automation\Release`, with `build-release.bat` (run it as `cmd /c ".\build-release.bat < NUL"` so the final `pause` does not wait). Never point the user at `src\...\bin\Debug` or `bin\Release`, and never build a test copy anywhere else for them.
- After finishing a change the user will test, rebuild `Release` before reporting, and say which build it is.
- `build-release.bat` keeps the user's own files across a rebuild: `Release\AutomationTest\Setting.ini`, `Release\AutomationTest\license.lic` and `Release\KeyGen\license-private.key`. Never delete or overwrite them by hand either, and never read their contents.
- Close the running AutomationTest.exe before rebuilding (it locks its files and holds port 47615); do not kill the user's process, ask them.
- Internal checks (compile, test harnesses) may build to a scratch folder, but that is not a build to hand over.

## Git
- The `main` branch is the production branch. Code lands here only after it is finished and confirmed OK on preview.
- The `preview` branch is the testing branch. New and changed features are tested here before moving to production.
- When starting a new task, create a `feature_xxx` branch from the `preview` branch.
- When a task is finished and the user says "PR into preview", create a PR from that feature branch into `preview`, merge it right away, and run the deploy/migration for preview if there is one.
- Likewise, when the user says "PR into production", create a PR from `preview` into `main`, merge it, and run the deploy/migration for production if there is one.

## Language
- The user chats and gives requests in Vietnamese or English, so reply to the user in Vietnamese.
- Everything inside the source code (variables, functions, comments, messages, logs, etc.) must always be written in English.
