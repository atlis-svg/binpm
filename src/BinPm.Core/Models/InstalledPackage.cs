using System.Text.Json.Serialization;

namespace BinPm.Core.Models;

/// <summary>A record of a package installation stored in the local package database.</summary>
public sealed class InstalledPackage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("installedAt")]
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Where the package came from, e.g. <c>github:owner/repo</c> or <c>http:mirror:ripgrep</c>.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("assetName")]
    public string AssetName { get; set; } = "";

    /// <summary>Absolute path of the extracted package directory.</summary>
    [JsonPropertyName("installDir")]
    public string InstallDir { get; set; } = "";

    /// <summary>Paths of the shimmed binaries, relative to the extracted package directory.</summary>
    [JsonPropertyName("bins")]
    public List<string> Bins { get; set; } = new();

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }
}
