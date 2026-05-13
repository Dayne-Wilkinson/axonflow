using System.Net;
using System.Text;

namespace AxonFlow;

internal static partial class HandlersDashboard
{
    /// <summary>Read-only hierarchy table: same snapshot bootstrap as the board, detail popup parity, v1 + v2 bundle.</summary>
    private static string BuildTreeViewHtml(string snapshotJson, int refreshSeconds, string pageTitle, string refScopeHint, bool multiProject, DashboardServedConfig? served)
    {
        var esc = snapshotJson.Replace("</script>", "<\\/script>", StringComparison.Ordinal);
        var titleEnc = WebUtility.HtmlEncode(pageTitle);
        var metaRefresh = served is null
            ? $"""  <meta http-equiv="refresh" content="{refreshSeconds}"/>"""
            : "";
        var footerBody = served is not null
            ? $"<p>Live tree from <code>/api/snapshot</code> (poll every <strong>{served.Value.PollSeconds}</strong>s). Board: <a href=\"index.html\">index.html</a>. Restart <code>axonflow dashboard</code> to refresh the HTML files written to your dashboard cache directory.</p>"
            : (multiProject
                ? $"<p>Tree view (multi-project). Use the project picker. Page reloads every <strong>{refreshSeconds}</strong>s. Board: <a href=\"index.html\">index.html</a>.</p>"
                : $"<p>Tree view · {WebUtility.HtmlEncode(refScopeHint)}. Reload every <strong>{refreshSeconds}</strong>s. Board: <a href=\"index.html\">index.html</a>.</p>");

        var head = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1"/>
__META_REFRESH__
  <title>AxonFlow — __TITLE__ · tree view</title>
  <style>
    :root {
      --bg: #0f1419;
      --panel: #1a2332;
      --text: #e6edf3;
      --muted: #8b9cb3;
      --border: #2d3a4d;
      --plan: #3d8bfd;
      --emergent: #d29922;
      --done: #3fb950;
      --blocked: #f85149;
    }
    * { box-sizing: border-box; }
    body { margin: 0; font-family: ui-sans-serif, system-ui, sans-serif; background: var(--bg); color: var(--text); min-height: 100vh; }
    header {
      padding: 1rem 1.25rem; border-bottom: 1px solid var(--border);
      display: flex; flex-wrap: wrap; gap: 0.75rem; align-items: baseline;
      justify-content: space-between; background: var(--panel);
    }
    header h1 { margin: 0; font-size: 1.1rem; font-weight: 600; }
    .nav-links { margin-top: 0.35rem; font-size: 0.85rem; }
    .nav-links a { color: var(--plan); text-decoration: none; }
    .nav-links a:hover { text-decoration: underline; }
    header .meta { color: var(--muted); font-size: 0.85rem; }
    .project-row {
      display: none; align-items: center; gap: 0.5rem; width: 100%;
      flex-basis: 100%; margin-top: 0.25rem;
    }
    .project-row label { font-size: 0.8rem; color: var(--muted); }
    #project-select {
      flex: 1; max-width: 28rem; padding: 0.35rem 0.5rem; border-radius: 6px;
      border: 1px solid var(--border); background: var(--bg); color: var(--text); font-size: 0.85rem;
    }
    .counts { display: flex; gap: 1rem; flex-wrap: wrap; font-size: 0.8rem; }
    .counts span { color: var(--muted); }
    .counts strong { color: var(--text); }
    main { padding: 1rem; display: flex; flex-direction: column; gap: 0.75rem; }
    #tree-scroll {
      overflow: auto; max-height: min(78vh, 820px);
      border: 1px solid var(--border); border-radius: 8px; background: #0b0e12;
    }
    #tree-table { width: 100%; border-collapse: collapse; font-size: 0.82rem; }
    #tree-table th {
      text-align: left; padding: 0.55rem 0.65rem; background: var(--panel);
      border-bottom: 1px solid var(--border); color: var(--muted); font-weight: 600; position: sticky; top: 0; z-index: 1;
    }
    #tree-table td { padding: 0.45rem 0.65rem; border-bottom: 1px solid #2d3a4d; vertical-align: top; }
    tr.tree-row { cursor: pointer; }
    tr.tree-row:hover { background: #161b22; }
    tr.tree-row:focus { outline: 2px solid var(--plan); outline-offset: -2px; }
    tr.tree-row.selected { background: #1c2430; }
    td.refcell { font-weight: 600; color: var(--plan); white-space: nowrap; }
    td.cell-type, td.cell-status { white-space: nowrap; }
    td.dep { color: var(--muted); font-size: 0.76rem; white-space: nowrap; }
    #tree-table .af-chip {
      display: inline-block; font-size: 0.68rem; font-weight: 600; line-height: 1.25;
      padding: 0.14rem 0.45rem; border-radius: 999px; border: 1px solid transparent;
      max-width: 12rem; overflow: hidden; text-overflow: ellipsis; vertical-align: middle;
    }
    #tree-table .af-chip-type {
      border-radius: 4px; padding-left: 0.35rem; text-transform: lowercase; color: var(--text);
      background: rgba(45, 58, 77, 0.55);
    }
    #tree-table .af-chip-type.af-type-epic { border-left: 3px solid #f778ba; }
    #tree-table .af-chip-type.af-type-feature { border-left: 3px solid #79c0ff; }
    #tree-table .af-chip-type.af-type-story { border-left: 3px solid #56d364; }
    #tree-table .af-chip-type.af-type-task { border-left: 3px solid #d2a8ff; }
    #tree-table .af-chip-type.af-type-bug { border-left: 3px solid #f85149; }
    #tree-table .af-chip-type.af-type-chore { border-left: 3px solid #8b949e; }
    #tree-table .af-chip-type.af-type-spike { border-left: 3px solid #ffa657; }
    #tree-table .af-chip-type.af-type-other { border-left: 3px solid var(--muted); color: var(--muted); }
    #tree-table .af-chip-status { text-transform: none; }
    #tree-table .af-chip-status.af-st-backlog {
      color: var(--muted); border-color: #3d4f66; background: rgba(139, 156, 179, 0.1);
    }
    #tree-table .af-chip-status.af-st-ready {
      color: #79c0ff; border-color: rgba(61, 139, 253, 0.55); background: rgba(61, 139, 253, 0.12);
    }
    #tree-table .af-chip-status.af-st-in_progress {
      color: var(--text); border-color: var(--plan); background: rgba(61, 139, 253, 0.2);
    }
    #tree-table .af-chip-status.af-st-blocked {
      color: #ffa198; border-color: rgba(248, 81, 73, 0.65); background: rgba(248, 81, 73, 0.12);
    }
    #tree-table .af-chip-status.af-st-done {
      color: #6ef7a4; border-color: rgba(63, 185, 80, 0.55); background: rgba(63, 185, 80, 0.12);
    }
    #tree-table .af-chip-status.af-st-cancelled {
      color: #8b949e; border-style: dashed; border-color: #6e7681; background: rgba(110, 118, 129, 0.1);
      text-decoration: line-through; text-decoration-thickness: 1px;
    }
    #tree-table .af-chip-status.af-st-unknown {
      color: var(--muted); border-color: var(--border); background: rgba(45, 58, 77, 0.35); font-weight: 500;
    }
    #tree-table .af-chip-status.af-snooze-flag { box-shadow: 0 0 0 1px rgba(163, 113, 247, 0.55); }
    .type-legend { font-size: 0.72rem; color: var(--muted); margin-top: 0.35rem; max-width: 52rem; line-height: 1.35; }
    .detail-overlay { position: fixed; inset: 0; z-index: 1000; }
    .detail-overlay[hidden] { display: none !important; }
    .detail-backdrop { position: absolute; inset: 0; background: rgba(0, 0, 0, 0.55); }
    .detail-dialog {
      position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%);
      width: min(960px, calc(100vw - 2rem)); max-height: min(85vh, 900px);
      background: var(--panel); border: 1px solid var(--border); border-radius: 10px;
      padding: 1rem 1rem 0.75rem; display: flex; flex-direction: column; gap: 0.5rem;
      box-shadow: 0 12px 40px rgba(0, 0, 0, 0.45);
    }
    #detail-close {
      position: absolute; top: 0.5rem; right: 0.5rem;
      background: var(--bg); color: var(--text); border: 1px solid var(--border);
      border-radius: 6px; width: 2.25rem; height: 2.25rem; font-size: 1.25rem;
      cursor: pointer; line-height: 1; padding: 0;
    }
    #detail-close:hover { border-color: #4a5f7a; }
    #detail-popup-title { margin: 0; padding-right: 3rem; font-size: 1rem; }
    #detail-popup-body {
      margin: 0; overflow: auto; white-space: pre-wrap; word-break: break-word;
      font-size: 0.78rem; color: var(--muted); flex: 1; min-height: 0;
    }
    footer { padding: 0.75rem 1.25rem; color: var(--muted); font-size: 0.75rem; border-top: 1px solid var(--border); }
    footer a { color: var(--plan); }
    #tree-empty { padding: 2rem; color: var(--muted); text-align: center; display: none; }
  </style>
