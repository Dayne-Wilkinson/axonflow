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

    /// <summary>Static HTML/cache for dashboard Kestrel (not used as source snapshot after load).</summary>
    public static DirectoryInfo DashboardServeCacheDirectory()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".axonflow",
            "dashboard-cache");
        return new DirectoryInfo(path);
    }
}
