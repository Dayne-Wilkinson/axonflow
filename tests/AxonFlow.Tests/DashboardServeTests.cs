using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace AxonFlow.Tests;

public class DashboardServeTests
{
    private static Parser CreateParser() =>
        new CommandLineBuilder(CliRoot.Build()).UseDefaults().Build();

    [Fact]
    public async Task Serve_api_snapshot_returns_json_and_item_route_returns_envelope()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-serve-{Guid.NewGuid():N}.db");
        var outDir = Path.Combine(Path.GetTempPath(), $"axonflow-serve-out-{Guid.NewGuid():N}");
        WebApplication? app = null;
        try
        {
            var parser = CreateParser();
            Assert.Equal(0, await parser.InvokeAsync(new[] { "init", "--db", db }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--type", "task", "--title", "HTTP integration item", "--status", "ready", "--json"
            }));

            Directory.CreateDirectory(outDir);
            var outDi = new DirectoryInfo(outDir);
            var jsonOpts = HandlersDashboard.CreateDashboardJsonSerializerOptions();
            await HandlersDashboard.WriteServeIndexHtmlAsync(outDi, 60, "default", false, jsonOpts);
            Assert.True(File.Exists(Path.Combine(outDir, "tree.html")));
            Assert.True(File.Exists(Path.Combine(outDir, "mindmap.html")));

            app = HandlersDashboard.BuildServeWebApplication(db, outDi, "http://127.0.0.1:0", "default", false, jsonOpts);
            await app.StartAsync();

            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
            Assert.NotNull(addresses);
            Assert.NotEmpty(addresses);
            var baseUri = new Uri(addresses.First().TrimEnd('/') + "/");

            using var client = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };

            using var snapRes = await client.GetAsync("api/snapshot?project=default");
            Assert.Equal(HttpStatusCode.OK, snapRes.StatusCode);
            Assert.Equal("application/json", snapRes.Content.Headers.ContentType?.MediaType);
            var snapJson = await snapRes.Content.ReadAsStringAsync();
            using var snapDoc = JsonDocument.Parse(snapJson);
            Assert.Equal(1, snapDoc.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(snapDoc.RootElement.GetProperty("items").GetArrayLength() >= 1);

            using var itemRes = await client.GetAsync("api/item?ref=AF-1&project=default&notes=false");
            Assert.Equal(HttpStatusCode.OK, itemRes.StatusCode);
            Assert.Equal("application/json", itemRes.Content.Headers.ContentType?.MediaType);
            using var itemDoc = JsonDocument.Parse(await itemRes.Content.ReadAsStringAsync());
            Assert.True(itemDoc.RootElement.TryGetProperty("item", out var itemEl));
            Assert.Equal("AF-1", itemEl.GetProperty("ref").GetString());

            using var treeRes = await client.GetAsync("tree.html");
            Assert.Equal(HttpStatusCode.OK, treeRes.StatusCode);
            var treeHtml = await treeRes.Content.ReadAsStringAsync();
            Assert.Contains("id=\"tree-body\"", treeHtml);
            Assert.Contains("__served", treeHtml);

            using var mmRes = await client.GetAsync("mindmap.html");
            Assert.Equal(HttpStatusCode.OK, mmRes.StatusCode);
            var mmHtml = await mmRes.Content.ReadAsStringAsync();
            Assert.Contains("tree.html", mmHtml);
            Assert.Contains("url=tree.html", mmHtml);
        }
        finally
        {
            if (app is not null)
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }

            try { File.Delete(db); } catch { /* ignore */ }
            try { Directory.Delete(outDir, recursive: true); } catch { /* ignore */ }
        }
    }
}
