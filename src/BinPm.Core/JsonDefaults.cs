using System.Text.Json;
using System.Text.Json.Serialization;

namespace BinPm.Core;

/// <summary>Shared JSON serialization settings used across BinPm.</summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options);

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);
}
