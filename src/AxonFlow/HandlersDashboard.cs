using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
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

        var emit = new Command("emit", "Write index.html with embedded snapshot (open via file://)");
        emit.AddOption(outDir);
        emit.AddOption(refreshSec);
        emit.AddOption(openBrowser);
        emit.SetHandler(ctx => RunEmit(
            ctx.ParseResult.GetValueForOption(outDir)!,
            ctx.ParseResult.GetValueForOption(refreshSec),
            logEmit: true,
            openAfterWrite: ctx.ParseResult.GetValueForOption(openBrowser),
            ctx));

        var openCmd = new Command("open", "Emit once then open index.html in the default browser (same defaults as emit)");
        openCmd.AddOption(outDir);
        openCmd.AddOption(refreshSec);
        openCmd.SetHandler(ctx => RunEmit(
            ctx.ParseResult.GetValueForOption(outDir)!,
            ctx.ParseResult.GetValueForOption(refreshSec),
            logEmit: true,
            openAfterWrite: true,
            ctx));

        var watch = new Command("watch", "Re-run emit every interval (no HTTP server; keeps file:// view fresh)");
        var intervalSec = new Option<int>("--interval", () => 120, "Seconds between emits");
        watch.AddOption(outDir);
        watch.AddOption(refreshSec);
        watch.AddOption(intervalSec);
        watch.AddOption(openBrowser);
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
            for (var first = true;; first = false)
            {
                RunEmit(outD, refresh, logEmit: !quiet, openAfterWrite: first && openAfter, ctx);
                if (ctx.ExitCode != 0) return;
                Thread.Sleep(TimeSpan.FromSeconds(interval));
            }
        });

        dashboard.AddCommand(emit);
        dashboard.AddCommand(openCmd);
        dashboard.AddCommand(watch);
        root.AddCommand(dashboard);
    }

    private static bool TryOpenDatabase(InvocationContext ctx, out Store store, out SqliteConnection c, out string? pid)
    {
        store = null!;
        c = null!;
        pid = null;
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
        pid = CliRoot.GetProjectId(ctx, store, c);
        if (pid is null)
        {
            c.Dispose();
            ctx.ExitCode = 3;
            return false;
        }
        return true;
    }

    private static void RunEmit(DirectoryInfo outDir, int refreshSeconds, bool logEmit, bool openAfterWrite, InvocationContext ctx)
    {
        if (!TryOpenDatabase(ctx, out var store, out var c, out var pid) || pid is null) return;
        try
        {
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

            var jsonOpts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(snapshot, jsonOpts);

            outDir.Create();
            var path = Path.Combine(outDir.FullName, "index.html");
            var html = BuildHtml(json, refreshSeconds, slug, pfx);
            File.WriteAllText(path, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption))
                JsonOut.WriteOk(new { path, refreshSeconds, itemCount = items.Count });
            else if (logEmit && !ctx.ParseResult.GetValueForOption(CliRoot.QuietOption))
                JsonOut.WriteText($"Wrote {path} ({items.Count} items). Open file:// URL in a browser; run `dashboard watch` to refresh data periodically.");

            ctx.ExitCode = 0;
            if (openAfterWrite && !ctx.ParseResult.GetValueForOption(CliRoot.JsonOption) && !TryLaunchDefaultBrowser(path))
                ctx.ExitCode = 4;
        }
        finally
        {
            c.Dispose();
        }
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

    private static string BuildHtml(string snapshotJson, int refreshSeconds, string projectSlug, string refPrefix)
    {
        var esc = snapshotJson.Replace("</script>", "<\\/script>", StringComparison.Ordinal);
        var titleEnc = System.Net.WebUtility.HtmlEncode(projectSlug);
        var refEnc = System.Net.WebUtility.HtmlEncode(refPrefix);
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
    Read-only view of <strong>{{refEnc}}-*</strong> items.
    Page reloads every <strong>{{refreshSeconds}}</strong>s (meta refresh).
    For live updates to the snapshot file, run: <code>axonflow dashboard watch --db … --out …</code>
  </footer>
  <script type="application/json" id="af-snapshot">
""";
        var tail = """
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
  const items = data.items || [];
  const pfx = (data.project && data.project.refPrefix) || 'AF';
  document.getElementById('hdr-meta').textContent =
    'Project: ' + (data.project && data.project.slug || '') +
    ' · Generated: ' + (data.generatedAt || '') +
    ' · ' + pfx + '-*';

  const cols = ['backlog', 'ready', 'in_progress', 'blocked', 'done', 'cancelled'];
  const byStatus = Object.fromEntries(cols.map(c => [c, []]));
  for (const it of items) {
    if (byStatus[it.status]) byStatus[it.status].push(it);
  }
  for (const k of cols) {
    byStatus[k].sort((a, b) => (a.priority - b.priority) || (a.ref && b.ref ? a.ref.localeCompare(b.ref) : 0));
  }

  const counts = document.getElementById('counts');
  const open = items.filter(i => i.status !== 'done' && i.status !== 'cancelled').length;
  counts.innerHTML =
    '<span>Open <strong>' + open + '</strong></span>' +
    cols.map(c => '<span>' + c.replace('_', ' ') + ' <strong>' + byStatus[c].length + '</strong></span>').join('');

  const board = document.getElementById('board');

  function esc(s) {
    if (s == null) return '';
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
  }

  function cardHtml(it) {
    const st = it.stream === 'emergent' ? 'stream-emergent' : 'stream-plan';
    let badges = '<span class="badge ' + st + '">' + esc(it.stream || '') + '</span>';
    if (it.assignedTo) badges += '<span class="badge assignee">' + esc(it.assignedTo) + '</span>';
    if (it.status === 'blocked' && it.blockedReason)
      badges += '<span class="badge" style="color:var(--blocked)">blocked</span>';
    return '<div class="card" data-id="' + esc(it.id) + '">' +
      '<div class="ref">' + esc(it.ref) + '</div>' +
      '<div class="title">' + esc(it.title) + '</div>' +
      '<div class="badges">' + badges + '</div></div>';
  }

  for (const c of cols) {
    const col = document.createElement('section');
    col.className = 'col';
    col.innerHTML = '<h2>' + esc(c.replace('_', ' ')) + '</h2><div class="cards" data-status="' + esc(c) + '"></div>';
    const cards = col.querySelector('.cards');
    for (const it of byStatus[c]) cards.insertAdjacentHTML('beforeend', cardHtml(it));
    board.appendChild(col);
  }

  const detail = document.getElementById('detail');
  const detailTitle = document.getElementById('detail-title');
  const detailBody = document.getElementById('detail-body');

  function showDetail(it) {
    if (!it) { detail.hidden = true; return; }
    detail.hidden = false;
    detailTitle.textContent = it.ref + ' — ' + it.title;
    const preds = (data.dependencies || []).filter(d => d.successorRef === it.ref);
    const lines = [
      'Type: ' + it.type,
      'Status: ' + it.status,
      'Priority: ' + it.priority,
      it.body ? ('Body:' + String.fromCharCode(10) + it.body) : '',
      it.blockedReason ? 'Blocked reason: ' + it.blockedReason : '',
      it.blockedByWorkItemId ? 'Blocked by work item id: ' + it.blockedByWorkItemId : '',
      preds.length ? 'Predecessors (finish-start): ' + preds.map(p => p.predecessorRef).join(', ') : ''
    ].filter(Boolean);
    detailBody.textContent = lines.join(String.fromCharCode(10));
  }

  board.addEventListener('click', (ev) => {
    const card = ev.target.closest('.card');
    if (!card) return;
    const id = card.getAttribute('data-id');
    const it = items.find(x => x.id === id);
    document.querySelectorAll('.card.selected').forEach(x => x.classList.remove('selected'));
    card.classList.add('selected');
    showDetail(it);
  });
})();
  </script>
</body>
</html>
""";
        return head + esc + tail;
    }
}
