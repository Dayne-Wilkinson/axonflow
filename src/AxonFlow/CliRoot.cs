using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Data.Sqlite;

namespace AxonFlow;

public static class CliRoot
{
    public static readonly Option<string> DbOption = new("--db", () => Paths.DefaultDbPath(), "SQLite database file path");
    /// <summary>When omitted, slug is inferred from the current directory name (see skill).</summary>
    public static readonly Option<string?> ProjectOption = new("--project", "Project slug; when omitted, inferred from cwd folder name");
    public static readonly Option<bool> JsonOption = new("--json", () => false, "Emit JSON to stdout");
    public static readonly Option<bool> QuietOption = new("--quiet", () => false, "Suppress non-error stdout");
    public static readonly Option<bool> DryRunOption = new("--dry-run", () => false, "Validate only; do not commit writes");

    public static RootCommand Build()
    {
        var root = new RootCommand("AxonFlow — hierarchical work graph for agents and humans");
        root.AddGlobalOption(DbOption);
        root.AddGlobalOption(ProjectOption);
        root.AddGlobalOption(JsonOption);
        root.AddGlobalOption(QuietOption);
        root.AddGlobalOption(DryRunOption);

        var schema = new Command("schema", "Print CLI and database schema versions");
        schema.SetHandler(HandlersMeta.Schema);
        root.AddCommand(schema);

        var init = new Command("init", "Create database file, schema, and default project");
        init.SetHandler(HandlersMeta.Init);
        root.AddCommand(init);

        var project = new Command("project", "Manage projects");
        var projectAdd = new Command("add", "Add a project");
        var nameOpt = new Option<string>("--name") { IsRequired = true };
        var slugOpt = new Option<string>("--slug") { IsRequired = true };
        var refPrefOpt = new Option<string?>("--ref-prefix");
        projectAdd.AddOption(nameOpt);
        projectAdd.AddOption(slugOpt);
        projectAdd.AddOption(refPrefOpt);
        projectAdd.SetHandler(ctx =>
        {
            var name = ctx.ParseResult.GetValueForOption(nameOpt)!;
            var slug = ctx.ParseResult.GetValueForOption(slugOpt)!;
            var rp = ctx.ParseResult.GetValueForOption(refPrefOpt);
            HandlersMeta.ProjectAdd(name, slug, rp, ctx);
        });
        var projectList = new Command("list", "List projects");
        projectList.SetHandler(HandlersMeta.ProjectList);

        var projectSetName = new Command("set-name", "Change a project's display name (slug and work item refs unchanged)");
        var setNameSlug = new Option<string>("--slug") { IsRequired = true };
        var setNameDisplay = new Option<string>("--name") { IsRequired = true };
        projectSetName.AddOption(setNameSlug);
        projectSetName.AddOption(setNameDisplay);
        projectSetName.SetHandler(ctx =>
        {
            HandlersMeta.ProjectSetName(
                ctx.ParseResult.GetValueForOption(setNameSlug)!,
                ctx.ParseResult.GetValueForOption(setNameDisplay)!,
                ctx);
        });

        project.AddCommand(projectAdd);
        project.AddCommand(projectList);
        project.AddCommand(projectSetName);
        root.AddCommand(project);

        HandlersItem.Register(root);
        HandlersDep.Register(root);
        HandlersQuery.Register(root);
        HandlersDashboard.Register(root);

        return root;
    }

    public static string ConnectionString(InvocationContext ctx) =>
        $"Data Source={Path.GetFullPath(ctx.ParseResult.GetValueForOption(DbOption)!)};Mode=ReadWrite";

    public static string ConnectionStringCreate(InvocationContext ctx) =>
        $"Data Source={Path.GetFullPath(ctx.ParseResult.GetValueForOption(DbOption)!)};Mode=ReadWriteCreate";

    public static string ResolveProjectSlug(InvocationContext ctx)
    {
        var slug = ctx.ParseResult.GetValueForOption(ProjectOption);
        return string.IsNullOrWhiteSpace(slug)
            ? ProjectSlug.InferFromWorkingDirectory()
            : slug.Trim();
    }

    /// <summary>Returns project id for resolved slug; creates project row if absent.</summary>
    public static string EnsureProjectExists(InvocationContext ctx, Store store, SqliteConnection c, SqliteTransaction? tx = null)
    {
        var slug = ResolveProjectSlug(ctx);
        var id = store.GetProjectId(c, slug);
        if (id is not null) return id;
        store.InsertProject(c, ProjectSlug.DisplayNameFromSlug(slug), slug, "AF");
        return store.GetProjectId(c, slug)!;
    }

    /// <summary>Opens DB after optional bootstrap; dry-run skips creating DB/project rows.</summary>
    public static bool TryOpenWorkload(InvocationContext ctx, out Store store, out SqliteConnection connection, out string projectId)
    {
        store = null!;
        connection = null!;
        projectId = "";
        var dbPath = Path.GetFullPath(ctx.ParseResult.GetValueForOption(DbOption)!);
        var dry = ctx.ParseResult.GetValueForOption(DryRunOption);
        if (!DatabaseBootstrap.TryPrepare(dbPath, allowCreate: !dry, out var prepErr))
        {
            if (ctx.ParseResult.GetValueForOption(JsonOption)) JsonOut.WriteErr("not_found", prepErr!);
            else Console.Error.WriteLine(prepErr);
            ctx.ExitCode = 3;
            return false;
        }

        store = new Store($"Data Source={dbPath};Mode=ReadWrite");
        connection = store.Open();

        if (dry)
        {
            var slug = ResolveProjectSlug(ctx);
            var pid = store.GetProjectId(connection, slug);
            if (pid is null)
            {
                var msg =
                    $"Project '{slug}' does not exist yet. Omit --dry-run once to materialize cwd-based projects, or pass --project with an existing slug.";
                if (ctx.ParseResult.GetValueForOption(JsonOption)) JsonOut.WriteErr("not_found", msg, new { slug });
                else Console.Error.WriteLine(msg);
                ctx.ExitCode = 3;
                connection.Dispose();
                connection = null!;
                store = null!;
                return false;
            }

            projectId = pid;
            return true;
        }

        projectId = EnsureProjectExists(ctx, store, connection);
        return true;
    }
}
