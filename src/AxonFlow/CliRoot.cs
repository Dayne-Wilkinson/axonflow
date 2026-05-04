using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.Data.Sqlite;

namespace AxonFlow;

public static class CliRoot
{
    public static readonly Option<string> DbOption = new("--db", () => Paths.DefaultDbPath(), "SQLite database file path");
    public static readonly Option<string> ProjectOption = new("--project", () => "default", "Project slug");
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

    public static string? GetProjectId(InvocationContext ctx, Store store, SqliteConnection c)
    {
        var slug = ctx.ParseResult.GetValueForOption(ProjectOption)!;
        return store.GetProjectId(c, slug);
    }
}
