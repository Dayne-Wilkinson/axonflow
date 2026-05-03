-- AxonFlow schema v1
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS schema_migrations (
  version INTEGER PRIMARY KEY NOT NULL,
  applied_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS projects (
  id TEXT PRIMARY KEY NOT NULL,
  name TEXT NOT NULL,
  slug TEXT NOT NULL UNIQUE,
  ref_prefix TEXT NOT NULL DEFAULT 'AF',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS work_items (
  id TEXT PRIMARY KEY NOT NULL,
  project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  ref_number INTEGER NOT NULL,
  client_key TEXT,
  path_hints TEXT,
  type TEXT NOT NULL CHECK (type IN ('epic','feature','story','task','bug','chore','spike')),
  stream TEXT NOT NULL DEFAULT 'plan' CHECK (stream IN ('plan','emergent')),
  discovered_from_work_item_id TEXT REFERENCES work_items(id) ON DELETE SET NULL,
  snoozed_until TEXT,
  title TEXT NOT NULL,
  body TEXT,
  status TEXT NOT NULL CHECK (status IN ('backlog','ready','in_progress','blocked','done','cancelled')),
  priority INTEGER NOT NULL DEFAULT 100,
  parent_id TEXT REFERENCES work_items(id) ON DELETE RESTRICT,
  blocked_by_work_item_id TEXT REFERENCES work_items(id) ON DELETE SET NULL,
  blocked_reason TEXT,
  external_ref TEXT,
  assigned_to TEXT,
  sort_order INTEGER NOT NULL DEFAULT 0,
  completed_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE(project_id, ref_number)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_work_items_project_client_key
  ON work_items(project_id, client_key) WHERE client_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_work_items_project_status ON work_items(project_id, status);
CREATE INDEX IF NOT EXISTS ix_work_items_project_parent ON work_items(project_id, parent_id);
CREATE INDEX IF NOT EXISTS ix_work_items_project_priority ON work_items(project_id, priority, sort_order);
CREATE INDEX IF NOT EXISTS ix_work_items_project_type ON work_items(project_id, type);
CREATE INDEX IF NOT EXISTS ix_work_items_project_stream ON work_items(project_id, stream);
CREATE INDEX IF NOT EXISTS ix_work_items_title ON work_items(title);

CREATE TABLE IF NOT EXISTS work_item_dependencies (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  predecessor_id TEXT NOT NULL REFERENCES work_items(id) ON DELETE CASCADE,
  successor_id TEXT NOT NULL REFERENCES work_items(id) ON DELETE CASCADE,
  kind TEXT NOT NULL DEFAULT 'finish_start' CHECK (kind IN ('finish_start','relates')),
  UNIQUE(predecessor_id, successor_id, kind)
);

CREATE INDEX IF NOT EXISTS ix_deps_successor ON work_item_dependencies(successor_id);
CREATE INDEX IF NOT EXISTS ix_deps_predecessor ON work_item_dependencies(predecessor_id);

CREATE TABLE IF NOT EXISTS work_item_notes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
  work_item_id TEXT NOT NULL REFERENCES work_items(id) ON DELETE CASCADE,
  at TEXT NOT NULL,
  actor TEXT,
  body TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_notes_item ON work_item_notes(work_item_id, id DESC);
