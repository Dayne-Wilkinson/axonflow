using System.CommandLine.Builder;
using System.CommandLine.Parsing;

namespace AxonFlow.Tests;

public class DatabaseBootstrapCliTests
{
    private static Parser CreateParser() =>
        new CommandLineBuilder(CliRoot.Build()).UseDefaults().Build();

    [Fact]
    public async Task Dry_run_does_not_create_database_file_on_first_touch()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-dry-{Guid.NewGuid():N}.db");
        try
        {
            var parser = CreateParser();
            var code = await parser.InvokeAsync(new[]
            {
                "item", "list", "--db", db, "--project", "default", "--dry-run", "--json"
            });
            Assert.NotEqual(0, code);
            Assert.False(File.Exists(db));
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Item_add_without_init_creates_database()
    {
        var db = Path.Combine(Path.GetTempPath(), $"axonflow-auto-{Guid.NewGuid():N}.db");
        try
        {
            var parser = CreateParser();
            Assert.Equal(0, await parser.InvokeAsync(new[]
            {
                "item", "add", "--db", db, "--project", "default",
                "--type", "task", "--title", "Bootstrapped", "--json"
            }));
            Assert.True(File.Exists(db));
        }
        finally
        {
            try { File.Delete(db); } catch { /* ignore */ }
        }
    }
}
