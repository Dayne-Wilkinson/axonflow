using Microsoft.Data.Sqlite;

namespace AxonFlow;

internal static class DatabaseBootstrap
{
    /// <summary>Creates DB dir, migrations, and default project if missing.</summary>
    public static void EnsureInitialized(string dbPath)
    {
        Paths.EnsureDbDirectory(dbPath);
        var full = Path.GetFullPath(dbPath);
        var existed = File.Exists(full);
        var cs = $"Data Source={full};Mode={(existed ? "ReadWrite" : "ReadWriteCreate")}";
        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            Migration.ApplyInitialSchema(conn);
        }

        var storeCs = $"Data Source={full};Mode=ReadWrite";
        var store = new Store(storeCs);
        using var c = store.Open();
        if (store.CountProjects(c) == 0)
            store.InsertDefaultProject(c);
    }

    /// <summary>
    /// When <paramref name="allowCreate"/> is false and the file does not exist, fails (e.g. <c>--dry-run</c>).
    /// </summary>
    public static bool TryPrepare(string dbPath, bool allowCreate, out string? errorMessage)
    {
        errorMessage = null;
        if (!File.Exists(dbPath))
        {
            if (!allowCreate)
            {
                errorMessage =
                    "No database file yet; run once without --dry-run to create the default database (~/.axonflow/axonflow.db), or pass an existing file with --db.";
                return false;
            }

            EnsureInitialized(dbPath);
            return true;
        }

        return true;
    }
}