</head>
<body>
  <header>
    <div>
      <h1>Tree view</h1>
      <nav class="nav-links" aria-label="Page navigation"><a href="index.html">Board</a></nav>
      <div class="meta" id="hdr-meta"></div>
      <div class="type-legend" id="type-legend">
        Types use the left stripe colors (same idea as the board). Status chips:
        <strong>Backlog</strong> · <strong>Ready</strong> · <strong>In progress</strong> · <strong>Blocked</strong> ·
        <strong>Done</strong> (closed) · <strong>Cancelled</strong> (dashed, struck). Snoozed rows add a violet outline; unknown statuses use a neutral chip.
      </div>
      <div class="project-row" id="project-row">
        <label for="project-select">Project</label>
        <select id="project-select" aria-label="Select project"></select>
      </div>
    </div>
    <div class="counts" id="counts"></div>
  </header>
  <main>
    <p id="tree-empty">No items in this project snapshot.</p>
    <div id="tree-scroll" aria-label="Work item tree">
      <table id="tree-table" class="tree-table" role="tree" aria-label="Work items by parent hierarchy" hidden>
        <thead>
          <tr><th scope="col">Ref</th><th scope="col">Type</th><th scope="col">Status</th><th scope="col">Title</th><th scope="col">Deps</th></tr>
        </thead>
        <tbody id="tree-body"></tbody>
      </table>
    </div>
  </main>
  <div id="detail-overlay" class="detail-overlay" hidden aria-hidden="true">
    <div class="detail-backdrop" id="detail-backdrop"></div>
    <div id="detail-dialog" class="detail-dialog" role="dialog" aria-modal="true" aria-labelledby="detail-popup-title" tabindex="-1">
      <button type="button" id="detail-close" aria-label="Close details">×</button>
      <h3 id="detail-popup-title"></h3>
      <pre id="detail-popup-body"></pre>
    </div>
  </div>
  <footer>
