using System.Text.Json;
using System.Text.Json.Serialization;

namespace AxonFlow;

public static class JsonOut
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static void WriteOk(object data, TextWriter? writer = null)
    {
        writer ??= Console.Out;
        writer.WriteLine(JsonSerializer.Serialize(new { ok = true, data }, Options));
    }

    public static void WriteErr(string code, string message, object? details = null, TextWriter? writer = null)
    {
        writer ??= Console.Error;
        writer.WriteLine(JsonSerializer.Serialize(new { error = new { code, message, details } }, Options));
    }

    public static void WriteText(string line, TextWriter? writer = null)
    {
        writer ??= Console.Out;
        writer.WriteLine(line);
    }
}
