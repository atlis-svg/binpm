using System.Text.Json.Serialization;

namespace BinPm.Core.Models;

public sealed class PackageVersion
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("published")]
    public DateTimeOffset? Published { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("assets")]
    public List<Asset> Assets { get; set; } = new();
}
