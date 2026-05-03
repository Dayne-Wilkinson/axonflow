using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AxonFlow;

internal static class HandlersQuery
{
    public static void Register(RootCommand root)
    {
        var tree = new Command("tree", "Print work item hierarchy");
        var rootRef = new Option<string?>("--root");
        var depth = new Option<int>("--depth", () => 12);
        tree.AddOption(rootRef);
        tree.AddOption(depth);
        tree.SetHandler(ctx => RunTree(ctx.ParseResult.GetValueForOption(rootRef), ctx.ParseResult.GetValueForOption(depth), ctx));

        var board = new Command("board", "ASCII kanban by status");
        var w = new Option<int>("--width", () => 28);
        board.AddOption(w);
        board.SetHandler(ctx => RunBoard(ctx.ParseResult.GetValueForOption(w), ctx));

        var validate = new Command("validate", "Structural and health checks");
        var stale = new Option<int>("--stale-days", () => 7);
        validate.AddOption(stale);
        validate.SetHandler(ctx => RunValidate(ctx.ParseResult.GetValueForOption(stale), ctx));

        var export = new Command("export", "Export snapshot");
        var fmt = new Option<string>("--format", () => "json");
        export.AddOption(fmt);
        export.SetHandler(ctx => RunExport(ctx.ParseResult.GetValueForOption(fmt)!, ctx));

        root.AddCommand(tree);
        root.AddCommand(board);
        root.AddCommand(validate);
        root.AddCommand(export);
    }

    private static bool Open(InvocationContext ctx, out Store store, out SqliteConnection c, out string? pid)
    {
        store = null!;
        c = null!;
        pid = null;
        var dbPath = Path.GetFullPath(ctx.ParseResult.GetValueForOption(CliRoot.DbOption)!);
        if (!File.Exists(dbPath)) { ctx.ExitCode = 3; return false; }
        store = new Store($"Data Source={dbPath};Mode=ReadWrite");
        c = store.Open();
        pid = CliRoot.GetProjectId(ctx, store, c);
        if (pid is null) { c.Dispose(); ctx.ExitCode = 3; return false; }
        return true;
    }

    private static void RunTree(string? rootRef, int maxDepth, InvocationContext ctx)
    {
        if (!Open(ctx, out var store, out var c, out var pid) || pid is null) return;
        var items = store.AllItemsForProject(c, pid);
        var byParent = items.GroupBy(i => i.ParentId ?? "").ToDictionary(g => g.Key, g => g.OrderBy(x => x.Priority).ThenBy(x => x.RefNumber).ToList());
        Store.WorkItemRow? rootItem = null;
        if (!string.IsNullOrEmpty(rootRef))
        {
            rootItem = store.ResolveItem(c, pid, rootRef);
            if (rootItem is null) { ctx.ExitCode = 3; return; }
        }
        var json = ctx.ParseResult.GetValueForOption(CliRoot.JsonOption);
        if (json)
        {
            object Build(Store.WorkItemRow r, int d)
            {
                if (d > maxDepth) return new { id = r.Id, truncated = true };
                var children = byParent.GetValueOrDefault(r.Id, []).Select(ch => Build(ch, d + 1)).ToList();
                return new { item = ItemJson.ItemDto(store, c, r), children };
            }
            if (rootItem is not null)
                JsonOut.WriteOk(Build(rootItem, 0));
            else
                JsonOut.WriteOk(byParent.GetValueOrDefault("", []).Select(r => Build(r, 0)).ToList());
        }
        else
        {
            void Walk(Store.WorkItemRow r, int d, string indent)
            {
                if (d > maxDepth) return;
                var pfx = store.GetProjectRefPrefix(c, pid);
                JsonOut.WriteText($"{indent}{pfx}-{r.RefNumber} [{r.Status}] {r.Title}");
                foreach (var ch in byParent.GetValueOrDefault(r.Id, []))
                    Walk(ch, d + 1, indent + "  ");
            }
            if (rootItem is not null)
                Walk(rootItem, 0, "");
            else
                foreach (var r in byParent.GetValueOrDefault("", []))
                    Walk(r, 0, "");
        }
        ctx.ExitCode = 0;
    }

