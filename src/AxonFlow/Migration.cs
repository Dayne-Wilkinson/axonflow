using System.Reflection;
using Microsoft.Data.Sqlite;

namespace AxonFlow;

public static class Migration
{
    public static void ApplyInitialSchema(SqliteConnection conn)
    {
        var sql = ReadEmbeddedSql();
        using var tx = conn.BeginTransaction();
        foreach (var batch in SplitSqlBatches(sql))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = batch;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO schema_migrations(version, applied_at) VALUES (@v, @t);";
            cmd.Parameters.AddWithValue("@v", AppInfo.DbSchemaVersion);
            cmd.Parameters.AddWithValue("@t", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static string ReadEmbeddedSql()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("001_initial.sql", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException("Embedded migration 001_initial.sql not found.");
        using var s = asm.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Cannot open migration stream.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private static IEnumerable<string> SplitSqlBatches(string sql)
    {
        var lines = sql.Split('\n');
        var buf = new System.Text.StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.TrimStart().StartsWith("--")) continue;
            buf.AppendLine(line);
            if (line.TrimEnd().EndsWith(';'))
            {
                var batch = buf.ToString().Trim();
                buf.Clear();
                if (batch.Length > 0) yield return batch;
            }
        }
    }
}
