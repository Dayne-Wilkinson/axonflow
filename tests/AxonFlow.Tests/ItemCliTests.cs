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
                "item", "add", "--db", db, "--project", "default", "--type", "story", "--title", "Parent story", "--status", "ready", "--json"
            }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--project", "default", "--type", "task", "--title", "Child", "--parent", "AF-1",
                "--body", "inline spec", "--status", "ready", "--json"
            }));

            var (cShow1, j1) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "show", "--db", db, "--project", "default", "--ref", "AF-2", "--json"
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
                "item", "update", "--db", db, "--project", "default", "--ref", "AF-2", "--body-file", bodyPath, "--json"
            }));
            var (cShow2, j2) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "show", "--db", db, "--project", "default", "--ref", "AF-2", "--json"
            });
            Assert.Equal(0, cShow2);
            using (var doc = JsonDocument.Parse(j2))
            {
                var item = doc.RootElement.GetProperty("data").GetProperty("item");
                Assert.Equal("from file", item.GetProperty("body").GetString());
            }

            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "update", "--db", db, "--project", "default", "--ref", "AF-2", "--clear-body", "--json"
            }));
            var (cShow3, j3) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "show", "--db", db, "--project", "default", "--ref", "AF-2", "--json"
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
                "item", "list", "--db", db, "--project", "default", "--parent", "AF-1", "--json"
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
                "item", "add", "--db", db, "--project", "default", "--type", "task", "--title", "T", "--json"
            }));
            Assert.NotEqual(0, await parser.InvokeAsync(new[]
            {
                "item", "update", "--db", db, "--project", "default", "--title", "x", "--json"
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
                "item", "add", "--db", db, "--project", "default", "--type", "task", "--title", "Mine", "--json"
            }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "update", "--db", db, "--project", "default", "--ref", "AF-1", "--assigned-to", "agent:x", "--json"
            }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--project", "default", "--type", "task", "--title", "Yours", "--json"
            }));

            var (cAsg, jAsg) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "list", "--db", db, "--project", "default", "--assigned-to", "agent:x", "--json"
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
                "item", "update", "--db", db, "--project", "default", "--ref", "AF-2", "--body", "needleUnique_q9z", "--json"
            }));
            var (cBc, jBc) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "list", "--db", db, "--project", "default", "--body-contains", "needleUnique_q9z", "--json"
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
                "item", "list", "--db", db, "--project", "default", "--updated-after", "not-a-date", "--json"
            }));

            await Task.Delay(1200);
            var mid = DateTimeOffset.UtcNow.ToString("O");
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--project", "default", "--type", "task", "--title", "Late", "--json"
            }));
            var (cUa, jUa) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "list", "--db", db, "--project", "default", "--updated-after", mid, "--json"
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

    [Fact]
    public async Task Item_lifecycle_enforces_start_before_complete_and_defer_sets_blocked_then_ready()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-itemcli-lifecycle-{Guid.NewGuid():N}.db");
        try
        {
            var parser = CreateParser();
            Assert.Equal(0, await parser.InvokeAsync(new[] { "init", "--db", db }));

            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--project", "default", "--type", "task", "--title", "Lifecycle item", "--json"
            }));

            Assert.Equal(2, await parser.InvokeAsync(new[]
            {
                "item", "complete", "--db", db, "--project", "default", "--ref", "AF-1", "--json"
            }));

            var (deferCode, deferJson) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "defer", "--db", db, "--project", "default", "--ref", "AF-1", "--until", "2030-01-01T00:00:00Z", "--json"
            });
            Assert.Equal(0, deferCode);
            using (var doc = JsonDocument.Parse(deferJson))
            {
                Assert.Equal("blocked", doc.RootElement.GetProperty("data").GetProperty("status").GetString());
            }

            var (clearCode, clearJson) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "defer", "--db", db, "--project", "default", "--ref", "AF-1", "--clear", "--json"
            });
            Assert.Equal(0, clearCode);
            using (var doc = JsonDocument.Parse(clearJson))
            {
                Assert.Equal("ready", doc.RootElement.GetProperty("data").GetProperty("status").GetString());
            }

            var (startCode, startJson) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "start", "--db", db, "--project", "default", "--ref", "AF-1", "--assignee", "agent:test", "--json"
            });
            Assert.Equal(0, startCode);
            using (var doc = JsonDocument.Parse(startJson))
            {
                Assert.Equal("in_progress", doc.RootElement.GetProperty("data").GetProperty("status").GetString());
            }

            var (completeCode, completeJson) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "complete", "--db", db, "--project", "default", "--ref", "AF-1", "--json"
            });
            Assert.Equal(0, completeCode);
            using (var doc = JsonDocument.Parse(completeJson))
            {
                Assert.Equal("done", doc.RootElement.GetProperty("data").GetProperty("status").GetString());
            }
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Item_start_requires_predecessors_unless_forced()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-itemcli-start-force-{Guid.NewGuid():N}.db");
        try
        {
            var parser = CreateParser();
            Assert.Equal(0, await parser.InvokeAsync(new[] { "init", "--db", db }));

            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--project", "default", "--type", "task", "--title", "Pred", "--json"
            }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--project", "default", "--type", "task", "--title", "Succ", "--json"
            }));
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "dep", "add", "--db", db, "--project", "default", "--predecessor", "AF-1", "--successor", "AF-2", "--json"
            }));

            Assert.Equal(2, await parser.InvokeAsync(new[]
            {
                "item", "start", "--db", db, "--project", "default", "--ref", "AF-2", "--assignee", "agent:test", "--json"
            }));

            var (forcedCode, forcedJson) = await InvokeStdoutJsonLine(parser, new[]
            {
                "item", "start", "--db", db, "--project", "default", "--ref", "AF-2", "--assignee", "agent:test", "--force", "--json"
            });
            Assert.Equal(0, forcedCode);
            using var doc = JsonDocument.Parse(forcedJson);
            Assert.Equal("in_progress", doc.RootElement.GetProperty("data").GetProperty("status").GetString());
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
