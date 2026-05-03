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
}
