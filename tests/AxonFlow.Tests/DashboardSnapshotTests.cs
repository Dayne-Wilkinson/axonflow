using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Text.Json;

namespace AxonFlow.Tests;

public class DashboardSnapshotTests
{
    private static Parser CreateParser() =>
        new CommandLineBuilder(CliRoot.Build()).UseDefaults().Build();

    [Fact]
    public void DashboardSnapshot_single_project_matches_schema_v1()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-snap-{Guid.NewGuid():N}.db");
        try
        {
            Assert.Equal(0, CreateParser().Invoke(new[] { "init", "--db", db }));
            Assert.Equal(0, CreateParser().Invoke(new[]
            {
                "item", "add", "--db", db, "--type", "epic", "--title", "E", "--status", "backlog", "--json"
            }));

            var store = new Store($"Data Source={db};Mode=ReadOnly");
            using var c = store.Open();
            var opts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var built = DashboardSnapshot.Build(store, c, "default", allProjects: false, opts);
            using var doc = JsonDocument.Parse(built.Json);
            Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(doc.RootElement.GetProperty("items").GetArrayLength() >= 1);
            Assert.Equal("Default", doc.RootElement.GetProperty("project").GetProperty("name").GetString());
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Project_set_name_then_snapshot_includes_display_name()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-snap-name-{Guid.NewGuid():N}.db");
        try
        {
            var parser = CreateParser();
            Assert.Equal(0, await parser.InvokeAsync(new[] { "init", "--db", db }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "project", "set-name", "--db", db, "--slug", "default", "--name", "axonflow", "--quiet"
            }));

            var store = new Store($"Data Source={db};Mode=ReadOnly");
            using var c = store.Open();
            var opts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var built = DashboardSnapshot.Build(store, c, "default", allProjects: false, opts);
            using var doc = JsonDocument.Parse(built.Json);
            Assert.Equal("axonflow", doc.RootElement.GetProperty("project").GetProperty("name").GetString());
            Assert.Equal("default", doc.RootElement.GetProperty("project").GetProperty("slug").GetString());
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }
}
