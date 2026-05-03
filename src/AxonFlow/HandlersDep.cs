using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Data.Sqlite;

namespace AxonFlow;

internal static class HandlersDep
{
    public static void Register(RootCommand root)
    {
        var dep = new Command("dep", "Dependencies");
        var add = new Command("add", "Add finish-start dependency (predecessor before successor)");
        var p = new Option<string>("--predecessor") { IsRequired = true };
        var s = new Option<string>("--successor") { IsRequired = true };
        var k = new Option<string>("--kind", () => "finish_start");
        add.AddOption(p);
        add.AddOption(s);
        add.AddOption(k);
        add.SetHandler(ctx =>
        {
            var predecessor = ctx.ParseResult.GetValueForOption(p)!;
            var successor = ctx.ParseResult.GetValueForOption(s)!;
            var kind = ctx.ParseResult.GetValueForOption(k)!;
            RunDepAdd(predecessor, successor, kind, ctx);
        });

        var rm = new Command("remove", "Remove dependency");
        var p2 = new Option<string>("--predecessor") { IsRequired = true };
        var s2 = new Option<string>("--successor") { IsRequired = true };
        var k2 = new Option<string>("--kind", () => "finish_start");
        rm.AddOption(p2);
        rm.AddOption(s2);
        rm.AddOption(k2);
        rm.SetHandler(ctx =>
        {
            var predecessor = ctx.ParseResult.GetValueForOption(p2)!;
            var successor = ctx.ParseResult.GetValueForOption(s2)!;
            var kind = ctx.ParseResult.GetValueForOption(k2)!;
            RunDepRemove(predecessor, successor, kind, ctx);
        });

        dep.AddCommand(add);
        dep.AddCommand(rm);
        root.AddCommand(dep);
    }

    private static bool Open(InvocationContext ctx, out Store store, out SqliteConnection c, out string? pid)
    {
        store = null!;
        c = null!;
        pid = null;
        var dbPath = Path.GetFullPath(ctx.ParseResult.GetValueForOption(CliRoot.DbOption)!);
        if (!File.Exists(dbPath))
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("not_found", "Database not found");
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

    private static void RunDepAdd(string predecessor, string successor, string kind, InvocationContext ctx)
    {
        if (!Open(ctx, out var store, out var c, out var pid) || pid is null) return;
        var pred = store.ResolveItem(c, pid, predecessor);
        var succ = store.ResolveItem(c, pid, successor);
        if (pred is null || succ is null)
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("not_found", "predecessor or successor not found");
            ctx.ExitCode = 3;
            return;
        }
        if (store.HasPathSuccessorToPredecessor(c, succ.Id, pred.Id, null))
        {
            if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteErr("conflict", "Would create cycle");
            ctx.ExitCode = 4;
            return;
        }
        if (ctx.ParseResult.GetValueForOption(CliRoot.DryRunOption)) { ctx.ExitCode = 0; return; }
        using var tx = c.BeginTransaction();
        store.AddDependency(c, tx, pid, pred.Id, succ.Id, string.IsNullOrEmpty(kind) ? "finish_start" : kind);
        tx.Commit();
        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteOk(new { ok = true });
        ctx.ExitCode = 0;
    }

    private static void RunDepRemove(string predecessor, string successor, string kind, InvocationContext ctx)
    {
        if (!Open(ctx, out var store, out var c, out var pid) || pid is null) return;
        var pred = store.ResolveItem(c, pid, predecessor);
        var succ = store.ResolveItem(c, pid, successor);
        if (pred is null || succ is null)
        {
            ctx.ExitCode = 3;
            return;
        }
        if (ctx.ParseResult.GetValueForOption(CliRoot.DryRunOption)) { ctx.ExitCode = 0; return; }
        store.RemoveDependency(c, pred.Id, succ.Id, string.IsNullOrEmpty(kind) ? "finish_start" : kind);
        if (ctx.ParseResult.GetValueForOption(CliRoot.JsonOption)) JsonOut.WriteOk(new { ok = true });
        ctx.ExitCode = 0;
    }
}