    private static void RunBoard(int width, InvocationContext ctx)
    {
        if (!Open(ctx, out var store, out var c, out var pid) || pid is null) return;
        var items = store.AllItemsForProject(c, pid);
        var cols = new[] { "backlog", "ready", "in_progress", "blocked", "done", "cancelled" };
        var grouped = cols.ToDictionary(col => col, col => items.Where(i => i.Status == col).OrderBy(i => i.Priority).ThenBy(i => i.RefNumber).ToList());
        var json = ctx.ParseResult.GetValueForOption(CliRoot.JsonOption);
        if (json)
        {
            JsonOut.WriteOk(grouped.ToDictionary(k => k.Key, v => v.Value.Select(x => ItemJson.ItemDto(store, c, x)).ToList()));
            ctx.ExitCode = 0;
            return;
        }
        var pfx = store.GetProjectRefPrefix(c, pid);
        foreach (var col in cols)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"== {col} ==");
            foreach (var it in grouped[col].Take(40))
            {
                var line = $"{pfx}-{it.RefNumber}\t{it.Title}";
                sb.AppendLine(line.Length > width ? $"{pfx}-{it.RefNumber}" : line);
            }
            JsonOut.WriteText(sb.ToString());
        }
        ctx.ExitCode = 0;
    }

    private static void RunValidate(int staleDays, InvocationContext ctx)
    {
        if (!Open(ctx, out var store, out var c, out var pid) || pid is null) return;
        var items = store.AllItemsForProject(c, pid);
        var report = new Dictionary<string, object?>();
        var cycles = new List<string>();
        var violated = new List<object>();
        var staleList = new List<object>();
        var deep = new List<object>();
        var emergentOld = new List<object>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-staleDays);
        foreach (var w in items.Where(x => x.Status == "in_progress"))
        {
            if (DateTimeOffset.TryParse(w.UpdatedAt, out var u) && u < cutoff)
            {
                var rp = store.GetProjectRefPrefix(c, pid);
                staleList.Add(new { @ref = $"{rp}-{w.RefNumber}", w.UpdatedAt });
            }
        }
        foreach (var w in items)
        {
            if (w.Status != "done" && w.Status != "cancelled" && !store.PredecessorsSatisfied(c, w.Id))
                violated.Add(new { item = ItemJson.ItemDto(store, c, w), reason = "unfinished_predecessor" });
        }
        var depthMemo = new Dictionary<string, int>();
        int Depth(string id)
        {
            if (depthMemo.TryGetValue(id, out var d)) return d;
            var row = store.GetItemById(c, id);
            if (row is null || string.IsNullOrEmpty(row.ParentId)) { depthMemo[id] = 0; return 0; }
            d = 1 + Depth(row.ParentId);
            depthMemo[id] = d;
            return d;
        }
        foreach (var w in items)
        {
            var d = Depth(w.Id);
            if (d > 8)
            {
                var rp = store.GetProjectRefPrefix(c, pid);
                deep.Add(new { @ref = $"{rp}-{w.RefNumber}", depth = d });
            }
        }
        foreach (var w in items.Where(x => x.Stream == "emergent" && x.Status is "backlog" or "ready"))
        {
            if (DateTimeOffset.TryParse(w.CreatedAt, out var cr) && cr < DateTimeOffset.UtcNow.AddDays(-14))
            {
                var rp = store.GetProjectRefPrefix(c, pid);
                emergentOld.Add(new { @ref = $"{rp}-{w.RefNumber}", w.CreatedAt });
            }
        }
        report["staleInProgress"] = staleList;
        report["deepTrees"] = deep;
        report["emergentOpenTooLong"] = emergentOld;
        report["violatedDependencies"] = violated;
        report["cycles"] = cycles;
        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption))
            JsonOut.WriteOk(report);
        else
            JsonOut.WriteText(JsonSerializer.Serialize(report));
        ctx.ExitCode = 0;
    }

    private static void RunExport(string format, InvocationContext ctx)
    {
        if (!Open(ctx, out var store, out var c, out var pid) || pid is null) return;
        var items = store.AllItemsForProject(c, pid);
        if (format.Equals("markdown", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder();
            sb.AppendLine("# AxonFlow export");
            foreach (var w in items.OrderBy(x => x.RefNumber))
            {
                var rp = store.GetProjectRefPrefix(c, pid);
                sb.AppendLine($"- **{rp}-{w.RefNumber}** [{w.Status}] {w.Title}");
            }
            JsonOut.WriteText(sb.ToString());
        }
        else
            JsonOut.WriteOk(items.Select(w => ItemJson.ItemDto(store, c, w)).ToList());
        ctx.ExitCode = 0;
    }
}
