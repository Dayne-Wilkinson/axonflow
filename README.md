# AxonFlow

Cross-platform **.NET 9** CLI for a **SQLite-backed work graph**: epics through bugs, **dependencies**, **blockers**, **emergent** work with provenance, **notes**, and **JSON-first** output for coding agents. Humans get plain text and ASCII **board**/**tree** views.

**Repository:** [github.com/Dayne-Wilkinson/axonflow](https://github.com/Dayne-Wilkinson/axonflow)

## Quick start

```bash
dotnet run --project src/AxonFlow -- init
dotnet run --project src/AxonFlow -- item add --type task --title "First task" --json
dotnet run --project src/AxonFlow -- item next --json
```

Default database: `.axonflow/axonflow.db` under the current working directory (override with `--db`).

Global options (most commands):

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

## Agent workflow (short)

1. `init` once per clone (or CI) with an explicit `--db` under the repo.
2. Seed plans with `item import` (JSON) using `clientKey` and `tempId` for idempotency; use `--dry-run` on large imports.
3. `item next --json` — read `picked` and `excluded` reasons.
4. `item start --assignee agent:<name> --ref AF-12` before editing (multi-agent).
5. Log progress with `item note add`; file emergent work with `--stream emergent --discovered-from <ref>`.
6. `validate` before end of session.

See [.cursor/skills/axonflow/SKILL.md](.cursor/skills/axonflow/SKILL.md) for the full Cursor skill.

## Build & test

```bash
dotnet build AxonFlow.sln
dotnet test AxonFlow.sln
```

## Phase 2

Read-only local **HTML dashboard** (same SQLite schema) is planned; not implemented in this milestone.
