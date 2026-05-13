---
name: axonflow
description: >-
  Materialize and maintain development plans in AxonFlow (SQLite CLI): hierarchical
  work items, dependencies, blockers, emergent todos with provenance, notes, and
  JSON-first queries. Use when planning multi-step work, breaking epics into
  session-sized tasks, tracking live todos, or when the user mentions AxonFlow,
  work graph, backlog, or agent-managed plans.
disable-model-invocation: false
---

# AxonFlow agent skill

## When to use

- Any multi-step implementation, refactor, or investigation where a **durable plan** beats chat-only context.
- User asks for a plan: **write it to AxonFlow** (`item import` or `item add`), not only in the reply.
- Tracking **emergent** todos discovered during work.

## Paths

- **Default `--db`** is **`~/.axonflow/axonflow.db`** (Windows: **`%USERPROFILE%\.axonflow\axonflow.db`**). The first non-dry-run command that needs the DB creates it; you do **not** rely on a separate “repo-local” database for day-to-day use.
- **`--db <path>`** is only for overrides (tests, migration, advanced cases). Treat the default file as the **single logical store** for a machine; **`--project`** partitions work inside that file.
- **Project slug = workspace identity** — align work with the **repository (or product) folder** you are in. The slug AxonFlow infers from cwd is a **normalized form of that folder name** (lowercase, hyphenated, ASCII). Prefer that convention when creating projects too: pick a **sensible variant** of the folder name (`MyApp` → `myapp`, `some_service` → `some-service`), not unrelated names. **Do not** park real product work in **`default`** unless that slug is intentionally this product’s home.
- **Check the database before any write** — run **`project list --json`** (same `--db`) **before** `item import`, first `item add`, or resuming a plan after opening a different repo. Confirm which **slug** (and display name) already exists for this workspace. If nothing matches, **`project add --name … --slug …`** using the folder-aligned slug above, then target that slug for all subsequent commands. Skipping this step is how work lands in the wrong partition when the DB already contains multiple projects or names from another machine.
- **After switching directories or repos, pin `--project`** — If you omit `--project`, the slug comes from **the shell’s current directory name only**. `cd` to a parent path, a different drive, or another clone can silently retarget **`item add` / `item import` / `item start`** to the wrong project. When you have **more than one project** in the DB or you **changed folders** since the last command, pass **`--project <slug>`** explicitly until cwd is confirmed to be the intended repo root.
- **Dashboard impact** — The read-only dashboard is **partitioned by project**. Items exist in exactly one project row in the picker. Wrong slug → work is **missing** from the board you expect or **mixed** with another product’s graph. Verifying `project list` + using the correct `--project` prevents that.
- **`--project` summary** — Use explicit **`--project`** whenever there is ambiguity. In **this repository**, folder **`AxonFlow`** → slug **`axonflow`**. In **automated tests** only, pin **`--project default`** when the harness seeds that slug.
- A **.NET global tool** puts `axonflow` on `PATH` under `~/.dotnet/tools` (Windows: `%USERPROFILE%\.dotnet\tools`); only the binary lives there—not the database.

## Loop

1. **`schema`** only when debugging version skew. Do **not** require `init`; the graph is ready after the first real write (or use `init` deliberately if the user wants explicit setup).
2. **`project list --json`**; align **`--project`** with this folder’s slug (create with **`project add`** if missing). Initial breakdown: **`item import`** with `clientKey`, `tempId`, `stream: plan`, and `dependencies`; run **`--dry-run`** first on large payloads. (**`--dry-run` + missing DB** fails—run one non-dry command first, or pass an existing `--db`.)
3. Before coding: **`item next --json`** — read `picked` and `excluded` entries.
4. Claim work: **`item start --assignee agent:<yourName> --ref AF-n`** (never skip in multi-agent flows).
5. During work: new findings → **`item add --stream emergent --discovered-from <activeRef> --client-key ...`**; append **`item note add`** for decisions/progress.
6. Park work with **`item defer`** instead of leaving misleading `ready` rows.
7. Finish: **`item complete`** / **`item cancel`** so AxonFlow mirrors reality.
8. Session end: **`validate --json`**.

## Status discipline (mandatory)

Keep **status and assignee aligned with what is actually happening** (see **AF-156** in-plan if present):

- **`item start`** before implementation; **`item complete`** / **`item cancel`** when done or dropped; use **`item defer`** when blocked.
- Never leave the primary ref in `backlog`/`ready` while you are actively implementing.
- If you switch refs or discover follow-ups, update notes and use **`--discovered-from`** for emergent items.
- **If AxonFlow is wrong, fix AxonFlow before or as you code—not after the fact.**

### Lifecycle command semantics (current)

