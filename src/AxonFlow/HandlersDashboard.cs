using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace AxonFlow;

internal static class HandlersDashboard
{
    public static void Register(RootCommand root)
    {
        var dashboard = new Command("dashboard", "Read-only HTML dashboard (static file + optional watch loop)");

        var outDir = new Option<DirectoryInfo>("--out", () => new DirectoryInfo("dashboard"), "Output directory");
        var refreshSec = new Option<int>("--refresh-seconds", () => 120, "HTML meta refresh interval (full page reload)");
        var openBrowser = new Option<bool>("--open", () => false, "Open index.html in the default browser after a successful write");
        var allProjects = new Option<bool>("--all-projects", () => false, "Embed all projects (schema v2) with an in-page project picker");

        var emit = new Command("emit", "Write index.html with embedded snapshot (open via file://)");
        emit.AddOption(outDir);
        emit.AddOption(refreshSec);
        emit.AddOption(openBrowser);
        emit.AddOption(allProjects);
        emit.SetHandler(ctx => RunEmit(
            ctx.ParseResult.GetValueForOption(outDir)!,
            ctx.ParseResult.GetValueForOption(refreshSec),
            logEmit: true,
            openAfterWrite: ctx.ParseResult.GetValueForOption(openBrowser),
            allProjects: ctx.ParseResult.GetValueForOption(allProjects),
            ctx));

        var openCmd = new Command("open", "Emit once then open index.html in the default browser (same defaults as emit)");
        openCmd.AddOption(outDir);
        openCmd.AddOption(refreshSec);
        openCmd.AddOption(allProjects);
        openCmd.SetHandler(ctx => RunEmit(
            ctx.ParseResult.GetValueForOption(outDir)!,
            ctx.ParseResult.GetValueForOption(refreshSec),
            logEmit: true,
            openAfterWrite: true,
            allProjects: ctx.ParseResult.GetValueForOption(allProjects),
            ctx));

        var watch = new Command("watch", "Re-run emit every interval (no HTTP server; keeps file:// view fresh)");
        var intervalSec = new Option<int>("--interval", () => 120, "Seconds between emits");
        watch.AddOption(outDir);
        watch.AddOption(refreshSec);
        watch.AddOption(intervalSec);
        watch.AddOption(openBrowser);
        watch.AddOption(allProjects);
        watch.SetHandler(ctx =>
        {
            var outD = ctx.ParseResult.GetValueForOption(outDir)!;
            var refresh = ctx.ParseResult.GetValueForOption(refreshSec);
            var interval = ctx.ParseResult.GetValueForOption(intervalSec);
            if (interval < 5)
            {
                if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption))
                    JsonOut.WriteErr("validation", "interval must be at least 5 seconds");
                else
                    Console.Error.WriteLine("interval must be at least 5 seconds");
                ctx.ExitCode = 2;
                return;
            }
            if (!ctx.ParseResult.GetValueForOption(CliRoot.QuietOption))
                JsonOut.WriteText($"Watching DB; emitting every {interval}s to {Path.Combine(outD.FullName, "index.html")} (Ctrl+C to stop).");
            var quiet = ctx.ParseResult.GetValueForOption(CliRoot.QuietOption);
            var openAfter = ctx.ParseResult.GetValueForOption(openBrowser);
            var allProj = ctx.ParseResult.GetValueForOption(allProjects);
            for (var first = true;; first = false)
            {
                RunEmit(outD, refresh, logEmit: !quiet, openAfterWrite: first && openAfter, allProjects: allProj, ctx);
                if (ctx.ExitCode != 0) return;
                Thread.Sleep(TimeSpan.FromSeconds(interval));
            }
        });

        dashboard.AddCommand(emit);
        dashboard.AddCommand(openCmd);
        dashboard.AddCommand(watch);
        root.AddCommand(dashboard);
    }

    private static bool TryOpenConnection(InvocationContext ctx, out Store store, out SqliteConnection c)
    {
        store = null!;
        c = null!;
        var dbPath = Path.GetFullPath(ctx.ParseResult.GetValueForOption(CliRoot.DbOption)!);
        if (!File.Exists(dbPath))
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption))
                JsonOut.WriteErr("not_found", "Database not found; run init first.");
            else
                Console.Error.WriteLine("Database not found; run init first.");
            ctx.ExitCode = 3;
            return false;
        }
        store = new Store($"Data Source={dbPath};Mode=ReadWrite");
        c = store.Open();
        return true;
    }

    private static void RunEmit(DirectoryInfo outDir, int refreshSeconds, bool logEmit, bool openAfterWrite, bool allProjects, InvocationContext ctx)
    {
        if (!TryOpenConnection(ctx, out var store, out var c)) return;
        try
        {
            var jsonOpts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            string json;
            string pageTitle;
            string refScopeHint;
            int itemCount;

            if (allProjects)
            {
                var defaultSlug = ctx.ParseResult.GetValueForOption(CliRoot.ProjectOption)!;
                var rootNode = BuildSnapshotV2(store, c, defaultSlug, jsonOpts);
                json = rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                pageTitle = "All projects";
                refScopeHint = "multi-project";
                itemCount = CountItemsInV2(rootNode);
            }
            else
            {
                var pid = CliRoot.GetProjectId(ctx, store, c);
                if (pid is null)
                {
                    c.Dispose();
                    ctx.ExitCode = 3;
                    return;
                }
                var items = store.AllItemsForProject(c, pid);
                var pfx = store.GetProjectRefPrefix(c, pid);
                var slug = ctx.ParseResult.GetValueForOption(CliRoot.ProjectOption)!;
                var deps = store.ListDependenciesForProject(c, pid);
                var byId = items.ToDictionary(x => x.Id, StringComparer.Ordinal);
                string RefOf(string id) =>
                    byId.TryGetValue(id, out var w) ? $"{pfx}-{w.RefNumber}" : id;

                var depDtos = deps.Select(d => new
                {
                    predecessorRef = RefOf(d.PredecessorId),
                    successorRef = RefOf(d.SuccessorId),
                    kind = d.Kind
                }).ToList();

                var snapshot = new
                {
                    schemaVersion = 1,
                    generatedAt = DateTimeOffset.UtcNow.ToString("O"),
                    project = new { slug, refPrefix = pfx },
                    items = items.Select(w => ItemJson.ItemDto(store, c, w)).ToList(),
                    dependencies = depDtos
                };
                json = JsonSerializer.Serialize(snapshot, jsonOpts);
                pageTitle = slug;
                refScopeHint = $"{pfx}-*";
                itemCount = items.Count;
            }

            outDir.Create();
            var path = Path.Combine(outDir.FullName, "index.html");
            var html = BuildHtml(json, refreshSeconds, pageTitle, refScopeHint, allProjects);
            File.WriteAllText(path, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption))
                JsonOut.WriteOk(new { path, refreshSeconds, itemCount, allProjects });
            else if (logEmit && !ctx.ParseResult.GetValueForOption(CliRoot.QuietOption))
                JsonOut.WriteText($"Wrote {path} ({itemCount} items). Open file:// URL in a browser; run `dashboard watch` to refresh data periodically.");

            ctx.ExitCode = 0;
            if (openAfterWrite && !ctx.ParseResult.GetValueForOption(CliRoot.JsonOption) && !TryLaunchDefaultBrowser(path))
                ctx.ExitCode = 4;
        }
        finally
        {
            c.Dispose();
        }
    }

    private static int CountItemsInV2(JsonObject root)
    {
        var bySlug = root["itemsByProjectSlug"] as JsonObject;
        if (bySlug is null) return 0;
        var n = 0;
        foreach (var prop in bySlug)
        {
            if (prop.Value is not JsonObject o) continue;
            if (o["items"] is JsonArray arr) n += arr.Count;
        }
        return n;
    }

    private static JsonObject BuildSnapshotV2(Store store, SqliteConnection c, string defaultSlug, JsonSerializerOptions jsonOpts)
    {
        var rows = store.ListProjects(c);
        var itemsBySlug = new JsonObject();
        var projectsArr = new JsonArray();
        foreach (var (id, name, slug, refPrefix) in rows)
        {
            projectsArr.Add(JsonSerializer.SerializeToNode(new { id, name, slug, refPrefix }, jsonOpts));
            var projectId = store.GetProjectId(c, slug);
            if (projectId is null) continue;
            var items = store.AllItemsForProject(c, projectId);
            var deps = store.ListDependenciesForProject(c, projectId);
            var pfx = store.GetProjectRefPrefix(c, projectId);
            var byId = items.ToDictionary(x => x.Id, StringComparer.Ordinal);
            string RefOf(string id) =>
                byId.TryGetValue(id, out var w) ? $"{pfx}-{w.RefNumber}" : id;

            var itemsArr = new JsonArray();
            foreach (var w in items)
            {
                var o = JsonSerializer.SerializeToNode(ItemJson.ItemDto(store, c, w), jsonOpts)!.AsObject();
                o["projectSlug"] = slug;
                o["projectName"] = name;
                itemsArr.Add(o);
            }
            var depsArr = new JsonArray();
            foreach (var d in deps)
            {
                depsArr.Add(JsonSerializer.SerializeToNode(new
                {
                    predecessorRef = RefOf(d.PredecessorId),
                    successorRef = RefOf(d.SuccessorId),
                    kind = d.Kind
                }, jsonOpts));
            }
            itemsBySlug[slug] = new JsonObject { ["items"] = itemsArr, ["dependencies"] = depsArr };
        }

        var effectiveDefault = defaultSlug;
        if (!itemsBySlug.ContainsKey(effectiveDefault) && rows.Count > 0)
            effectiveDefault = rows[0].Slug;

        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["defaultProjectSlug"] = effectiveDefault,
            ["projects"] = projectsArr,
            ["itemsByProjectSlug"] = itemsBySlug
        };
    }

    private static bool TryLaunchDefaultBrowser(string htmlPath)
    {
        try
        {
            var full = Path.GetFullPath(htmlPath);
            var uri = new Uri(full);
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not open browser: {ex.Message}");
            return false;
        }
    }

    private static string BuildHtml(string snapshotJson, int refreshSeconds, string pageTitle, string refScopeHint, bool multiProject)
    {
        var esc = snapshotJson.Replace("</script>", "<\\/script>", StringComparison.Ordinal);
        var titleEnc = System.Net.WebUtility.HtmlEncode(pageTitle);
        var footerBody = multiProject
            ? $"<p>Multiple projects in this database. Use the picker to switch. Page reloads every <strong>{refreshSeconds}</strong>s (meta refresh). Run <code>axonflow dashboard watch --all-projects …</code> to refresh the file.</p>"
            : $"<p>Read-only view of <strong>{System.Net.WebUtility.HtmlEncode(refScopeHint)}</strong> items. Page reloads every <strong>{refreshSeconds}</strong>s (meta refresh). For live updates to the snapshot file, run: <code>axonflow dashboard watch --db … --out …</code></p>";
        var head = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1"/>
  <meta http-equiv="refresh" content="{{refreshSeconds}}"/>
  <title>AxonFlow — {{titleEnc}}</title>
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
    main { padding: 1rem; display: grid; gap: 1rem; }
    .board {
      display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 0.75rem;
    }
    .col {
      background: var(--panel); border: 1px solid var(--border); border-radius: 8px;
      min-height: 120px; display: flex; flex-direction: column;
    }
    .col h2 {
      margin: 0; padding: 0.5rem 0.75rem; font-size: 0.75rem; text-transform: uppercase;
      letter-spacing: 0.06em; color: var(--muted); border-bottom: 1px solid var(--border);
    }
    .cards { padding: 0.5rem; display: flex; flex-direction: column; gap: 0.4rem; flex: 1; }
    .card {
      background: var(--bg); border: 1px solid var(--border); border-radius: 6px;
      padding: 0.45rem 0.55rem; font-size: 0.8rem; cursor: pointer;
    }
    .card:hover { border-color: #4a5f7a; }
    .card.selected { outline: 2px solid var(--plan); }
    .card .ref { color: var(--plan); font-weight: 600; font-size: 0.72rem; }
    .card .title { margin-top: 0.15rem; line-height: 1.25; }
    .badges { margin-top: 0.35rem; display: flex; flex-wrap: wrap; gap: 0.25rem; }
    .badge {
      font-size: 0.65rem; padding: 0.1rem 0.35rem; border-radius: 4px;
      background: #2d3a4d; color: var(--muted);
    }
    .badge.stream-plan { color: var(--plan); }
    .badge.stream-emergent { color: var(--emergent); }
    .badge.assignee { color: #a371f7; }
    .badge.proj { color: #79c0ff; }
    .detail {
      background: var(--panel); border: 1px solid var(--border); border-radius: 8px;
      padding: 1rem; max-width: 900px;
    }
    .detail h3 { margin: 0 0 0.5rem; font-size: 1rem; }
    .detail pre {
      margin: 0; white-space: pre-wrap; word-break: break-word; font-size: 0.78rem;
      color: var(--muted);
    }
    footer {
      padding: 0.75rem 1.25rem; color: var(--muted); font-size: 0.75rem;
      border-top: 1px solid var(--border);
    }
  </style>
</head>
<body>
  <header>
    <div>
      <h1>AxonFlow dashboard</h1>
      <div class="meta" id="hdr-meta"></div>
      <div class="project-row" id="project-row">
        <label for="project-select">Project</label>
        <select id="project-select" aria-label="Select project"></select>
      </div>
    </div>
    <div class="counts" id="counts"></div>
  </header>
  <main>
    <section class="board" id="board" aria-label="Work board"></section>
    <section class="detail" id="detail" hidden>
      <h3 id="detail-title"></h3>
      <pre id="detail-body"></pre>
    </section>
  </main>
  <footer>
{{footerBody}}
  </footer>
  <script type="application/json" id="af-snapshot">
""";
        var head2 = """
  </script>
  <script>
(function () {
  const el = document.getElementById('af-snapshot');
  let data;
  try {
    data = JSON.parse(el.textContent);
  } catch (e) {
    document.body.innerHTML = '<p style="padding:2rem">Invalid snapshot JSON.</p>';
    return;
  }

  const cols = ['backlog', 'ready', 'in_progress', 'blocked', 'done', 'cancelled'];
  const board = document.getElementById('board');
  const counts = document.getElementById('counts');
  const detail = document.getElementById('detail');
  const detailTitle = document.getElementById('detail-title');
  const detailBody = document.getElementById('detail-body');
  const projectRow = document.getElementById('project-row');
  const projectSelect = document.getElementById('project-select');

  function esc(s) {
    if (s == null) return '';
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
  }

  function cardHtml(it, showProjBadge) {
    const st = it.stream === 'emergent' ? 'stream-emergent' : 'stream-plan';
    let badges = '<span class="badge ' + st + '">' + esc(it.stream || '') + '</span>';
    if (showProjBadge && it.projectSlug)
      badges += '<span class="badge proj">' + esc(it.projectSlug) + '</span>';
    if (it.assignedTo) badges += '<span class="badge assignee">' + esc(it.assignedTo) + '</span>';
    if (it.status === 'blocked' && it.blockedReason)
      badges += '<span class="badge" style="color:var(--blocked)">blocked</span>';
    return '<div class="card" data-id="' + esc(it.id) + '">' +
      '<div class="ref">' + esc(it.ref) + '</div>' +
      '<div class="title">' + esc(it.title) + '</div>' +
      '<div class="badges">' + badges + '</div></div>';
  }

  function showDetail(it, depsList) {
    if (!it) { detail.hidden = true; return; }
    detail.hidden = false;
    const slugLine = it.projectSlug ? ('[' + it.projectSlug + '] ') : '';
    detailTitle.textContent = slugLine + it.ref + ' — ' + it.title;
    const preds = (depsList || []).filter(function (d) { return d.successorRef === it.ref; });
    const lines = [
      'Type: ' + it.type,
      'Status: ' + it.status,
      'Priority: ' + it.priority,
      it.body ? ('Body:' + String.fromCharCode(10) + it.body) : '',
      it.blockedReason ? 'Blocked reason: ' + it.blockedReason : '',
      it.blockedByWorkItemId ? 'Blocked by work item id: ' + it.blockedByWorkItemId : '',
      preds.length ? 'Predecessors (finish-start): ' + preds.map(function (p) { return p.predecessorRef; }).join(', ') : ''
    ].filter(Boolean);
    detailBody.textContent = lines.join(String.fromCharCode(10));
  }

  function renderLegacy() {
    projectRow.style.display = 'none';
    const items = data.items || [];
    const deps = data.dependencies || [];
    const pfx = (data.project && data.project.refPrefix) || 'AF';
    document.getElementById('hdr-meta').textContent =
      'Project: ' + (data.project && data.project.slug || '') +
      ' · Generated: ' + (data.generatedAt || '') +
      ' · ' + pfx + '-*';
    const byStatus = Object.fromEntries(cols.map(function (c) { return [c, []]; }));
    for (const it of items) {
      if (byStatus[it.status]) byStatus[it.status].push(it);
    }
    for (const k of cols) {
      byStatus[k].sort(function (a, b) {
        return (a.priority - b.priority) || (a.ref && b.ref ? a.ref.localeCompare(b.ref) : 0);
      });
    }
    const open = items.filter(function (i) { return i.status !== 'done' && i.status !== 'cancelled'; }).length;
    counts.innerHTML =
      '<span>Open <strong>' + open + '</strong></span>' +
      cols.map(function (c) {
        return '<span>' + esc(c.replace('_', ' ')) + ' <strong>' + byStatus[c].length + '</strong></span>';
      }).join('');
    board.innerHTML = '';
    for (const c of cols) {
      const col = document.createElement('section');
      col.className = 'col';
      col.innerHTML = '<h2>' + esc(c.replace('_', ' ')) + '</h2><div class="cards" data-status="' + esc(c) + '"></div>';
      const cards = col.querySelector('.cards');
      for (const it of byStatus[c]) cards.insertAdjacentHTML('beforeend', cardHtml(it, false));
      board.appendChild(col);
    }
    board.addEventListener('click', function (ev) {
      const card = ev.target.closest('.card');
      if (!card) return;
      const id = card.getAttribute('data-id');
      const it = items.find(function (x) { return x.id === id; });
      document.querySelectorAll('.card.selected').forEach(function (x) { x.classList.remove('selected'); });
      card.classList.add('selected');
      showDetail(it, deps);
    });
  }

  function renderMulti() {
    projectRow.style.display = 'flex';
    let currentSlug = data.defaultProjectSlug;
    if (!data.itemsByProjectSlug[currentSlug] && data.projects && data.projects.length) {
      currentSlug = data.projects[0].slug;
    }
    function bundle() { return data.itemsByProjectSlug[currentSlug] || { items: [], dependencies: [] }; }

    function fillSelect() {
      projectSelect.innerHTML = '';
      for (const p of data.projects || []) {
        const opt = document.createElement('option');
        opt.value = p.slug;
        opt.textContent = p.name + ' (' + p.slug + ', ' + p.refPrefix + ')';
        projectSelect.appendChild(opt);
      }
      projectSelect.value = currentSlug;
    }

    function updateHdr() {
      const p = (data.projects || []).find(function (x) { return x.slug === currentSlug; });
      const rp = (p && p.refPrefix) || '';
      document.getElementById('hdr-meta').textContent =
        'Project: ' + currentSlug +
        ' · Generated: ' + (data.generatedAt || '') +
        ' · ' + rp + '-*';
    }

    function renderBoard() {
      const items = bundle().items || [];
      const deps = bundle().dependencies || [];
      const byStatus = Object.fromEntries(cols.map(function (c) { return [c, []]; }));
      for (const it of items) {
        if (byStatus[it.status]) byStatus[it.status].push(it);
      }
      for (const k of cols) {
        byStatus[k].sort(function (a, b) {
          return (a.priority - b.priority) || (a.ref && b.ref ? a.ref.localeCompare(b.ref) : 0);
        });
      }
      const open = items.filter(function (i) { return i.status !== 'done' && i.status !== 'cancelled'; }).length;
      counts.innerHTML =
        '<span>Open <strong>' + open + '</strong></span>' +
        cols.map(function (c) {
          return '<span>' + esc(c.replace('_', ' ')) + ' <strong>' + byStatus[c].length + '</strong></span>';
        }).join('');
      board.innerHTML = '';
      for (const c of cols) {
        const col = document.createElement('section');
        col.className = 'col';
        col.innerHTML = '<h2>' + esc(c.replace('_', ' ')) + '</h2><div class="cards" data-status="' + esc(c) + '"></div>';
        const cards = col.querySelector('.cards');
        for (const it of byStatus[c]) cards.insertAdjacentHTML('beforeend', cardHtml(it, false));
        board.appendChild(col);
      }
      board.onclick = function (ev) {
        const card = ev.target.closest('.card');
        if (!card) return;
        const id = card.getAttribute('data-id');
        const it = items.find(function (x) { return x.id === id; });
        document.querySelectorAll('.card.selected').forEach(function (x) { x.classList.remove('selected'); });
        card.classList.add('selected');
        showDetail(it, deps);
      };
    }

    fillSelect();
    updateHdr();
    renderBoard();
    projectSelect.addEventListener('change', function () {
      currentSlug = projectSelect.value;
      detail.hidden = true;
      updateHdr();
      renderBoard();
    });
  }

  if (data.schemaVersion >= 2 && data.itemsByProjectSlug && data.projects) {
    renderMulti();
  } else {
    renderLegacy();
  }
})();
  </script>
</body>
</html>
""";
        return head + esc + head2;
    }
}
