# AxonFlow

**Repository:** [github.com/Dayne-Wilkinson/axonflow](https://github.com/Dayne-Wilkinson/axonflow)

## What it is

AxonFlow is a **small command-line app** (cross-platform **.NET 8**) that keeps your work in a **single SQLite database** as a **live graph**: epics, features, stories, tasks, bugs, and chores sit in one hierarchy; **dependencies** express “finish A before B”; **blockers** capture who or what is in the way; **emergent** items record work discovered mid-flight with **provenance** (where the idea came from); **notes** append a running log without rewriting specs in chat.

It is built for two audiences:

1. **Coding agents** — stable **`--json`** I/O, **`client_key`** idempotency, bulk **`item import`**, an explainable **`item next`** result (`picked`, `candidates`, `excluded` with reasons), and commands that map cleanly to tool/shell calls.
2. **Humans** — plain-text and ASCII CLI **`board`** and **`tree`** views, `export` to Markdown or JSON, an optional **HTML dashboard** (board + tree pages; see Phase 2), and ad-hoc **`sqlite3`** / GUI access to the same file.

The database file is **yours** (default **`~/.axonflow/axonflow.db`**, i.e. **`%USERPROFILE%\.axonflow\axonflow.db`** on Windows). The first normal command (**without** **`--dry-run`**) creates that file and migrations if needed. **`--db`** overrides the path only for advanced use; there is intentionally **one logical database** for everyday work—items are partitioned by **`--project`**, not separate DB paths.

## Why agents use it

LLM conversations **lose detail** (summarization, context limits) and **fork** when you parallelize work. AxonFlow is the **plan of record**: one structured store that survives across turns, branches, and sessions. Agents should **read/write the graph** the same way they read/write code—via the CLI—so “what’s next?”, “what’s blocked?”, and “what did we decide?” stay **queryable** instead of buried in transcript.

**Practical habits for agents:**

- Prefer **`--json`** on every call so responses are machine-parseable.
- Pass **`--project <slug>`** when you mean a named workspace slice; **if you omit `--project`, the slug is inferred from your current folder name** (sanitize to lowercase `a-z0-9`-separated segments). Omitting project is fine for exploratory use; CI/tests often pin **`--project default`** explicitly.
- The DB is created automatically when you run a writer (or **`project list`** / **`dashboard`**) once without **`--dry-run`**; **`init`** remains as an explicit noop-style setup if you want it. Prefer **`item import`** (or **`item add --client-key …`**) with **`--dry-run`** before large payloads.
- Use **`item next`** before picking work; use **`item start --assignee …`** when multiple agents (or human + agent) share a backlog.
- After meaningful progress: **`item note add`** on the active item; for surprises: **`item add --stream emergent --discovered-from <ref>`**.
- End of session (or before a handoff): **`validate`**; optional **`export --format markdown`** if the team wants a git-visible snapshot.

The full agent loop and copy-paste examples live in the **Cursor skill** (see below).

## Quick start

```bash
dotnet run --project src/AxonFlow -- item add --type task --title "First task" --project default --json
dotnet run --project src/AxonFlow -- item next --project default --json
```

Default database: **`~/.axonflow/axonflow.db`**. Overrides: **`--db <path>`** only when needed.

### Use the `axonflow` command (global tool)

Until you install the tool, run via **`dotnet run --project src/AxonFlow --`** (examples below use that form). To put **`axonflow`** on your `PATH` as a [.NET global tool](https://learn.microsoft.com/dotnet/core/tools/global-tools):

```bash
dotnet pack src/AxonFlow/AxonFlow.csproj -c Release -o ./artifacts
dotnet tool install --global AxonFlow --source ./artifacts --version 0.2.0
```

`--source ./artifacts` **replaces** every configured feed for that command, so your user or Visual Studio [`NuGet.Config`](https://learn.microsoft.com/nuget/consume-packages/configuring-nuget-behavior) (including misconfigured private feeds) is not consulted. If you prefer to keep merging all sources, use `--add-source ./artifacts` and add **`--ignore-failed-sources`** when a corporate feed fails.

Then **`axonflow`** works from any directory (for example **`axonflow dashboard`**). Upgrade later with **`dotnet tool update --global AxonFlow --source ./artifacts`** after packing a newer version, or install from NuGet if the package is published.

**Visual Studio / corporate feeds:** Global tools are installed with **`dotnet tool`**, not the VS “Manage NuGet Packages” UI. If restore or pack inside this repo still hits a bad private feed, the repo includes a root **[`NuGet.Config`](NuGet.Config)** that limits package sources to **nuget.org** for this tree only (delete or edit that file if your org requires a different mirror).

**Quick setup from a clone** (packs into `./artifacts` and installs or updates the global tool; reads **`Version`** from [`src/AxonFlow/AxonFlow.csproj`](src/AxonFlow/AxonFlow.csproj)):

```powershell
.\scripts\install-global.ps1
```

From **`scripts\`**, use **`.\install-global.ps1`** (PowerShell requires the **`.\`** prefix to run a script in the current directory; `install-global.ps1` alone will not resolve).

If you see **“not digitally signed”** / execution policy errors, either run the batch wrapper (no policy change): **`scripts\install-global.cmd`** from the repo root, or invoke PowerShell once with bypass:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-global.ps1
```

To allow local scripts for your user account (optional): **`Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned`** — see [about_Execution_Policies](https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_execution_policies).

```bash
chmod +x scripts/install-global.sh
./scripts/install-global.sh
```

Ensure **`%USERPROFILE%\.dotnet\tools`** (Windows) or **`~/.dotnet/tools`** (macOS/Linux) is on your **`PATH`** (the .NET SDK installer usually adds it).

Use **`axonflow dashboard`** (**no subcommands**) to serve the dashboard on loopback (**`http://127.0.0.1:5057`**); the UI refreshes from **`/api/snapshot`** every **120 seconds** and includes an **all-projects** picker. **`--json`** suppresses opening a browser while the server runs.

### Global options (most commands)

| Option | Default | Description |
|--------|---------|-------------|
| `--db` | `~/.axonflow/axonflow.db` | SQLite file path |
| `--project` | *inferred from cwd folder name* | Project slug; pass explicitly when pinning (e.g. **`default`** in scripts) |
| `--json` | off | Machine-readable stdout |
| `--dry-run` | off | Validate / plan writes without committing (where supported) |

## Commands (overview)

- `schema` — CLI and embedded DB schema version
- `init` — explicitly create DB, apply migrations, ensure default **`default`** project (optional; ordinary commands bootstrap for you)
- `project add|list|set-name` — **`set-name`** updates the display **`name`** for a **`--slug`** (slug and `AF-*` refs stay the same; use when the default project should read e.g. **axonflow** in the dashboard instead of **Default**)
- `item add|update|show|list|import|start|next|complete|cancel|reopen|note add|defer` — **`list`** supports **`--assigned-to`**, **`--body-contains`**, **`--updated-after`** (ISO-8601, UTC), plus **`--status`**, **`--type`**, **`--parent`**, **`--stream`**, **`--title-contains`**, **`--ref-prefix`**, **`--sort`**, **`--limit`**; **`update`** accepts **`--ref`** or **`--id`** and can set **`--body`**, **`--body-file`**, or **`--clear-body`**
- `dep add|remove`
- `tree`, `board`, `validate`, `export`
- `dashboard` — loopback (**`127.0.0.1:5057`**) read-only Kanban **+** hierarchy pages; **`GET /api/snapshot`** and **`GET /api/item`** live data; caches bootstrap HTML under **`~/.axonflow/dashboard-cache`**; **`--project`** selects the dashboard’s initial picker focus (all projects are always selectable)

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

## Phase 2 — HTML dashboard (read-only)

**`axonflow dashboard`** starts a **loopback-only** Kestrel host (default **`http://127.0.0.1:5057`**) and serves:

- **`index.html`** — Kanban board + project picker (always **all projects** in the DB).
- **`tree.html`** — hierarchy view with the same live data.
- **`mindmap.html`** — tiny redirect stub to **`tree.html`** for old bookmarks.

Bootstrap assets are written under **`%USERPROFILE%\.axonflow\dashboard-cache`** (or **`~/.axonflow/dashboard-cache`**) so you do not manage an output folder. The browser **polls** **`GET /api/snapshot?allProjects=1`** every **120 seconds**; item detail uses **`GET /api/item`**. Stop with **`Ctrl+C`**. With **`--json`**, the default browser is not opened (error output still respects JSON when applicable).

```bash
dotnet run --project src/AxonFlow -- dashboard
```