- **`item start`** moves an item to `in_progress` and now validates unsatisfied predecessors unless `--force` is used.
- **`item defer --until ...`** marks the item `blocked` and applies snooze metadata; this is the preferred way to represent parked/blocked work.
- **`item defer --clear`** clears snooze and restores `ready` for non-active work (or keeps `in_progress` if the item is actively worked).
- **`item complete`** now expects `in_progress` unless `--force` is provided; this prevents silent `backlog -> done` jumps in normal flows.
- Automation that previously completed directly from `backlog` should either call `item start` first or use `item complete --force` intentionally.

## Entry quality standard (mandatory)

- Every item body must be **technically detailed**. Do not create one-line bodies for stories, tasks, bugs, chores, or spikes.
- Write entries so a brand-new agent can resume after a context reset with no extra chat history.
- Treat AxonFlow as the source of truth for handoff state; keep key details in the item body and notes, not only in transient conversation.

### Minimum body requirements (all planned items)

Each item body must include:

1. **Objective** — exact outcome and why it matters.
2. **Scope** — what is in and out.
3. **Implementation details** — architecture, files/modules, APIs/contracts, data model expectations.
4. **Execution steps** — concrete sequence to perform.
5. **Validation plan** — how to verify (tests, commands, expected signals).
6. **Risks/blockers** — known hazards, assumptions, dependencies.
7. **Handoff context** — what the next agent should check first if work pauses.

If this cannot fit clearly in one paragraph, use structured bullet points inside `body`.

### Story decomposition rule (mandatory)

- Stories are coordination containers and must be decomposed into executable child tasks.
- Every story should have child tasks that are one-session sized and independently completable.
- Do not mark a story in progress until there is at least one actionable child task.
- Prefer explicit dependencies between child tasks when ordering matters (`dependencies` in import payload or `dep add`).

Recommended task split under each story:

- Setup / prep task (environment, schema, feature flag, scaffolding).
- Core implementation task(s) (group by cohesive code path).
- Validation task (tests, instrumentation, dashboards, manual checks).
- Cleanup/follow-up task (docs, migration notes, rollback considerations) when needed.

### Resume-safe note standard

For active work, append notes frequently using `item note add` with:

- current status and timestamped progress,
- exact files/paths touched,
- commands run and outcomes,
- decisions made and rationale,
- remaining work and next immediate command.

Write notes so the next agent can continue immediately without re-discovery.

## CLI cheat sheet

```text
# Default global DB. List projects before imports so the slug matches this workspace:
dotnet run --project src/AxonFlow -- project list --json
dotnet run --project src/AxonFlow -- item add --project axonflow --type task --title "..." --body "..." --json
dotnet run --project src/AxonFlow -- item update --ref AF-1 --body-file path/to/spec.md --json
dotnet run --project src/AxonFlow -- item list --parent AF-12 --json
dotnet run --project src/AxonFlow -- item list --assigned-to agent:composer --body-contains needle --updated-after 2026-05-01T00:00:00Z --json
dotnet run --project src/AxonFlow -- item import --project axonflow --file plan.json --json --dry-run
dotnet run --project src/AxonFlow -- project set-name --slug default --name myproduct
dotnet run --project src/AxonFlow -- item next --json
dotnet run --project src/AxonFlow -- item start --ref AF-1 --assignee agent:composer --json
dotnet run --project src/AxonFlow -- dep add --predecessor AF-1 --successor AF-2
dotnet run --project src/AxonFlow -- validate --json
dotnet run --project src/AxonFlow -- dashboard
dotnet run --project src/AxonFlow -- dashboard --poll-seconds 10
# Use --json on dashboard to avoid launching a browser.
.\scripts\install-global.ps1   # or scripts\install-global.cmd if execution policy blocks .ps1
./scripts/install-global.sh
```

After global install, replace `dotnet run --project src/AxonFlow --` with `axonflow`.

## Rules

- **Project partition discipline** — Before writing to AxonFlow, **`project list --json`**. Match the active workspace to an existing **slug** (or **`project add`** a folder-aligned slug). Use **`--project`** on every command when cwd might not be the repo root or when multiple products share one machine DB. Never assume **`default`** is the right target for product work.
- **Leaf tasks** should be **one-session sized** (one clear outcome).
- **`client_key`** on planned rows for idempotent retries.
- Emergent items: **`--discovered-from`** required unless the user explicitly allows **`--no-provenance`**.
- Multi-agent: only **`item start`** items you own; use **`item next --assignee`** filtering.
- Planned stories must include child tasks before execution; no story-only plans.
- No one-line item bodies for planned work; include objective, scope, implementation details, validation, and handoff context.
- Maintain **live status parity** (`start`/`complete`/`cancel`/`defer`) — see Status discipline above.
- When using the dashboard for live tracking, prefer explicit **`dashboard --poll-seconds <n>`** to match the team's desired freshness and host load.

## Remote

Upstream repo: [https://github.com/Dayne-Wilkinson/axonflow](https://github.com/Dayne-Wilkinson/axonflow)
