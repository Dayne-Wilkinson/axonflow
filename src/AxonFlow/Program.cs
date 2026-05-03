using System.CommandLine;

namespace AxonFlow;

internal static class Program
{
    private static async Task<int> Main(string[] args) => await CliRoot.Build().InvokeAsync(args);
}
