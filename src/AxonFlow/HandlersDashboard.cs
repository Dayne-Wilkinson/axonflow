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
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace AxonFlow;

internal static partial class HandlersDashboard
{
    public static void Register(RootCommand root)
    {
        var dashboard = new Command("dashboard",
            "Read-only dashboard on loopback HTTP (live data refreshes automatically; Ctrl+C to stop)");
        var pollSecondsOpt = new Option<int>("--poll-seconds", () => 10, "Polling interval in seconds (1-300)");
        dashboard.AddOption(pollSecondsOpt);
        dashboard.SetHandler(async ctx => await RunDashboardCommandAsync(ctx, ctx.ParseResult.GetValueForOption(pollSecondsOpt)).ConfigureAwait(false));
        root.AddCommand(dashboard);
    }

    private static async Task RunDashboardCommandAsync(InvocationContext ctx, int pollSeconds)
    {
        if (ctx.ParseResult.GetValueForOption(CliRoot.DryRunOption))
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption))
                JsonOut.WriteErr("validation", "dashboard does not support --dry-run");
            else
                Console.Error.WriteLine("dashboard does not support --dry-run.");
            ctx.ExitCode = 2;
            return;
        }

        if (pollSeconds < 1 || pollSeconds > 300)
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption))
                JsonOut.WriteErr("validation", "--poll-seconds must be between 1 and 300");
            else
                Console.Error.WriteLine("--poll-seconds must be between 1 and 300");
            ctx.ExitCode = 2;
            return;
        }

        const string listenUrl = "http://127.0.0.1:5057";
        await RunServeAsync(ctx, Paths.DashboardServeCacheDirectory(), listenUrl, pollSeconds).ConfigureAwait(false);
    }

    /// <summary>Minimal HTML so old <c>mindmap.html</c> paths redirect to <c>tree.html</c>.</summary>
    private static string MindmapHtmlRedirectStub() =>
        """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8"/>
  <meta http-equiv="refresh" content="0;url=tree.html"/>
  <title>Moved to tree view</title>
</head>
<body style="margin:0;padding:2rem;font-family:system-ui,Segoe UI,sans-serif;background:#0f1419;color:#e6edf3">
  <p>This view moved to <a href="tree.html" style="color:#3d8bfd">tree.html</a> (tree view).</p>
</body>
</html>
""";

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

    internal static JsonSerializerOptions CreateDashboardJsonSerializerOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static async Task WriteServeIndexHtmlAsync(
        DirectoryInfo outDir,
        int refreshSeconds,
        string projectSlug,
        bool allProjects,
        JsonSerializerOptions jsonOpts,
        CancellationToken cancellationToken = default)
    {
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
        await File.WriteAllTextAsync(path, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
        var treePath = Path.Combine(outDir.FullName, "tree.html");
        var title = allProjects ? "All projects" : projectSlug;
        var treeHtml = BuildTreeViewHtml(bootstrap, refreshSeconds, title, allProjects ? "multi-project" : "live", allProjects, served: new DashboardServedConfig(refreshSeconds, projectSlug, allProjects));
        await File.WriteAllTextAsync(treePath, treeHtml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
        var mindmapStubPath = Path.Combine(outDir.FullName, "mindmap.html");
        await File.WriteAllTextAsync(mindmapStubPath, MindmapHtmlRedirectStub(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
    }

    internal static WebApplication BuildServeWebApplication(
        string dbPath,
        DirectoryInfo outDir,
        string urls,
        string projectSlug,
        bool allProjects,
        JsonSerializerOptions jsonOpts)
    {
        var opts = new WebApplicationOptions
        {
            ContentRootPath = outDir.FullName,
            WebRootPath = outDir.FullName,
            ApplicationName = Assembly.GetEntryAssembly()?.GetName().Name ?? "axonflow"
        };
        var builder = WebApplication.CreateBuilder(opts);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        builder.WebHost.UseUrls(urls);

        var app = builder.Build();
        var log = app.Logger;
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
                log.LogError(ex, "GET /api/snapshot failed (project={Project}, allProjects={AllProjects}).", proj, all);
                return Results.Problem(
                    title: "Snapshot error",
                    detail: "An unexpected error occurred while building the dashboard snapshot.",
                    statusCode: StatusCodes.Status500InternalServerError);
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
                log.LogError(ex, "GET /api/item failed (ref={Ref}, project={Project}).", @ref, slug);
                return Results.Problem(
                    title: "Item error",
                    detail: "An unexpected error occurred while loading the work item.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        return app;
    }

    private static async Task RunServeAsync(InvocationContext ctx, DirectoryInfo outDir, string urls, int pollSeconds)
    {
        if (!IsLoopbackUrl(urls, out var urlErr))
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("validation", urlErr!);
            else Console.Error.WriteLine(urlErr);
            ctx.ExitCode = 2;
            return;
        }

        var dbPath = Path.GetFullPath(ctx.ParseResult.GetValueForOption(CliRoot.DbOption)!);
        DatabaseBootstrap.EnsureInitialized(dbPath);

        const bool allProjects = true;
        var projectSlug = CliRoot.ResolveProjectSlug(ctx);
        var probeStore = new Store($"Data Source={dbPath};Mode=ReadWrite");
        using (var pc = probeStore.Open())
            CliRoot.EnsureProjectExists(ctx, probeStore, pc);

        var jsonOpts = CreateDashboardJsonSerializerOptions();
        await WriteServeIndexHtmlAsync(outDir, pollSeconds, projectSlug, allProjects, jsonOpts).ConfigureAwait(false);

        var app = BuildServeWebApplication(dbPath, outDir, urls, projectSlug, allProjects, jsonOpts);

        if (!ctx.ParseResult.GetValueForOption(CliRoot.QuietOption))
            JsonOut.WriteText($"Serving dashboard at {urls.TrimEnd('/')}/ (poll {pollSeconds}s; Ctrl+C to stop). Static cache: {outDir.FullName}");

        ctx.ExitCode = 0;

        var openBrowser = !ctx.ParseResult.GetValueForOption(CliRoot.JsonOption);
        if (openBrowser && !TryLaunchDefaultBrowser(urls.TrimEnd('/') + "/"))
            ctx.ExitCode = 4;

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
            ? $"<p>Live data from <code>/api/snapshot</code> · auto-refresh every <strong>{served.Value.PollSeconds}</strong>s. Stop with <code>Ctrl+C</code>.</p>"
            : (multiProject
                ? $"<p>Multiple projects in this database. Use the picker to switch. Page reloads every <strong>{refreshSeconds}</strong>s.</p>"
                : $"<p>Read-only view of <strong>{System.Net.WebUtility.HtmlEncode(refScopeHint)}</strong> items.</p>");
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
    .refresh-meta { color: var(--muted); font-size: 0.75rem; margin-top: 0.25rem; }
    .status-warning {
      display: none; border: 1px solid #6e5200; background: rgba(210, 153, 34, 0.12);
      color: #f2cc60; border-radius: 6px; padding: 0.45rem 0.6rem; font-size: 0.78rem;
    }
    .status-warning.show { display: block; }
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
    .detail-overlay {
      position: fixed; inset: 0; z-index: 1000;
    }
    .detail-overlay[hidden] { display: none !important; }
    .detail-backdrop {
      position: absolute; inset: 0; background: rgba(0, 0, 0, 0.55);
    }
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
      <nav class="nav-links" aria-label="Page navigation"><a href="tree.html">Tree view</a></nav>
      <div class="meta" id="hdr-meta"></div>
      <div class="refresh-meta" id="refresh-meta"></div>
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
    <section class="status-warning" id="status-warning" role="status" aria-live="polite"></section>
    <section class="board" id="board" aria-label="Work board"></section>
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
  const refreshMeta = document.getElementById('refresh-meta');
  const statusWarning = document.getElementById('status-warning');
  const detailOverlay = document.getElementById('detail-overlay');
  const detailDialog = document.getElementById('detail-dialog');
  const detailBackdrop = document.getElementById('detail-backdrop');
  const detailClose = document.getElementById('detail-close');
  const detailPopupTitle = document.getElementById('detail-popup-title');
  const detailPopupBody = document.getElementById('detail-popup-body');
  const projectRow = document.getElementById('project-row');
  const projectSelect = document.getElementById('project-select');

  let currentSlug = null;
  let lastFocusedCard = null;
  let detailUiBound = false;
  let lastRefreshAt = null;
  let lastRefreshError = null;

  function closeDetailPopup() {
    detailOverlay.hidden = true;
    detailOverlay.setAttribute('aria-hidden', 'true');
    if (lastFocusedCard && typeof lastFocusedCard.focus === 'function') {
      try { lastFocusedCard.focus(); } catch (e) { /* ignore */ }
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

  function normToken(s) {
    return String(s == null ? '' : s).trim().toLowerCase().replace(/[-\s]+/g, '_');
  }

  function laneForStatus(rawStatus) {
    const token = normToken(rawStatus);
    if (!token) return null;
    if (token === 'active' || token === 'inprogress' || token === 'wip') return 'in_progress';
    if (token === 'complete' || token === 'completed') return 'done';
    if (token === 'cancel' || token === 'canceled') return 'cancelled';
    if (cols.indexOf(token) >= 0) return token;
    return null;
  }

  function classifyItems(items) {
    const byStatus = Object.fromEntries(cols.map(function (c) { return [c, []]; }));
    const unknown = [];
    for (const it of items || []) {
      const lane = laneForStatus(it.status);
      if (lane) byStatus[lane].push(it);
      else unknown.push(it);
    }
    for (const k of cols) {
      byStatus[k].sort(function (a, b) {
        return (a.priority - b.priority) || (a.ref && b.ref ? a.ref.localeCompare(b.ref) : 0);
      });
    }
    unknown.sort(function (a, b) {
      return (a.priority - b.priority) || (a.ref && b.ref ? a.ref.localeCompare(b.ref) : 0);
    });
    return { byStatus: byStatus, unknown: unknown };
  }

  function openCount(items) {
    return (items || []).filter(function (it) {
      const lane = laneForStatus(it.status);
      return lane !== 'done' && lane !== 'cancelled';
    }).length;
  }

  function blockedOrActiveCount(items) {
    return (items || []).filter(function (it) {
      const lane = laneForStatus(it.status);
      return lane === 'blocked' || lane === 'in_progress';
    }).length;
  }

  function updateRefreshMeta() {
    if (!served) {
      refreshMeta.textContent = '';
      return;
    }
    let text = 'Refresh: ';
    if (lastRefreshAt) text += lastRefreshAt.toLocaleTimeString();
    else text += 'waiting...';
    if (lastRefreshError) text += ' · last error: ' + lastRefreshError;
    refreshMeta.textContent = text;
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

  function wireBoardClicks(items, deps) {
    board.onclick = function (ev) {
      const card = ev.target.closest('.card');
      if (!card) {
        closeDetailPopup();
        return;
      }
      const id = card.getAttribute('data-id');
      const it = items.find(function (x) { return x.id === id; });
      document.querySelectorAll('.card.selected').forEach(function (x) { x.classList.remove('selected'); });
      card.classList.add('selected');
      lastFocusedCard = card;
      void showDetail(it, deps);
    };
  }

  function renderLegacy() {
    projectRow.style.display = 'none';
    statusWarning.className = 'status-warning';
    statusWarning.textContent = '';
    currentSlug = null;
    const items = data.items || [];
    const deps = data.dependencies || [];
    const pfx = (data.project && data.project.refPrefix) || 'AF';
    const projLabel = (data.project && (data.project.name || data.project.slug)) || '';
    document.getElementById('hdr-meta').textContent =
      'Project: ' + projLabel +
      ' · Generated: ' + (data.generatedAt || '') +
      ' · ' + pfx + '-*';
    const grouped = classifyItems(items);
    const byStatus = grouped.byStatus;
    const unknown = grouped.unknown;
    const open = openCount(items);
    counts.innerHTML =
      '<span>Open <strong>' + open + '</strong></span>' +
      cols.map(function (c) {
        return '<span>' + esc(c.replace('_', ' ')) + ' <strong>' + byStatus[c].length + '</strong></span>';
      }).join('') +
      '<span>unknown <strong>' + unknown.length + '</strong></span>';
    board.innerHTML = '';
    for (const c of cols) {
      const col = document.createElement('section');
      col.className = 'col';
      col.innerHTML = '<h2>' + esc(c.replace('_', ' ')) + '</h2><div class="cards" data-status="' + esc(c) + '"></div>';
      const cards = col.querySelector('.cards');
      for (const it of byStatus[c]) cards.insertAdjacentHTML('beforeend', cardHtml(it, false));
      board.appendChild(col);
    }
    if (unknown.length) {
      const col = document.createElement('section');
      col.className = 'col';
      col.innerHTML = '<h2>unknown</h2><div class="cards" data-status="unknown"></div>';
      const cards = col.querySelector('.cards');
      for (const it of unknown) cards.insertAdjacentHTML('beforeend', cardHtml(it, false));
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
        const projectItems = (data.itemsByProjectSlug[p.slug] && data.itemsByProjectSlug[p.slug].items) || [];
        const projectOpen = openCount(projectItems);
        const opt = document.createElement('option');
        opt.value = p.slug;
        opt.textContent = p.name + ' (' + p.slug + ', ' + p.refPrefix + ', open ' + projectOpen + ')';
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
      const grouped = classifyItems(items);
      const byStatus = grouped.byStatus;
      const unknown = grouped.unknown;
      const open = openCount(items);
      counts.innerHTML =
        '<span>Open <strong>' + open + '</strong></span>' +
        cols.map(function (c) {
          return '<span>' + esc(c.replace('_', ' ')) + ' <strong>' + byStatus[c].length + '</strong></span>';
        }).join('') +
        '<span>unknown <strong>' + unknown.length + '</strong></span>';
      board.innerHTML = '';
      for (const c of cols) {
        const col = document.createElement('section');
        col.className = 'col';
        col.innerHTML = '<h2>' + esc(c.replace('_', ' ')) + '</h2><div class="cards" data-status="' + esc(c) + '"></div>';
        const cards = col.querySelector('.cards');
        for (const it of byStatus[c]) cards.insertAdjacentHTML('beforeend', cardHtml(it, false));
        board.appendChild(col);
      }
      if (unknown.length) {
        const col = document.createElement('section');
        col.className = 'col';
        col.innerHTML = '<h2>unknown</h2><div class="cards" data-status="unknown"></div>';
        const cards = col.querySelector('.cards');
        for (const it of unknown) cards.insertAdjacentHTML('beforeend', cardHtml(it, false));
        board.appendChild(col);
      }
      const currentActive = blockedOrActiveCount(items);
      let otherActive = 0;
      for (const p of data.projects || []) {
        if (p.slug === currentSlug) continue;
        const otherItems = (data.itemsByProjectSlug[p.slug] && data.itemsByProjectSlug[p.slug].items) || [];
        otherActive += blockedOrActiveCount(otherItems);
      }
      if (currentActive == 0 && otherActive > 0) {
        statusWarning.className = 'status-warning show';
        statusWarning.textContent = 'No active/blocked items in this project, but ' + otherActive + ' active/blocked item(s) exist in other projects. Check the project picker.';
      } else {
        statusWarning.className = 'status-warning';
        statusWarning.textContent = '';
      }
      wireBoardClicks(items, deps);
    }

    fillSelect();
    updateHdr();
    renderBoard();
    if (!multiSelectBound) {
      projectSelect.addEventListener('change', function () {
        currentSlug = projectSelect.value;
        closeDetailPopup();
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
        lastRefreshAt = new Date();
        lastRefreshError = null;
        updateRefreshMeta();
        window.__axonflowReload = async function () {
          try {
            data = await loadSnapshotFromApi();
            lastRefreshAt = new Date();
            lastRefreshError = null;
            rerenderAll();
          } catch (e) {
            lastRefreshError = e && e.message ? e.message : String(e);
            updateRefreshMeta();
            throw e;
          }
        };
        setInterval(function () {
          window.__axonflowReload().catch(function (e) { console.error(e); });
        }, (bootstrapData.pollSeconds || 10) * 1000);
      } else {
        data = bootstrapData;
      }
      updateRefreshMeta();
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
