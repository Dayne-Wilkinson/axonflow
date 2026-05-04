---
name: axonflow
description: >-
  Materialize and maintain development plans in AxonFlow (SQLite CLI): hierarchical
  work items, dependencies, blockers, emergent todos with provenance, notes, and
  JSON-first queries. Use when planning multi-step work, breaking epics into
  session-sized tasks, tracking live todos, or when the user mentions AxonFlow,
  work graph, backlog, or agent-managed plans.
disable-model-invocation: true
---

# AxonFlow agent skill

## When to use

- Any multi-step implementation, refactor, or investigation where a **durable plan** beats chat-only context.
- User asks for a plan: **write it to AxonFlow** (`item import` or `item add`), not only in the reply.
- Tracking **emergent** todos discovered during work.

## Paths

- **CLI default `--db`** is **`Paths.DefaultDbPath()`** → `~/.axonflow/axonflow.db` (Windows: `%USERPROFILE%\.axonflow\axonflow.db`), so a **global** `axonflow` works from any cwd.
- **Repo-local graph** — use `--db .axonflow/axonflow.db` (or an absolute path under the repo) when you want a per-project database.
- A **.NET global tool** install puts `axonflow` on `PATH` under `~/.dotnet/tools` (Windows: `%USERPROFILE%\.dotnet\tools`); the **database is not stored there**—only the executable.
- **Prefer passing `--db` explicitly** (absolute path, or a path relative to the known repo root) whenever you mean a specific workspace file.

## Loop

1. `schema` if debugging version skew; `init` if the DB file is missing.
2. Initial breakdown: **`item import --json`** (stdin) with `clientKey`, `tempId`, `stream: plan`, and `dependencies`; run **`--dry-run`** first on large payloads.
3. Before coding: **`item next --json`** — read `picked` and `excluded` entries.
4. Claim work: **`item start --assignee agent:<yourName> --ref AF-n`** (do not skip in multi-agent flows).
5. During work: new findings → **`item add --stream emergent --discovered-from <activeRef> --client-key ...`**; append **`item note add --ref ... --body "..."`** for decisions and progress.
6. Park work with **`item defer --until <ISO>`** instead of leaving misleading `ready` rows.
7. Complete: **`item complete`** (respects children and predecessors unless `--force`).
8. Session end: **`validate`** (structural + health); optional **`export --format markdown`** only if the user wants a git-visible snapshot.

## CLI cheat sheet

```text
dotnet run --project src/AxonFlow -- init --db .axonflow/axonflow.db
dotnet run --project src/AxonFlow -- item add --db .axonflow/axonflow.db --type task --title "..." --json
dotnet run --project src/AxonFlow -- item import --db .axonflow/axonflow.db --json --dry-run < plan.json
dotnet run --project src/AxonFlow -- project set-name --db .axonflow/axonflow.db --slug default --name axonflow
dotnet run --project src/AxonFlow -- item next --db .axonflow/axonflow.db --json
dotnet run --project src/AxonFlow -- item start --db .axonflow/axonflow.db --ref AF-1 --assignee agent:composer --json
dotnet run --project src/AxonFlow -- dep add --db .axonflow/axonflow.db --predecessor AF-1 --successor AF-2
dotnet run --project src/AxonFlow -- validate --db .axonflow/axonflow.db --json
dotnet run --project src/AxonFlow -- dashboard emit --db .axonflow/axonflow.db --out dashboard --refresh-seconds 120
dotnet run --project src/AxonFlow -- dashboard emit --db .axonflow/axonflow.db --out dashboard --all-projects
dotnet run --project src/AxonFlow -- dashboard open --db .axonflow/axonflow.db --out dashboard
dotnet run --project src/AxonFlow -- dashboard watch --db .axonflow/axonflow.db --out dashboard --interval 120
dotnet run --project src/AxonFlow -- dashboard serve --db .axonflow/axonflow.db --out dashboard --urls http://127.0.0.1:5057 --refresh-seconds 120
# User-level DB (same file global workflows often use), if .axonflow under cwd does not exist:
#   Windows:   --db %USERPROFILE%\.axonflow\axonflow.db
#   macOS/Linux: --db ~/.axonflow/axonflow.db
.\scripts\install-global.ps1   # or scripts\install-global.cmd if execution policy blocks .ps1
./scripts/install-global.sh
```

After global install (if you publish a tool), replace `dotnet run --project src/AxonFlow --` with `axonflow`.

## Rules

- **Leaf tasks** should be **one-session sized** (one clear outcome).
- **`client_key`** on planned rows for idempotent retries.
- Emergent items: **`--discovered-from`** required unless the user explicitly allows **`--no-provenance`**.
- Multi-agent: only **`item start`** items you own; use **`item next --assignee`** filtering.

## Remote

Upstream repo: [https://github.com/Dayne-Wilkinson/axonflow](https://github.com/Dayne-Wilkinson/axonflow)
