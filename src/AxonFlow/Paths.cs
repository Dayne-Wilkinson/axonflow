namespace AxonFlow;

public static class Paths
{
    public static string DefaultDbPath() =>
        Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".axonflow",
            "axonflow.db"));

    public static void EnsureDbDirectory(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}
