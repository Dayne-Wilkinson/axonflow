using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

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

        var urlsOpt = new Option<string>("--urls", () => "http://127.0.0.1:5057", "Loopback base URL for Kestrel (must use 127.0.0.1, localhost, or ::1)");
        var serve = new Command("serve", "Kestrel loopback: static dashboard + read-only /api/snapshot (live fetch; Ctrl+C to stop)");
        serve.AddOption(outDir);
        serve.AddOption(refreshSec);
        serve.AddOption(allProjects);
        serve.AddOption(openBrowser);
        serve.AddOption(urlsOpt);
        serve.SetHandler(async ctx => await RunServeAsync(
            ctx.ParseResult.GetValueForOption(outDir)!,
            ctx.ParseResult.GetValueForOption(urlsOpt)!,
            ctx.ParseResult.GetValueForOption(refreshSec),
            ctx.ParseResult.GetValueForOption(allProjects),
            ctx.ParseResult.GetValueForOption(openBrowser),
            ctx));

        dashboard.AddCommand(emit);
        dashboard.AddCommand(openCmd);
        dashboard.AddCommand(watch);
        dashboard.AddCommand(serve);
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

            var slug = ctx.ParseResult.GetValueForOption(CliRoot.ProjectOption)!;
            if (!allProjects && CliRoot.GetProjectId(ctx, store, c) is null)
            {
                c.Dispose();
                ctx.ExitCode = 3;
                return;
            }

            var built = DashboardSnapshot.Build(store, c, slug, allProjects, jsonOpts);
            var json = built.Json;
            var pageTitle = built.PageTitle;
            var refScopeHint = built.RefScopeHint;
            var itemCount = built.ItemCount;

            outDir.Create();
            var path = Path.Combine(outDir.FullName, "index.html");
            var html = BuildHtml(json, refreshSeconds, pageTitle, refScopeHint, allProjects, served: null);
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

    private static bool IsLoopbackUrl(string raw, out string? error)
    {
        error = null;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
        {
            error = "Invalid --urls; expected absolute http(s) URL.";
            return false;
        }
        if (u.Host is not ("127.0.0.1" or "localhost" or "::1"))
        {
            error = "For safety, --urls must target loopback (127.0.0.1, localhost, or ::1).";
            return false;
        }
        return true;
    }

    private static async Task RunServeAsync(DirectoryInfo outDir, string urls, int refreshSeconds, bool allProjects, bool openBrowser, InvocationContext ctx)
    {
        if (!IsLoopbackUrl(urls, out var urlErr))
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("validation", urlErr!);
            else Console.Error.WriteLine(urlErr);
            ctx.ExitCode = 2;
            return;
        }

        var dbPath = Path.GetFullPath(ctx.ParseResult.GetValueForOption(CliRoot.DbOption)!);
        if (!File.Exists(dbPath))
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("not_found", "Database not found; run init first.");
            else Console.Error.WriteLine("Database not found; run init first.");
            ctx.ExitCode = 3;
            return;
        }

        var projectSlug = ctx.ParseResult.GetValueForOption(CliRoot.ProjectOption)!;
        var probeStore = new Store($"Data Source={dbPath};Mode=ReadOnly");
        using (var pc = probeStore.Open())
        {
            if (!allProjects && probeStore.GetProjectId(pc, projectSlug) is null)
            {
                if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("not_found", "Project not found for slug.");
                else Console.Error.WriteLine("Project not found for slug.");
                ctx.ExitCode = 3;
                return;
            }
        }

        var jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var bootstrap = JsonSerializer.Serialize(new
        {
            __served = true,
            pollSeconds = refreshSeconds,
            defaultProject = projectSlug,
            allProjects
        }, jsonOpts);

        outDir.Create();
        var path = Path.Combine(outDir.FullName, "index.html");
        var html = BuildHtml(bootstrap, refreshSeconds, allProjects ? "All projects" : projectSlug, allProjects ? "multi-project" : "live", allProjects, served: new DashboardServedConfig(refreshSeconds, projectSlug, allProjects));
        await File.WriteAllTextAsync(path, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);

        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption))
        {
            JsonOut.WriteOk(new { path, urls, refreshSeconds, allProjects });
            ctx.ExitCode = 0;
            return;
        }

        var opts = new WebApplicationOptions
        {
            ContentRootPath = outDir.FullName,
            WebRootPath = outDir.FullName,
            ApplicationName = Assembly.GetEntryAssembly()?.GetName().Name ?? "axonflow"
        };
        var builder = WebApplication.CreateBuilder(opts);
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls(urls);

        var app = builder.Build();
        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = new PhysicalFileProvider(outDir.FullName)
        });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(outDir.FullName)
        });

        var slugClosure = projectSlug;
        var allClosure = allProjects;
        var jsonOptsClosure = jsonOpts;

        app.MapGet("/api/snapshot", (string? project, bool? allProjectsQuery) =>
        {
            var all = allProjectsQuery ?? allClosure;
            var proj = string.IsNullOrWhiteSpace(project) ? slugClosure : project!;
            try
            {
                var store = new Store($"Data Source={dbPath};Mode=ReadOnly");
                using var conn = store.Open();
                var built = DashboardSnapshot.Build(store, conn, proj, all, jsonOptsClosure);
                return Results.Text(built.Json, "application/json; charset=utf-8");
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        app.MapGet("/api/item", (string @ref, string? project, bool notes, int? notesLimit) =>
        {
            var lim = notesLimit is null or < 1 ? 20 : Math.Min(notesLimit.Value, 200);
            var slug = string.IsNullOrWhiteSpace(project) ? slugClosure : project!;
            try
            {
                var store = new Store($"Data Source={dbPath};Mode=ReadOnly");
                using var conn = store.Open();
                var pid = store.GetProjectId(conn, slug);
                if (pid is null) return Results.NotFound();
                var row = store.ResolveItem(conn, pid, @ref);
                if (row is null) return Results.NotFound();
                var env = ItemJson.BuildItemShowEnvelope(store, conn, pid, row, notes, lim);
                return Results.Json(env, jsonOptsClosure);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        if (!ctx.ParseResult.GetValueForOption(CliRoot.QuietOption))
            JsonOut.WriteText($"Serving dashboard at {urls} (Ctrl+C to stop). GET /api/snapshot and /api/item");

        if (openBrowser && !TryLaunchDefaultBrowser(urls))
            ctx.ExitCode = 4;

        ctx.ExitCode = 0;
        await app.RunAsync().ConfigureAwait(false);
    }

    private readonly record struct DashboardServedConfig(int PollSeconds, string DefaultProjectSlug, bool AllProjects);

    private static bool TryLaunchDefaultBrowser(string pathOrUrl)
    {
        try
        {
            var launch = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? pathOrUrl
                : new Uri(Path.GetFullPath(pathOrUrl)).AbsoluteUri;
            Process.Start(new ProcessStartInfo
            {
                FileName = launch,
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

    private static string BuildHtml(string snapshotJson, int refreshSeconds, string pageTitle, string refScopeHint, bool multiProject, DashboardServedConfig? served)
    {
        var esc = snapshotJson.Replace("</script>", "<\\/script>", StringComparison.Ordinal);
        var titleEnc = System.Net.WebUtility.HtmlEncode(pageTitle);
        var metaRefresh = served is null
            ? $"""  <meta http-equiv="refresh" content="{refreshSeconds}"/>"""
            : "";
        var footerBody = served is not null
            ? $"<p>Live data from <code>/api/snapshot</code> (poll every <strong>{served.Value.PollSeconds}</strong>s). Offline snapshot: <code>axonflow dashboard emit …</code>. Stop server with <code>Ctrl+C</code>.</p>"
            : (multiProject
                ? $"<p>Multiple projects in this database. Use the picker to switch. Page reloads every <strong>{refreshSeconds}</strong>s (meta refresh). Run <code>axonflow dashboard watch --all-projects …</code> to refresh the file.</p>"
                : $"<p>Read-only view of <strong>{System.Net.WebUtility.HtmlEncode(refScopeHint)}</strong> items. Page reloads every <strong>{refreshSeconds}</strong>s (meta refresh). For live updates to the snapshot file, run: <code>axonflow dashboard watch --db … --out …</code> or <code>axonflow dashboard serve …</code>.</p>");
        var head = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1"/>
__META_REFRESH__
  <title>AxonFlow — __TITLE__</title>
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
    .badge.type-epic { border-left: 3px solid #f778ba; }
    .badge.type-feature { border-left: 3px solid #79c0ff; }
    .badge.type-story { border-left: 3px solid #56d364; }
    .badge.type-task { border-left: 3px solid #d2a8ff; }
    .badge.type-bug { border-left: 3px solid #f85149; }
    .badge.type-chore { border-left: 3px solid #8b949e; }
    .type-legend { font-size: 0.72rem; color: var(--muted); margin-top: 0.35rem; }
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
      <div class="type-legend" id="type-legend" hidden>
        Types: epic · feature · story · task · bug · chore (badges + left stripe; not color-only — labels in cards)
      </div>
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
__FOOTER__
  </footer>
  <script type="application/json" id="af-snapshot">
""".Replace("__META_REFRESH__", metaRefresh, StringComparison.Ordinal)
            .Replace("__TITLE__", titleEnc, StringComparison.Ordinal)
            .Replace("__FOOTER__", footerBody, StringComparison.Ordinal);
        var head2 = """
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

  if (served) {
    typeLegend.hidden = false;
  } else {
    typeLegend.hidden = true;
  }

  const cols = ['backlog', 'ready', 'in_progress', 'blocked', 'done', 'cancelled'];
  const board = document.getElementById('board');
  const counts = document.getElementById('counts');
  const detail = document.getElementById('detail');
  const detailTitle = document.getElementById('detail-title');
  const detailBody = document.getElementById('detail-body');
  const projectRow = document.getElementById('project-row');
  const projectSelect = document.getElementById('project-select');

  let currentSlug = null;
  function currentItemProject() {
    if (data && data.schemaVersion >= 2 && data.itemsByProjectSlug && data.projects) {
      return currentSlug || data.defaultProjectSlug || (data.projects[0] && data.projects[0].slug) || bootstrapData.defaultProject || 'default';
    }
    if (data && data.project && data.project.slug) {
      return data.project.slug;
    }
    return bootstrapData.defaultProject || 'default';
  }

  function esc(s) {
    if (s == null) return '';
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
  }

  function typeBadgeClass(tp) {
    const t = (tp || 'task').toLowerCase();
    const map = { epic: 'type-epic', feature: 'type-feature', story: 'type-story', task: 'type-task', bug: 'type-bug', chore: 'type-chore' };
    return map[t] || 'type-task';
  }

  function cardHtml(it, showProjBadge) {
    const tlabel = (it.type || 'task').toLowerCase();
    const st = it.stream === 'emergent' ? 'stream-emergent' : 'stream-plan';
    let badges = '<span class="badge ' + typeBadgeClass(it.type) + '" aria-label="work item type">' + esc(tlabel) + '</span>';
    badges += '<span class="badge ' + st + '">' + esc(it.stream || '') + '</span>';
    if (showProjBadge && it.projectSlug)
      badges += '<span class="badge proj">' + esc(it.projectSlug) + '</span>';
    if (it.assignedTo) badges += '<span class="badge assignee">' + esc(it.assignedTo) + '</span>';
    if (it.status === 'blocked' && it.blockedReason)
      badges += '<span class="badge" style="color:var(--blocked)">blocked</span>';
    const alabel = esc((it.type || '') + ' ' + (it.ref || '') + ': ' + (it.title || ''));
    return '<div class="card" tabindex="0" data-id="' + esc(it.id) + '" data-ref="' + esc(it.ref) + '" aria-label="' + alabel + '">' +
      '<div class="ref">' + esc(it.ref) + '</div>' +
      '<div class="title">' + esc(it.title) + '</div>' +
      '<div class="badges">' + badges + '</div></div>';
  }

  async function showDetail(it, depsList) {
    if (!it) { detail.hidden = true; return; }
    detail.hidden = false;
    const slugLine = it.projectSlug ? ('[' + it.projectSlug + '] ') : '';
    detailTitle.textContent = slugLine + (it.type || '') + ' ' + it.ref + ' — ' + it.title;
    if (served && it.ref) {
      detailBody.textContent = 'Loading…';
      try {
        const u = new URL('/api/item', location.origin);
        u.searchParams.set('ref', it.ref);
        u.searchParams.set('project', currentItemProject());
        const res = await fetch(u);
        if (res.ok) {
          const env = await res.json();
          detailBody.textContent = JSON.stringify(env, null, 2);
          return;
        }
      } catch (e) { /* fall through */ }
    }
    const preds = (depsList || []).filter(function (d) { return d.successorRef === it.ref; });
    detailBody.textContent = JSON.stringify({ item: it, predecessorDependencies: preds }, null, 2);
  }

  function wireBoardClicks(items, deps) {
    board.onclick = function (ev) {
      const card = ev.target.closest('.card');
      if (!card) return;
      const id = card.getAttribute('data-id');
      const it = items.find(function (x) { return x.id === id; });
      document.querySelectorAll('.card.selected').forEach(function (x) { x.classList.remove('selected'); });
      card.classList.add('selected');
      void showDetail(it, deps);
    };
  }

  function renderLegacy() {
    projectRow.style.display = 'none';
    currentSlug = null;
    const items = data.items || [];
    const deps = data.dependencies || [];
    const pfx = (data.project && data.project.refPrefix) || 'AF';
    const projLabel = (data.project && (data.project.name || data.project.slug)) || '';
    document.getElementById('hdr-meta').textContent =
      'Project: ' + projLabel +
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
    wireBoardClicks(items, deps);
  }

  let multiSelectBound = false;
  function renderMulti() {
    projectRow.style.display = 'flex';
    if (!currentSlug || !data.itemsByProjectSlug[currentSlug]) {
      currentSlug = data.defaultProjectSlug;
      if (!data.itemsByProjectSlug[currentSlug] && data.projects && data.projects.length) {
        currentSlug = data.projects[0].slug;
      }
    }
    function bundle() { return data.itemsByProjectSlug[currentSlug] || { items: [], dependencies: [] }; }

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

    function updateHdr() {
      const p = (data.projects || []).find(function (x) { return x.slug === currentSlug; });
      const rp = (p && p.refPrefix) || '';
      const projLabel = (p && (p.name || p.slug)) || currentSlug;
      document.getElementById('hdr-meta').textContent =
        'Project: ' + projLabel +
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
      wireBoardClicks(items, deps);
    }

    fillSelect();
    updateHdr();
    renderBoard();
    if (!multiSelectBound) {
      projectSelect.addEventListener('change', function () {
        currentSlug = projectSelect.value;
        detail.hidden = true;
        updateHdr();
        renderBoard();
      });
      multiSelectBound = true;
    }
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
      document.body.innerHTML = '<p style="padding:2rem">Could not load dashboard: ' + esc(e.message) + '</p>';
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
