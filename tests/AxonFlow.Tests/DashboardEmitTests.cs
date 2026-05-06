using System.Text.Json;
using System.Text.Json.Serialization;

namespace AxonFlow.Tests;

/// <summary>HTML written for dashboard serve (no CLI emit; cache files under serve path).</summary>
public class DashboardStaticHtmlTests
{
    [Fact]
    public async Task WriteServeIndexHtml_writes_parseable_board_and_tree_v1()
    {
        var outDir = Path.Combine(Path.GetTempPath(), $"axonflow-serve-html-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outDir);
            var jsonOpts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            await HandlersDashboard.WriteServeIndexHtmlAsync(new DirectoryInfo(outDir), refreshSeconds: 60, "default",
                allProjects: false, jsonOpts);

            var index = await File.ReadAllTextAsync(Path.Combine(outDir, "index.html"));
            Assert.Contains("id=\"af-snapshot\"", index);
            Assert.Contains("id=\"detail-overlay\"", index);
            Assert.Contains("href=\"tree.html\"", index);
            Assert.Contains("Tree view</a>", index);

            var tree = await File.ReadAllTextAsync(Path.Combine(outDir, "tree.html"));
            Assert.Contains("id=\"af-snapshot\"", tree);
            Assert.Contains("id=\"tree-body\"", tree);
            Assert.Contains("role=\"tree\"", tree);
            Assert.Contains("href=\"index.html\"", tree);
            Assert.Contains("af-chip-status", tree);
            Assert.Contains("statusPresentation", tree);
            Assert.Contains("af-st-done", tree);

            var stub = await File.ReadAllTextAsync(Path.Combine(outDir, "mindmap.html"));
            Assert.Contains("tree.html", stub);
            Assert.DoesNotContain("id=\"map-svg\"", stub);
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task WriteServeIndexHtml_all_projects_bootstrap_contains_schema_v2_flag()
    {
        var outDir = Path.Combine(Path.GetTempPath(), $"axonflow-serve-html2-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outDir);
            var jsonOpts = HandlersDashboard.CreateDashboardJsonSerializerOptions();
            await HandlersDashboard.WriteServeIndexHtmlAsync(new DirectoryInfo(outDir), 120, "default",
                allProjects: true, jsonOpts);

            var index = await File.ReadAllTextAsync(Path.Combine(outDir, "index.html"));
            var marker = "<script type=\"application/json\" id=\"af-snapshot\">";
            var start = index.IndexOf(marker, StringComparison.Ordinal);
            var end = index.IndexOf("</script>", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            var json = index[(start + marker.Length)..end].Trim();
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("__served").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("allProjects").GetBoolean());
            Assert.Equal(120, doc.RootElement.GetProperty("pollSeconds").GetInt32()); // wired from refreshSeconds bootstrap
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { /* ignore */ }
        }
    }
}
