# AxonFlow

**Repository:** [github.com/Dayne-Wilkinson/axonflow](https://github.com/Dayne-Wilkinson/axonflow)

## What it is

AxonFlow is a **small command-line app** (cross-platform **.NET 9**) that keeps your work in a **single SQLite database** as a **live graph**: epics, features, stories, tasks, bugs, and chores sit in one hierarchy; **dependencies** express “finish A before B”; **blockers** capture who or what is in the way; **emergent** items record work discovered mid-flight with **provenance** (where the idea came from); **notes** append a running log without rewriting specs in chat.

It is built for two audiences:

1. **Coding agents** — stable **`--json`** I/O, **`client_key`** idempotency, bulk **`item import`**, an explainable **`item next`** result (`picked`, `candidates`, `excluded` with reasons), and commands that map cleanly to tool/shell calls.
2. **Humans** — plain-text and ASCII **`board`** / **`tree`** views, `export` to Markdown or JSON, and ad-hoc **`sqlite3`** / GUI access to the same file.

The database file is **yours** (default `.axonflow/axonflow.db` next to where you run the CLI). It is **not** a hosted SaaS: no accounts, no server—only optional future local UI on top of the same schema.

## Why agents use it

LLM conversations **lose detail** (summarization, context limits) and **fork** when you parallelize work. AxonFlow is the **plan of record**: one structured store that survives across turns, branches, and sessions. Agents should **read/write the graph** the same way they read/write code—via the CLI—so “what’s next?”, “what’s blocked?”, and “what did we decide?” stay **queryable** instead of buried in transcript.

**Practical habits for agents:**

- Prefer **`--json`** on every call so responses are machine-parseable.
- Pass an explicit **`--db`** (ideally absolute) so the correct repo’s DB is used regardless of shell cwd.
- **`init`** once per machine/clone that should own a DB; then **`item import`** (or repeated **`item add --client-key …`**) to materialize plans; **`--dry-run`** before large imports.
- Use **`item next`** before picking work; use **`item start --assignee …`** when multiple agents (or human + agent) share a backlog.
- After meaningful progress: **`item note add`** on the active item; for surprises: **`item add --stream emergent --discovered-from <ref>`**.
- End of session (or before a handoff): **`validate`**; optional **`export --format markdown`** if the team wants a git-visible snapshot.

The full agent loop and copy-paste examples live in the **Cursor skill** (see below).

## Quick start

```bash
dotnet run --project src/AxonFlow -- init
dotnet run --project src/AxonFlow -- item add --type task --title "First task" --json
dotnet run --project src/AxonFlow -- item next --json
```

Default database: `.axonflow/axonflow.db` under the current working directory (override with `--db`).

### Global options (most commands)

| Option | Default | Description |
|--------|---------|-------------|
| `--db` | `./.axonflow/axonflow.db` | SQLite file path |
| `--project` | `default` | Project slug |
| `--json` | off | Machine-readable stdout |
| `--dry-run` | off | Validate / plan writes without committing (where supported) |

## Commands (overview)

- `schema` — CLI and embedded DB schema version
- `init` — create DB, apply migration, ensure default project
- `project add|list`
- `item add|update|show|list|import|start|next|complete|cancel|reopen|note add|defer`
- `dep add|remove`
- `tree`, `board`, `validate`, `export`

Use `--help` on the root or any command for details.

## Adding the skill to your agent (Cursor)

AxonFlow ships a **Cursor Agent skill** in this repo: [.cursor/skills/axonflow/SKILL.md](.cursor/skills/axonflow/SKILL.md). Skills are Markdown instructions the agent can load when relevant; this one tells the agent **when** to use AxonFlow, **which** flags to pass, and a **repeatable workflow** (import → next → start → notes → validate).

The skill is configured with **`disable-model-invocation: true`**, so Cursor will **not** auto-attach it to every chat from ambient context alone. **You (or the user) should invoke it explicitly** when starting agent work on a backlog—for example: ask the agent to *“follow the AxonFlow skill”*, *“read `.cursor/skills/axonflow/SKILL.md` before planning”*, or use your Cursor workflow for **@skills / skill picker** if you have skills enabled there.

### Option A — Skill only for this repository (clone of AxonFlow)

If you cloned **this** repo, the path already exists:

`c:\src\AxonFlow\.cursor\skills\axonflow\SKILL.md` (Windows) or `<repo>/.cursor/skills/axonflow/SKILL.md` in general.

Commit it with your branch; Cursor loads **project** skills from `.cursor/skills/<name>/` inside the workspace.

### Option B — Skill in *your* application repo (recommended for day-to-day product work)

1. Copy the folder **`axonflow`** from this repo’s `.cursor/skills/` into **your** project:

   - From: `<axonflow-repo>/.cursor/skills/axonflow/`  
   - To: `<your-app>/.cursor/skills/axonflow/`

   So your app contains at least:

   `<your-app>/.cursor/skills/axonflow/SKILL.md`

2. Adjust the **cheat sheet** inside `SKILL.md` if your paths differ (e.g. path to `AxonFlow` checkout, or a globally installed `axonflow` tool once you publish one).

3. Open **your** repo in Cursor; the skill appears as a **project** skill for that workspace.

### Option C — Personal skill (all workspaces on your machine)

Copy the same `axonflow` folder (with `SKILL.md`) into your **user** skills directory so every project sees it:

| OS | Folder |
|----|--------|
| Windows | `%USERPROFILE%\.cursor\skills\axonflow\` → put `SKILL.md` there (create folders if missing) |
| macOS / Linux | `~/.cursor/skills/axonflow/` |

Do **not** put custom skills under `~/.cursor/skills-cursor/` — that tree is reserved for Cursor’s built-in skills.

### Option D — Other agents (not Cursor)

There is no universal “skill” standard. For **Codex CLI**, **Claude Code**, **Aider**, etc., reuse the same Markdown: save `SKILL.md` (or an excerpt) as a **project rule**, **AGENTS.md** section, or **prompt snippet** your runner injects; keep the **CLI contract** (`--db`, `--json`, commands) identical.

## Build & test

```bash
dotnet build AxonFlow.sln
dotnet test AxonFlow.sln
```

## Phase 2

Read-only local **HTML dashboard** (same SQLite schema) is planned; not implemented in this milestone.
