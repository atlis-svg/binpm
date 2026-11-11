using System.Text.Json.Serialization;

namespace BinPm.Core.Models;

/// <summary>A downloadable file attached to a package version.</summary>
public sealed class Asset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Absolute download URL. When omitted, <see cref="PackageManifest.AssetUrlTemplate"/> is used.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    /// <summary>Explicit platform ids this asset targets, e.g. <c>linux-x64</c>. Takes precedence over name matching.</summary>
    [JsonPropertyName("platforms")]
    public List<string>? Platforms { get; set; }

    /// <summary>Paths (relative to the extracted package root) of binaries that should be shimmed.</summary>
    [JsonPropertyName("bins")]
    public List<string>? Bins { get; set; }

    /// <summary>Optional hint: the asset is a self-contained executable rather than an archive.</summary>
    [JsonPropertyName("executable")]
    public bool? Executable { get; set; }
}
