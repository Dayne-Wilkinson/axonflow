using System.CommandLine.Builder;
using System.CommandLine.Parsing;

namespace AxonFlow.Tests;

public class DashboardEmitTests
{
    private static Parser CreateParser() =>
        new CommandLineBuilder(CliRoot.Build()).UseDefaults().Build();

    [Fact]
    public async Task Dashboard_emit_writes_html_with_parseable_snapshot()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-dash-{Guid.NewGuid():N}.db");
        var outDir = Path.Combine(Path.GetTempPath(), $"axonflow-out-{Guid.NewGuid():N}");
        try
        {
            var parser = CreateParser();
            var exit = await parser.InvokeAsync(new[] { "init", "--db", db });
            Assert.Equal(0, exit);
            exit = await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--type", "task", "--title", "Sample", "--status", "ready", "--json"
            });
            Assert.Equal(0, exit);

            Directory.CreateDirectory(outDir);
            exit = await parser.InvokeAsync(new[]
            {
                "dashboard", "emit", "--db", db, "--out", outDir, "--refresh-seconds", "60", "--quiet"
            });
            Assert.Equal(0, exit);

            var html = await File.ReadAllTextAsync(Path.Combine(outDir, "index.html"));
            Assert.Contains("id=\"af-snapshot\"", html);
            Assert.Contains("id=\"detail-overlay\"", html);
            Assert.Contains("id=\"detail-popup-body\"", html);
            Assert.Contains("href=\"mindmap.html\"", html);

            var mind = await File.ReadAllTextAsync(Path.Combine(outDir, "mindmap.html"));
            Assert.Contains("id=\"af-snapshot\"", mind);
            Assert.Contains("id=\"map-svg\"", mind);
            Assert.Contains("href=\"index.html\"", mind);
            Assert.Contains("id=\"detail-overlay\"", mind);
            var start = html.IndexOf("<script type=\"application/json\" id=\"af-snapshot\">", StringComparison.Ordinal);
            var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            var json = html[(start + "<script type=\"application/json\" id=\"af-snapshot\">".Length)..end].Trim();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(doc.RootElement.GetProperty("items").GetArrayLength() >= 1);
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
            try { Directory.Delete(outDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Dashboard_emit_all_projects_writes_schema_v2_with_multiple_slugs()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-dash2-{Guid.NewGuid():N}.db");
        var outDir = Path.Combine(Path.GetTempPath(), $"axonflow-out2-{Guid.NewGuid():N}");
        try
        {
            var parser = CreateParser();
            Assert.Equal(0, await parser.InvokeAsync(new[] { "init", "--db", db }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "project", "add", "--db", db, "--name", "Second", "--slug", "second", "--ref-prefix", "OX"
            }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--project", "default", "--type", "task", "--title", "A", "--status", "ready", "--json"
            }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--project", "second", "--type", "task", "--title", "B", "--status", "ready", "--json"
            }));

            Directory.CreateDirectory(outDir);
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "dashboard", "emit", "--db", db, "--out", outDir, "--all-projects", "--quiet"
            }));

            var html = await File.ReadAllTextAsync(Path.Combine(outDir, "index.html"));
            var start = html.IndexOf("<script type=\"application/json\" id=\"af-snapshot\">", StringComparison.Ordinal);
            var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
            var json = html[(start + "<script type=\"application/json\" id=\"af-snapshot\">".Length)..end].Trim();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(2, doc.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(doc.RootElement.GetProperty("projects").GetArrayLength() >= 2);
            Assert.True(doc.RootElement.GetProperty("itemsByProjectSlug").TryGetProperty("default", out _));
            Assert.True(doc.RootElement.GetProperty("itemsByProjectSlug").TryGetProperty("second", out var sec));
            Assert.True(sec.GetProperty("items").GetArrayLength() >= 1);

            Assert.True(File.Exists(Path.Combine(outDir, "mindmap.html")));
            var mind = await File.ReadAllTextAsync(Path.Combine(outDir, "mindmap.html"));
            Assert.Contains("\"schemaVersion\":2", mind);
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
            try { Directory.Delete(outDir, recursive: true); } catch { /* ignore */ }
        }
    }
}
