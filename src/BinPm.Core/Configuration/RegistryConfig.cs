using System.Text.Json.Serialization;

namespace BinPm.Core.Configuration;

/// <summary>A configured package source.</summary>
public sealed class RegistryConfig
{
    /// <summary>Short name used to reference the registry, e.g. <c>github</c> or <c>mirror</c>.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Registry type: <c>http</c> or <c>github</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "http";

    /// <summary>Base URL for HTTP registries.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Default repository owner for GitHub registries (optional).</summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    /// <summary>Default repository name for GitHub registries (optional).</summary>
    [JsonPropertyName("repo")]
    public string? Repo { get; set; }

    /// <summary>Git ref (branch/tag) used when reading <c>binpm.json</c> from GitHub.</summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }
}
