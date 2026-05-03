using System.CommandLine.Invocation;
using Microsoft.Data.Sqlite;

namespace AxonFlow;

internal static class HandlersMeta
{
    public static void Schema(InvocationContext ctx)
    {
        var json = ctx.ParseResult.GetValueForOption(CliRoot.JsonOption);
        if (json)
            JsonOut.WriteOk(new { cliVersion = AppInfo.Version, dbSchemaVersion = AppInfo.DbSchemaVersion });
        else
            JsonOut.WriteText($"cliVersion={AppInfo.Version} dbSchemaVersion={AppInfo.DbSchemaVersion}");
        ctx.ExitCode = 0;
    }

    public static void Init(InvocationContext ctx)
    {
        var dbPath = Path.GetFullPath(ctx.ParseResult.GetValueForOption(CliRoot.DbOption)!);
        var json = ctx.ParseResult.GetValueForOption(CliRoot.JsonOption);
        var dry = ctx.ParseResult.GetValueForOption(CliRoot.DryRunOption);

        if (dry)
        {
            if (json) JsonOut.WriteOk(new { planned = "create_db_and_schema", dbPath });
            else JsonOut.WriteText($"[dry-run] would init: {dbPath}");
            ctx.ExitCode = 0;
            return;
        }

        Paths.EnsureDbDirectory(dbPath);
        var cs = $"Data Source={dbPath};Mode=ReadWriteCreate";
        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            Migration.ApplyInitialSchema(conn);
        }

        var store = new Store(cs);
        using var c = store.Open();
        if (store.CountProjects(c) == 0)
            store.InsertDefaultProject(c);

        if (json)
            JsonOut.WriteOk(new { dbPath, message = "initialized" });
        else
            JsonOut.WriteText($"Initialized {dbPath}");
        ctx.ExitCode = 0;
    }

    public static void ProjectAdd(string name, string slug, string? refPrefix, InvocationContext ctx)
    {
        var dbPath = Path.GetFullPath(ctx.ParseResult.GetValueForOption(CliRoot.DbOption)!);
        var json = ctx.ParseResult.GetValueForOption(CliRoot.JsonOption);
        if (!File.Exists(dbPath))
        {
            if (json) JsonOut.WriteErr("not_found", "Database not found; run init first.");
            else Console.Error.WriteLine("Database not found; run init first.");
            ctx.ExitCode = 3;
            return;
        }

        var cs = $"Data Source={dbPath};Mode=ReadWrite";
        var store = new Store(cs);
        using var c = store.Open();
        try
        {
            store.InsertProject(c, name, slug, string.IsNullOrWhiteSpace(refPrefix) ? "AF" : refPrefix!);
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        {
            if (json) JsonOut.WriteErr("conflict", "Slug already exists.", new { slug });
            else Console.Error.WriteLine("Slug already exists.");
            ctx.ExitCode = 4;
            return;
        }
        if (json) JsonOut.WriteOk(new { slug });
        else JsonOut.WriteText($"Added project {slug}");
        ctx.ExitCode = 0;
    }

    public static void ProjectList(InvocationContext ctx)
    {
        var dbPath = Path.GetFullPath(ctx.ParseResult.GetValueForOption(CliRoot.DbOption)!);
        var json = ctx.ParseResult.GetValueForOption(CliRoot.JsonOption);
        if (!File.Exists(dbPath))
        {
            if (json) JsonOut.WriteErr("not_found", "Database not found; run init first.");
            else Console.Error.WriteLine("Database not found; run init first.");
            ctx.ExitCode = 3;
            return;
        }
        var store = new Store($"Data Source={dbPath};Mode=ReadWrite");
        using var c = store.Open();
        var rows = store.ListProjects(c);
        if (json)
            JsonOut.WriteOk(rows.Select(r => new { id = r.Id, name = r.Name, slug = r.Slug, refPrefix = r.RefPrefix }).ToList());
        else
            foreach (var r in rows)
                JsonOut.WriteText($"{r.Slug}\t{r.Name}\t{r.RefPrefix}");
        ctx.ExitCode = 0;
    }
}
