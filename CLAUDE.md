## Advisor (Opus 5.5)
Consult the advisor at these three points:
- Before finalizing a plan.
- When the same error occurs a second time.
- Before reporting that work is done.

## Subagents
- Use `search` for repo maps, `grep` for call sites, and `docs` for API documentation. All three are read-only Haiku agents.

## Background tasks
- Before reporting work as done, check for background tasks you started. If one is not needed for what comes next, stop it, and say in the report which ones you stopped and which you kept (with the reason).

## Git
- The `main` branch is the production branch. Code lands here only after it is finished and confirmed OK on preview.
- The `preview` branch is the testing branch. New and changed features are tested here before moving to production.
- When starting a new task, create a `feature_xxx` branch from the `preview` branch.
- When a task is finished and the user says "PR into preview", create a PR from that feature branch into `preview`, merge it right away, and run the deploy/migration for preview if there is one.
- Likewise, when the user says "PR into production", create a PR from `preview` into `main`, merge it, and run the deploy/migration for production if there is one.

## Language
- The user chats and gives requests in Vietnamese or English, so reply to the user in Vietnamese.
- Everything inside the source code (variables, functions, comments, messages, logs, etc.) must always be written in English.

<!-- agent-office:start -->
# Automation (Agent Office department)

This repository is a department of Agent Office. A virtual team of Claude Code agents builds the product here:
the PM runs the `/office` skill, proposes a team, and delegates work to the agents in `.claude/agents/`.
The Agent Office dashboard watches this folder and lets the customer follow and steer the work.

## Office files
- `.claude/agents/`, `.claude/skills/`, `.claude/hooks/`, `.claude/settings.json`: the team and its tools.
  Only the PM and the dashboard change them.
- `.office/`: live office state (events, tasks, team proposal, staff names and looks). Ignored by git.

## Language
- Talk to the customer in Vietnamese.
- Everything in the repository (code, comments, docs, commit messages, logs) is in English.

## Git
- `main` is production. Code reaches `main` only when the customer releases it from the dashboard.
  Never commit to, merge into or push `main` yourself.
- `preview` is the testing branch. Finished, tested work is merged here and pushed.
- For each request, the PM creates one `feature_<short-name>` branch from `preview`; the whole team works on it.
- Only the PM runs git commands that change the repository (commit, checkout, merge, push). Other agents only edit files.
- The PM commits after each finished task with a clear English message, for example `T3: Add login API`.
- When the work is done and tested: merge the feature branch into `preview` with `--no-ff` and push `preview` (remote: https://github.com/hano-c4l/automation.git).
- Never force-push, rewrite history or delete branches.
<!-- agent-office:end -->
