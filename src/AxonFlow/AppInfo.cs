namespace AxonFlow;

public static class AppInfo
{
    public const int DbSchemaVersion = 1;
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.3";
}
