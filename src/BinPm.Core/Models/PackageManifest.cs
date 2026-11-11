using System.Text.Json.Serialization;
using BinPm.Core.Semver;

namespace BinPm.Core.Models;

/// <summary>
/// Describes a package and the assets published for each of its versions.
/// See the repository README for the full manifest format.
/// </summary>
public sealed class PackageManifest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    /// <summary>
    /// Optional URL template used to build asset download URLs.
    /// Supported placeholders: <c>{version}</c> and <c>{name}</c>.
    /// </summary>
    [JsonPropertyName("assetUrlTemplate")]
    public string? AssetUrlTemplate { get; set; }

    /// <summary>Binary paths applied to every version when an asset does not declare its own.</summary>
    [JsonPropertyName("binaries")]
    public List<string>? Binaries { get; set; }

    /// <summary>Version selection used when the user does not specify one. Defaults to the highest version.</summary>
    [JsonPropertyName("defaultVersion")]
    public string? DefaultVersion { get; set; }

    [JsonPropertyName("versions")]
    public List<PackageVersion> Versions { get; set; } = new();

    /// <summary>True when this manifest was synthesized from a remote source (e.g. GitHub releases) rather than read verbatim.</summary>
    [JsonIgnore]
    public bool IsSynthetic { get; set; }

    /// <summary>All versions, highest first.</summary>
    public IEnumerable<SemVersion> GetSortedVersions()
    {
        var parsed = new List<SemVersion>();
        foreach (var v in Versions)
        {
            if (SemVersion.TryParse(v.Version, out var semver))
                parsed.Add(semver);
        }

        return parsed.OrderByDescending(v => v);
    }

    public PackageVersion? FindVersion(SemVersion version) =>
        Versions.FirstOrDefault(v => SemVersion.TryParse(v.Version, out var semver) && semver == version);
}
