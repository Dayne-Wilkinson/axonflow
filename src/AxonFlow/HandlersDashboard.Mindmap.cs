using System.Net;
using System.Text;

namespace AxonFlow;

internal static partial class HandlersDashboard
{
    /// <summary>Static mind map page: same snapshot bootstrap as the board, hierarchical layout + dependency overlay, pan/zoom, detail popup parity.</summary>
    private static string BuildMindmapHtml(string snapshotJson, int refreshSeconds, string pageTitle, string refScopeHint, bool multiProject, DashboardServedConfig? served)
    {
        var esc = snapshotJson.Replace("</script>", "<\\/script>", StringComparison.Ordinal);
        var titleEnc = WebUtility.HtmlEncode(pageTitle);
        var metaRefresh = served is null
            ? $"""  <meta http-equiv="refresh" content="{refreshSeconds}"/>"""
            : "";
        var footerBody = served is not null
            ? $"<p>Live mind map from <code>/api/snapshot</code> (poll every <strong>{served.Value.PollSeconds}</strong>s). Board: <a href=\"index.html\">index.html</a>. Offline: <code>axonflow dashboard emit …</code>.</p>"
            : (multiProject
                ? $"<p>Mind map (multi-project). Use the project picker. Page reloads every <strong>{refreshSeconds}</strong>s. Board: <a href=\"index.html\">index.html</a>.</p>"
                : $"<p>Mind map · {WebUtility.HtmlEncode(refScopeHint)}. Reload every <strong>{refreshSeconds}</strong>s. Board: <a href=\"index.html\">index.html</a>.</p>");

        var head = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1"/>
__META_REFRESH__
  <title>AxonFlow — __TITLE__ · mind map</title>
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
    body {
      margin: 0; font-family: ui-sans-serif, system-ui, sans-serif;
      background: var(--bg); color: var(--text); min-height: 100vh;
    }
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
    main { padding: 1rem; display: flex; flex-direction: column; gap: 0.5rem; }
    #map-toolbar { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: center; }
    #map-toolbar button {
      padding: 0.35rem 0.6rem; border-radius: 6px; border: 1px solid var(--border);
      background: var(--bg); color: var(--text); cursor: pointer; font-size: 0.8rem;
    }
    #map-toolbar button:hover { border-color: #4a5f7a; }
    #map-viewport {
      position: relative; overflow: hidden; height: min(72vh, 720px);
      border: 1px solid var(--border); border-radius: 8px; background: #0b0e12;
      touch-action: none;
    }
    #map-pan-layer { transform-origin: 0 0; will-change: transform; }
    #map-svg { display: block; cursor: grab; min-width: 400px; min-height: 280px; }
    #map-svg:active { cursor: grabbing; }
    .mm-node { cursor: pointer; }
    .mm-node rect { fill: var(--panel); stroke: var(--border); rx: 6; }
    .mm-node:focus { outline: none; }
    .mm-node:focus rect { stroke: var(--plan); stroke-width: 2; }
    .mm-node text { fill: var(--text); font-size: 11px; pointer-events: none; }
    .mm-node .mm-ref { fill: var(--plan); font-weight: 600; font-size: 10px; }
    .mm-edge-tree { stroke: #5a6d85; stroke-width: 1.5; fill: none; }
    .mm-edge-dep { stroke: #79c0ff; stroke-width: 1.2; stroke-dasharray: 5 4; fill: none; opacity: 0.9; }
    .type-legend { font-size: 0.72rem; color: var(--muted); margin-top: 0.35rem; }
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
    #map-empty { padding: 2rem; color: var(--muted); text-align: center; display: none; }
  </style>
</head>
<body>
  <header>
    <div>
      <h1>Mind map</h1>
      <nav class="nav-links" aria-label="Page navigation"><a href="index.html">Board</a></nav>
      <div class="meta" id="hdr-meta"></div>
      <div class="type-legend" id="type-legend" hidden>Types use labels on nodes (color is secondary).</div>
      <div class="project-row" id="project-row">
        <label for="project-select">Project</label>
        <select id="project-select" aria-label="Select project"></select>
      </div>
    </div>
    <div class="counts" id="counts"></div>
  </header>
  <main>
    <p id="map-empty">No items in this project snapshot.</p>
    <div id="map-toolbar">
      <span style="color:var(--muted);font-size:0.8rem">Zoom: Ctrl + wheel on map</span>
      <button type="button" id="zoom-in" aria-label="Zoom in">+</button>
      <button type="button" id="zoom-out" aria-label="Zoom out">−</button>
      <button type="button" id="zoom-reset" aria-label="Reset pan and zoom">Reset view</button>
    </div>
    <div id="map-viewport" aria-label="Mind map canvas">
      <div id="map-pan-layer">
        <svg id="map-svg" xmlns="http://www.w3.org/2000/svg" width="1200" height="800"></svg>
      </div>
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
  const typeLegend = document.getElementById('type-legend');
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

  if (served) typeLegend.hidden = false;

  const counts = document.getElementById('counts');
  const projectRow = document.getElementById('project-row');
  const projectSelect = document.getElementById('project-select');
  const svg = document.getElementById('map-svg');
  const panLayer = document.getElementById('map-pan-layer');
  const viewport = document.getElementById('map-viewport');
  const mapEmpty = document.getElementById('map-empty');
  const detailOverlay = document.getElementById('detail-overlay');
  const detailDialog = document.getElementById('detail-dialog');
  const detailBackdrop = document.getElementById('detail-backdrop');
  const detailClose = document.getElementById('detail-close');
  const detailPopupTitle = document.getElementById('detail-popup-title');
  const detailPopupBody = document.getElementById('detail-popup-body');

  let currentSlug = null;
  let lastFocusedNode = null;
  let detailUiBound = false;
  let multiSelectBound = false;

  let panX = 0, panY = 0, scale = 1;
  const nodeW = 200, nodeH = 52, colGap = 260, rowGap = 72;

  function closeDetailPopup() {
    detailOverlay.hidden = true;
    detailOverlay.setAttribute('aria-hidden', 'true');
    if (lastFocusedNode && typeof lastFocusedNode.focus === 'function') {
      try { lastFocusedNode.focus(); } catch (e) { /* ignore */ }
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
    if (!roots.length && items.length) {
      for (const it of items) {
        if (it && it.id) roots.push(it.id);
      }
    }
    return { nodes, parentEdges, depEdges, roots };
  }

  function layoutPositions(graph) {
    const { nodes, parentEdges, roots } = graph;
    const children = new Map();
    for (const e of parentEdges) {
      if (!children.has(e.parentId)) children.set(e.parentId, []);
      children.get(e.parentId).push(e.childId);
    }
    for (const [, arr] of children) {
      arr.sort(function (a, b) {
        const ra = (nodes.get(a) || {}).ref || '';
        const rb = (nodes.get(b) || {}).ref || '';
        return ra.localeCompare(rb);
      });
    }
    const depth = new Map();
    const q = roots.slice().sort(function (a, b) {
      const ra = (nodes.get(a) || {}).ref || '';
      const rb = (nodes.get(b) || {}).ref || '';
      return ra.localeCompare(rb);
    });
    for (const id of q) depth.set(id, 0);
    for (let qi = 0; qi < q.length; qi++) {
      const id = q[qi];
      const d0 = depth.get(id) || 0;
      const ch = children.get(id) || [];
      for (const c of ch) {
        depth.set(c, Math.max(depth.get(c) || 0, d0 + 1));
        q.push(c);
      }
    }
    for (const id of nodes.keys()) {
      if (!depth.has(id)) depth.set(id, 0);
    }
    const byDepth = new Map();
    depth.forEach(function (d, id) {
      if (!byDepth.has(d)) byDepth.set(d, []);
      byDepth.get(d).push(id);
    });
    for (const [, arr] of byDepth) {
      arr.sort(function (a, b) {
        const ra = (nodes.get(a) || {}).ref || '';
        const rb = (nodes.get(b) || {}).ref || '';
        return ra.localeCompare(rb);
      });
    }
    const pos = new Map();
    let maxY = 0, maxX = 0;
    const dList = Array.from(byDepth.keys()).sort(function (a, b) { return a - b; });
    for (const d of dList) {
      const row = byDepth.get(d) || [];
      const x = 40 + d * colGap;
      let y = 40;
      for (const id of row) {
        pos.set(id, { x: x, y: y, w: nodeW, h: nodeH });
        maxX = Math.max(maxX, x + nodeW + 40);
        maxY = Math.max(maxY, y + nodeH + 40);
        y += rowGap;
      }
    }
    return { pos, size: { w: Math.max(600, maxX), h: Math.max(400, maxY) } };
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

  function applyPanZoom() {
    panLayer.style.transform = 'translate(' + panX + 'px,' + panY + 'px) scale(' + scale + ')';
  }

  function renderMap() {
    const { items, deps } = pickBundle();
    const graph = buildGraph(items, deps);
    const { pos, size } = layoutPositions(graph);
    svg.setAttribute('width', String(size.w));
    svg.setAttribute('height', String(size.h));
    svg.innerHTML = '';

    const open = items.filter(function (i) { return i.status !== 'done' && i.status !== 'cancelled'; }).length;
    counts.innerHTML = '<span>Nodes <strong>' + items.length + '</strong></span><span>Open <strong>' + open + '</strong></span>';

    if (!items.length) {
      mapEmpty.style.display = 'block';
      document.getElementById('map-toolbar').style.display = 'none';
      viewport.style.display = 'none';
      return;
    }
    mapEmpty.style.display = 'none';
    document.getElementById('map-toolbar').style.display = 'flex';
    viewport.style.display = 'block';

    const gEdges = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    gEdges.setAttribute('class', 'mm-edges');
    svg.appendChild(gEdges);

    for (const e of graph.parentEdges) {
      const pa = pos.get(e.parentId);
      const ca = pos.get(e.childId);
      if (!pa || !ca) continue;
      const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
      line.setAttribute('x1', String(pa.x + pa.w));
      line.setAttribute('y1', String(pa.y + pa.h / 2));
      line.setAttribute('x2', String(ca.x));
      line.setAttribute('y2', String(ca.y + ca.h / 2));
      line.setAttribute('class', 'mm-edge-tree');
      gEdges.appendChild(line);
    }

    for (const e of graph.depEdges) {
      const a = pos.get(e.fromId);
      const b = pos.get(e.toId);
      if (!a || !b) continue;
      const ax = a.x + a.w, ay = a.y + a.h / 2, bx = b.x, by = b.y + b.h / 2;
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      const mx = (ax + bx) / 2;
      const d = 'M ' + ax + ' ' + ay + ' C ' + mx + ' ' + ay + ', ' + mx + ' ' + by + ', ' + bx + ' ' + by;
      path.setAttribute('d', d);
      path.setAttribute('class', 'mm-edge-dep');
      gEdges.appendChild(path);
    }

    const gNodes = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    gNodes.setAttribute('class', 'mm-nodes');
    svg.appendChild(gNodes);

    for (const it of items) {
      const p = pos.get(it.id);
      if (!p) continue;
      const g = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      g.setAttribute('class', 'mm-node');
      g.setAttribute('transform', 'translate(' + p.x + ',' + p.y + ')');
      g.setAttribute('data-id', it.id);
      g.setAttribute('data-ref', it.ref || '');
      g.setAttribute('tabindex', '0');
      g.setAttribute('role', 'button');
      const st = (it.status || '').replace('_', ' ');
      const r = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
      r.setAttribute('width', String(nodeW));
      r.setAttribute('height', String(nodeH));
      const t1 = document.createElementNS('http://www.w3.org/2000/svg', 'text');
      t1.setAttribute('x', '8');
      t1.setAttribute('y', '16');
      t1.setAttribute('class', 'mm-ref');
      t1.textContent = (it.ref || '') + ' · ' + (it.type || '');
      const t2 = document.createElementNS('http://www.w3.org/2000/svg', 'text');
      t2.setAttribute('x', '8');
      t2.setAttribute('y', '34');
      const title = (it.title || '').length > 34 ? (it.title || '').slice(0, 32) + '…' : (it.title || '');
      t2.textContent = title;
      const t3 = document.createElementNS('http://www.w3.org/2000/svg', 'text');
      t3.setAttribute('x', '8');
      t3.setAttribute('y', '48');
      t3.setAttribute('fill', 'var(--muted)');
      t3.setAttribute('font-size', '10');
      t3.textContent = st;
      g.appendChild(r);
      g.appendChild(t1);
      g.appendChild(t2);
      g.appendChild(t3);
      g.addEventListener('click', function (ev) {
        ev.stopPropagation();
        document.querySelectorAll('.mm-node').forEach(function (n) { n.classList.remove('selected'); });
        g.classList.add('selected');
        lastFocusedNode = g;
        void showDetail(it, deps);
      });
      g.addEventListener('keydown', function (ev) {
        if (ev.key === 'Enter' || ev.key === ' ') {
          ev.preventDefault();
          g.click();
        }
      });
      gNodes.appendChild(g);
    }

    panX = 20; panY = 20; scale = 1;
    applyPanZoom();
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
    renderMap();
    if (!multiSelectBound) {
      projectSelect.addEventListener('change', function () {
        currentSlug = projectSelect.value;
        closeDetailPopup();
        updateHdrMulti();
        renderMap();
      });
      multiSelectBound = true;
    }
  }

  function renderLegacy() {
    projectRow.style.display = 'none';
    currentSlug = null;
    updateHdrLegacy();
    renderMap();
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

  let drag = null;
  viewport.addEventListener('pointerdown', function (ev) {
    if (ev.target.closest('.mm-node')) return;
    drag = { x: ev.clientX - panX, y: ev.clientY - panY, pid: ev.pointerId };
    viewport.setPointerCapture(ev.pointerId);
  });
  viewport.addEventListener('pointermove', function (ev) {
    if (!drag || drag.pid !== ev.pointerId) return;
    panX = ev.clientX - drag.x;
    panY = ev.clientY - drag.y;
    applyPanZoom();
  });
  function endDrag(ev) {
    if (drag && ev.pointerId === drag.pid) drag = null;
  }
  viewport.addEventListener('pointerup', endDrag);
  viewport.addEventListener('pointercancel', endDrag);

  viewport.addEventListener('wheel', function (ev) {
    if (!ev.ctrlKey) return;
    ev.preventDefault();
    const z = ev.deltaY < 0 ? 1.08 : 1 / 1.08;
    const old = scale;
    scale = Math.min(2.5, Math.max(0.25, scale * z));
    const rect = viewport.getBoundingClientRect();
    const mx = ev.clientX - rect.left;
    const my = ev.clientY - rect.top;
    panX = mx - (mx - panX) * (scale / old);
    panY = my - (my - panY) * (scale / old);
    applyPanZoom();
  }, { passive: false });

  document.getElementById('zoom-in').addEventListener('click', function () {
    scale = Math.min(2.5, scale * 1.15);
    applyPanZoom();
  });
  document.getElementById('zoom-out').addEventListener('click', function () {
    scale = Math.max(0.25, scale / 1.15);
    applyPanZoom();
  });
  document.getElementById('zoom-reset').addEventListener('click', function () {
    panX = 20; panY = 20; scale = 1;
    applyPanZoom();
  });

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
      document.body.innerHTML = '<p style="padding:2rem">Could not load mind map: ' + esc(e.message) + '</p>';
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