__FOOTER__
  </footer>
  <script type="application/json" id="af-snapshot">
""".Replace("__META_REFRESH__", metaRefresh, StringComparison.Ordinal)
            .Replace("__TITLE__", titleEnc, StringComparison.Ordinal)
            .Replace("__FOOTER__", footerBody, StringComparison.Ordinal);

        const string head2 = """
  </script>
  <script>
(function () {
  const el = document.getElementById('af-snapshot');
  let bootstrapData;
  try {
    bootstrapData = JSON.parse(el.textContent);
  } catch (e) {
    document.body.innerHTML = '<p style="padding:2rem">Invalid snapshot JSON.</p>';
    return;
  }

  const served = !!(bootstrapData && bootstrapData.__served);
  let data;
  async function loadSnapshotFromApi() {
    const u = new URL('/api/snapshot', location.origin);
    if (bootstrapData.allProjects) u.searchParams.set('allProjects', '1');
    else u.searchParams.set('project', currentItemProject());
    const res = await fetch(u);
    if (!res.ok) throw new Error('snapshot HTTP ' + res.status);
    return res.json();
  }

  const counts = document.getElementById('counts');
  const projectRow = document.getElementById('project-row');
  const projectSelect = document.getElementById('project-select');
  const detailOverlay = document.getElementById('detail-overlay');
  const detailClose = document.getElementById('detail-close');
  const detailBackdrop = document.getElementById('detail-backdrop');
  const detailPopupTitle = document.getElementById('detail-popup-title');
  const detailPopupBody = document.getElementById('detail-popup-body');
  const treeBody = document.getElementById('tree-body');
  const treeTable = document.getElementById('tree-table');
  const treeEmpty = document.getElementById('tree-empty');

  let currentSlug = null;
  let lastFocusedRow = null;
  let detailUiBound = false;
  let multiSelectBound = false;

  function closeDetailPopup() {
    detailOverlay.hidden = true;
    detailOverlay.setAttribute('aria-hidden', 'true');
    if (lastFocusedRow && typeof lastFocusedRow.focus === 'function') {
      try { lastFocusedRow.focus(); } catch (e) { /* ignore */ }
    }
  }
  function openDetailPopup() {
    detailOverlay.hidden = false;
    detailOverlay.setAttribute('aria-hidden', 'false');
    try { detailClose.focus(); } catch (e) { /* ignore */ }
  }
  function bindDetailUiOnce() {
    if (detailUiBound) return;
    detailUiBound = true;
    detailClose.addEventListener('click', function () { closeDetailPopup(); });
    detailBackdrop.addEventListener('click', function () { closeDetailPopup(); });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && !detailOverlay.hidden) {
        e.preventDefault();
        closeDetailPopup();
      }
    });
  }
  bindDetailUiOnce();

  function currentItemProject() {
    if (data && data.schemaVersion >= 2 && data.itemsByProjectSlug && data.projects) {
      return currentSlug || data.defaultProjectSlug || (data.projects[0] && data.projects[0].slug) || bootstrapData.defaultProject || 'default';
    }
    if (data && data.project && data.project.slug) return data.project.slug;
    return bootstrapData.defaultProject || 'default';
  }

  function esc(s) {
    if (s == null) return '';
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
  }

  function normToken(s) {
    return String(s == null ? '' : s).trim().toLowerCase().replace(/\s+/g, '_');
  }

  /** Maps ItemDto.status + snoozedUntil to chip label & CSS suffix (parity with board column names). */
  function statusPresentation(it) {
    let raw = normToken(it.status);
    if (raw === 'active' || raw === 'inprogress' || raw === 'wip') raw = 'in_progress';
    else if (raw === 'complete' || raw === 'completed') raw = 'done';
    else if (raw === 'cancel' || raw === 'canceled') raw = 'cancelled';
    let label;
    let cls;
    let bucket = 'open';
    if (raw === 'done') {
      label = 'Done'; cls = 'af-st-done'; bucket = 'terminal';
    } else if (raw === 'cancelled') {
      label = 'Cancelled'; cls = 'af-st-cancelled'; bucket = 'terminal';
    } else if (raw === 'blocked') {
      label = 'Blocked'; cls = 'af-st-blocked'; bucket = 'blocked';
    } else if (raw === 'in_progress') {
      label = 'In progress'; cls = 'af-st-in_progress';
    } else if (raw === 'ready') {
      label = 'Ready'; cls = 'af-st-ready';
    } else if (raw === 'backlog') {
      label = 'Backlog'; cls = 'af-st-backlog';
    } else if (raw) {
      cls = 'af-st-unknown';
      label = String(it.status).replace(/_/g, ' ');
    } else {
      cls = 'af-st-unknown';
      label = 'Unknown';
    }
    const terminal = bucket === 'terminal';
    let snooze = !!(it.snoozedUntil && !terminal);
    let extra = '';
    if (snooze) extra = ' · snoozed';
    return {
      label: label + extra,
      className: 'af-chip af-chip-status ' + cls + (snooze ? ' af-snooze-flag' : ''),
      bucket: bucket
    };
  }

  function typePresentation(it) {
    const tp = normToken(it.type) || 'task';
    const map = {
      epic: 'af-type-epic', feature: 'af-type-feature', story: 'af-type-story', task: 'af-type-task',
      bug: 'af-type-bug', chore: 'af-type-chore', spike: 'af-type-spike'
    };
    const cls = map[tp] || 'af-type-other';
    const label = (it.type || 'task').toLowerCase();
    return { label: label, className: 'af-chip af-chip-type ' + cls };
  }

  function truncateAria(s, n) {
    const t = String(s == null ? '' : s);
    if (t.length <= n) return t;
    return t.slice(0, Math.max(0, n - 1)) + '…';
  }

  function pickBundle() {
    if (data.schemaVersion >= 2 && data.itemsByProjectSlug && data.projects) {
      const b = data.itemsByProjectSlug[currentSlug] || { items: [], dependencies: [] };
      return { items: b.items || [], deps: b.dependencies || [] };
    }
    return { items: data.items || [], deps: data.dependencies || [] };
  }

  function buildGraph(items, deps) {
    const nodes = new Map();
    const refToId = new Map();
    for (const it of items) {
      if (!it || !it.id) continue;
      nodes.set(it.id, it);
      if (it.ref) refToId.set(it.ref, it.id);
    }
    const parentEdges = [];
    for (const it of items) {
      if (!it || !it.id) continue;
      if (it.parentId && nodes.has(it.parentId)) {
        parentEdges.push({ parentId: it.parentId, childId: it.id });
      }
    }
    const depEdges = [];
    for (const d of deps) {
      if (!d) continue;
      const a = refToId.get(d.predecessorRef);
      const b = refToId.get(d.successorRef);
      if (a && b) depEdges.push({ fromId: a, toId: b, kind: d.kind || 'finish_start' });
    }
    const incoming = new Set(parentEdges.map(function (e) { return e.childId; }));
    const roots = [];
    for (const it of items) {
      if (!it || !it.id) continue;
      if (!incoming.has(it.id)) roots.push(it.id);
    }
    if (!roots.length && nodes.size) {
      for (const id of nodes.keys()) roots.push(id);
      roots.sort(function (a, b) {
        return ((nodes.get(a) || {}).ref || '').localeCompare((nodes.get(b) || {}).ref || '');
      });
    }
    return { nodes, parentEdges, depEdges, roots };
  }

  function countDepsFor(id, depEdges) {
    var n = 0;
    for (var i = 0; i < depEdges.length; i++) {
      var e = depEdges[i];
      if (e.fromId === id || e.toId === id) n++;
    }
    return n;
  }

  function dfsRows(graph) {
    const nodes = graph.nodes;
    const parentEdges = graph.parentEdges;
    const depEdges = graph.depEdges;
    const children = new Map();
    for (const e of parentEdges) {
      if (!children.has(e.parentId)) children.set(e.parentId, []);
      children.get(e.parentId).push(e.childId);
    }
    children.forEach(function (arr) {
      arr.sort(function (a, b) {
        const ra = (nodes.get(a) || {}).ref || '';
        const rb = (nodes.get(b) || {}).ref || '';
        return ra.localeCompare(rb);
      });
    });
    const rows = [];
    const visited = new Set();
    function visit(id, depth) {
      if (visited.has(id)) return;
      visited.add(id);
      const it = nodes.get(id);
      if (!it) return;
      rows.push({ it: it, depth: depth, depN: countDepsFor(id, depEdges) });
      const ch = children.get(id) || [];
      for (let i = 0; i < ch.length; i++) visit(ch[i], depth + 1);
    }
    const rts = graph.roots.slice().sort(function (a, b) {
      const ra = (nodes.get(a) || {}).ref || '';
      const rb = (nodes.get(b) || {}).ref || '';
      return ra.localeCompare(rb);
    });
    for (let i = 0; i < rts.length; i++) visit(rts[i], 0);
    return rows;
  }

  async function showDetail(it, depsList) {
    if (!it) { closeDetailPopup(); return; }
    const slugLine = it.projectSlug ? ('[' + it.projectSlug + '] ') : '';
    detailPopupTitle.textContent = slugLine + (it.type || '') + ' ' + it.ref + ' — ' + it.title;
    openDetailPopup();
    if (served && it.ref) {
      detailPopupBody.textContent = 'Loading…';
      try {
        const u = new URL('/api/item', location.origin);
        u.searchParams.set('ref', it.ref);
        u.searchParams.set('project', currentItemProject());
        const res = await fetch(u);
        if (res.ok) {
          const env = await res.json();
          detailPopupBody.textContent = JSON.stringify(env, null, 2);
          return;
        }
      } catch (e) { /* fall through */ }
    }
    const preds = (depsList || []).filter(function (d) { return d.successorRef === it.ref; });
    detailPopupBody.textContent = JSON.stringify({ item: it, predecessorDependencies: preds }, null, 2);
  }

  function renderTree() {
    const bundle = pickBundle();
    const items = bundle.items;
    const deps = bundle.deps;
    const graph = buildGraph(items, deps);
    const rows = dfsRows(graph);
    const open = items.filter(function (i) { return i.status !== 'done' && i.status !== 'cancelled'; }).length;
    counts.innerHTML = '<span>Rows <strong>' + rows.length + '</strong></span><span>Open <strong>' + open + '</strong></span>';

    if (!items.length) {
      treeEmpty.style.display = 'block';
      treeTable.hidden = true;
      return;
    }
    treeEmpty.style.display = 'none';
    treeTable.hidden = false;
    treeBody.innerHTML = '';
    for (const row of rows) {
      const it = row.it;
      const tr = document.createElement('tr');
      tr.className = 'tree-row';
      tr.setAttribute('role', 'treeitem');
      tr.setAttribute('aria-level', String(row.depth + 1));
      tr.tabIndex = 0;
      tr.setAttribute('data-id', it.id);
      tr.setAttribute('data-ref', it.ref || '');
      const pad = (row.depth * 18) + 'px';
      const depLabel = row.depN ? (String(row.depN) + ' link' + (row.depN === 1 ? '' : 's')) : '';
      const title = (it.title || '').length > 120 ? (it.title || '').slice(0, 118) + '…' : (it.title || '');
      const st = statusPresentation(it);
      const ty = typePresentation(it);
      const ariaBits = [
        it.ref || '',
        'type ' + ty.label,
        'status ' + st.label,
        truncateAria(it.title || '', 100)
      ];
      const ariaRow = ariaBits.filter(Boolean).join(', ');
      tr.setAttribute('aria-label', ariaRow);
      tr.innerHTML =
        '<td class="refcell" style="padding-left:calc(0.65rem + ' + pad + ')">' + esc(it.ref) + '</td>' +
        '<td class="cell-type">' +
          '<span class="' + ty.className + '" aria-label="Type: ' + esc(ty.label) + '">' + esc(ty.label) + '</span>' +
        '</td>' +
        '<td class="cell-status">' +
          '<span class="' + st.className + '" aria-label="Status: ' + esc(st.label) + '">' + esc(st.label) + '</span>' +
        '</td>' +
        '<td>' + esc(title) + '</td>' +
        '<td class="dep">' + esc(depLabel) + '</td>';
      tr.addEventListener('click', function (ev) {
        ev.stopPropagation();
        document.querySelectorAll('tr.tree-row').forEach(function (x) { x.classList.remove('selected'); });
        tr.classList.add('selected');
        lastFocusedRow = tr;
        void showDetail(it, deps);
      });
      tr.addEventListener('keydown', function (ev) {
        if (ev.key === 'Enter' || ev.key === ' ') {
          ev.preventDefault();
          tr.click();
        }
      });
      treeBody.appendChild(tr);
    }
  }

  function updateHdrMulti() {
    const p = (data.projects || []).find(function (x) { return x.slug === currentSlug; });
    const rp = (p && p.refPrefix) || '';
    const projLabel = (p && (p.name || p.slug)) || currentSlug;
    document.getElementById('hdr-meta').textContent =
      'Project: ' + projLabel + ' · Generated: ' + (data.generatedAt || '') + ' · ' + rp + '-*';
  }
  function updateHdrLegacy() {
    const pfx = (data.project && data.project.refPrefix) || 'AF';
    const projLabel = (data.project && (data.project.name || data.project.slug)) || '';
    document.getElementById('hdr-meta').textContent =
      'Project: ' + projLabel + ' · Generated: ' + (data.generatedAt || '') + ' · ' + pfx + '-*';
  }

  function renderMulti() {
    projectRow.style.display = 'flex';
    if (!currentSlug || !data.itemsByProjectSlug[currentSlug]) {
      currentSlug = data.defaultProjectSlug;
      if (!data.itemsByProjectSlug[currentSlug] && data.projects && data.projects.length) {
        currentSlug = data.projects[0].slug;
      }
    }
    function fillSelect() {
      const prev = currentSlug;
      projectSelect.innerHTML = '';
      for (const p of data.projects || []) {
        const opt = document.createElement('option');
        opt.value = p.slug;
        opt.textContent = p.name + ' (' + p.slug + ', ' + p.refPrefix + ')';
        projectSelect.appendChild(opt);
      }
      if (data.itemsByProjectSlug[prev]) currentSlug = prev;
      projectSelect.value = currentSlug;
    }
    fillSelect();
    updateHdrMulti();
    renderTree();
    if (!multiSelectBound) {
      projectSelect.addEventListener('change', function () {
        currentSlug = projectSelect.value;
        closeDetailPopup();
        updateHdrMulti();
        renderTree();
      });
      multiSelectBound = true;
    }
  }

  function renderLegacy() {
    projectRow.style.display = 'none';
    currentSlug = null;
    updateHdrLegacy();
    renderTree();
  }

  function rerenderAll() {
    if (data.schemaVersion >= 2 && data.itemsByProjectSlug && data.projects) {
      if (!data.itemsByProjectSlug[currentSlug]) {
        currentSlug = data.defaultProjectSlug;
        if (!data.itemsByProjectSlug[currentSlug] && data.projects && data.projects.length) {
          currentSlug = data.projects[0].slug;
        }
      }
      renderMulti();
    } else {
      renderLegacy();
    }
  }

  (async function bootstrap() {
    try {
      if (served) {
        data = await loadSnapshotFromApi();
        window.__axonflowReload = async function () {
          data = await loadSnapshotFromApi();
          rerenderAll();
        };
        setInterval(function () {
          window.__axonflowReload().catch(function (e) { console.error(e); });
        }, (bootstrapData.pollSeconds || 120) * 1000);
      } else {
        data = bootstrapData;
      }
      if (data.schemaVersion >= 2 && data.itemsByProjectSlug && data.projects) {
        renderMulti();
      } else {
        renderLegacy();
      }
    } catch (e) {
      document.body.innerHTML = '<p style="padding:2rem">Could not load tree: ' + esc(e.message) + '</p>';
    }
  })();
})();
  </script>
</body>
</html>
""";

        return head + esc + head2;
    }
}
