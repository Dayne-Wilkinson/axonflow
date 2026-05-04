using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Text;
using System.Text.Json;

namespace AxonFlow.Tests;

public class ItemCliTests
{
    private static readonly object ConsoleRedirectGate = new();

    private static Parser CreateParser() =>
        new CommandLineBuilder(CliRoot.Build()).UseDefaults().Build();

    [Fact]
    public async Task Item_add_body_and_update_ref_clear_body_and_list_parent_by_ref()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-itemcli-{Guid.NewGuid():N}.db");
        var bodyPath = Path.Combine(Path.GetTempPath(), $"axonflow-body-{Guid.NewGuid():N}.txt");
        try
        {
            var parser = CreateParser();
            Assert.Equal(0, await parser.InvokeAsync(new[] { "init", "--db", db }));

            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--type", "story", "--title", "Parent story", "--status", "ready", "--json"
            }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--type", "task", "--title", "Child", "--parent", "AF-1",
                "--body", "inline spec", "--status", "ready", "--json"
            }));

            var (cShow1, j1) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "show", "--db", db, "--ref", "AF-2", "--json"
            });
            Assert.Equal(0, cShow1);
            using (var doc = JsonDocument.Parse(j1))
            {
                var item = doc.RootElement.GetProperty("data").GetProperty("item");
                Assert.Equal("inline spec", item.GetProperty("body").GetString());
            }

            await File.WriteAllTextAsync(bodyPath, "from file", Encoding.UTF8);
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "update", "--db", db, "--ref", "AF-2", "--body-file", bodyPath, "--json"
            }));
            var (cShow2, j2) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "show", "--db", db, "--ref", "AF-2", "--json"
            });
            Assert.Equal(0, cShow2);
            using (var doc = JsonDocument.Parse(j2))
            {
                var item = doc.RootElement.GetProperty("data").GetProperty("item");
                Assert.Equal("from file", item.GetProperty("body").GetString());
            }

            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "update", "--db", db, "--ref", "AF-2", "--clear-body", "--json"
            }));
            var (cShow3, j3) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "show", "--db", db, "--ref", "AF-2", "--json"
            });
            Assert.Equal(0, cShow3);
            using (var doc = JsonDocument.Parse(j3))
            {
                var item = doc.RootElement.GetProperty("data").GetProperty("item");
                if (item.TryGetProperty("body", out var bodyEl))
                    Assert.Equal(JsonValueKind.Null, bodyEl.ValueKind);
            }

            var (cList, listLine) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "list", "--db", db, "--parent", "AF-1", "--json"
            });
            Assert.Equal(0, cList);
            using var listDoc = JsonDocument.Parse(listLine);
            Assert.True(listDoc.RootElement.TryGetProperty("data", out var data));
            Assert.Equal(JsonValueKind.Array, data.ValueKind);
            Assert.Equal(1, data.GetArrayLength());
            Assert.Equal("AF-2", data[0].GetProperty("ref").GetString());
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
            try { File.Delete(bodyPath); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Item_update_requires_xor_id_or_ref()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-itemcli2-{Guid.NewGuid():N}.db");
        try
        {
            var parser = CreateParser();
            Assert.Equal(0, await parser.InvokeAsync(new[] { "init", "--db", db }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--type", "task", "--title", "T", "--json"
            }));
            Assert.NotEqual(0, await parser.InvokeAsync(new[]
            {
                "item", "update", "--db", db, "--title", "x", "--json"
            }));
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Item_list_filters_assigned_to_body_contains_updated_after_and_rejects_bad_timestamp()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-itemcli-filters-{Guid.NewGuid():N}.db");
        try
        {
            var parser = CreateParser();
            Assert.Equal(0, await parser.InvokeAsync(new[] { "init", "--db", db }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--type", "task", "--title", "Mine", "--json"
            }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "update", "--db", db, "--ref", "AF-1", "--assigned-to", "agent:x", "--json"
            }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--type", "task", "--title", "Yours", "--json"
            }));

            var (cAsg, jAsg) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "list", "--db", db, "--assigned-to", "agent:x", "--json"
            });
            Assert.Equal(0, cAsg);
            using (var doc = JsonDocument.Parse(jAsg))
            {
                var data = doc.RootElement.GetProperty("data");
                Assert.Equal(JsonValueKind.Array, data.ValueKind);
                Assert.Equal(1, data.GetArrayLength());
                Assert.Equal("AF-1", data[0].GetProperty("ref").GetString());
            }

            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "update", "--db", db, "--ref", "AF-2", "--body", "needleUnique_q9z", "--json"
            }));
            var (cBc, jBc) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "list", "--db", db, "--body-contains", "needleUnique_q9z", "--json"
            });
            Assert.Equal(0, cBc);
            using (var doc = JsonDocument.Parse(jBc))
            {
                var data = doc.RootElement.GetProperty("data");
                Assert.Equal(1, data.GetArrayLength());
                Assert.Equal("AF-2", data[0].GetProperty("ref").GetString());
            }

            Assert.Equal(3, await parser.InvokeAsync(new[]
            {
                "item", "list", "--db", db, "--updated-after", "not-a-date", "--json"
            }));

            await Task.Delay(1200);
            var mid = DateTimeOffset.UtcNow.ToString("O");
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--type", "task", "--title", "Late", "--json"
            }));
            var (cUa, jUa) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "list", "--db", db, "--updated-after", mid, "--json"
            });
            Assert.Equal(0, cUa);
            using (var doc = JsonDocument.Parse(jUa))
            {
                var data = doc.RootElement.GetProperty("data");
                Assert.Equal(1, data.GetArrayLength());
                Assert.Equal("AF-3", data[0].GetProperty("ref").GetString());
            }
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }

    private static Task<(int Code, string JsonLine)> InvokeStdoutJsonLine(Parser parser, string[] args)
    {
        lock (ConsoleRedirectGate)
        {
            var prev = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                var code = parser.InvokeAsync(args).GetAwaiter().GetResult();
                var text = sw.ToString();
                var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .LastOrDefault(s => s.Length > 0 && s[0] == '{');
                return Task.FromResult((code, line ?? ""));
            }
            finally
            {
                Console.SetOut(prev);
            }
        }
    }
}
