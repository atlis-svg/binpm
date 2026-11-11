using System.Text.Json;
using BinPm.Core.Models;

namespace BinPm.Core.Registry;

/// <summary>
/// A registry served as static JSON over HTTP. Expected layout:
/// <c>{baseUrl}/index.json</c> listing packages and <c>{baseUrl}/{package}/manifest.json</c> per package.
/// </summary>
public sealed class HttpRegistryClient : IRegistryClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _name;

    public HttpRegistryClient(HttpClient http, string baseUrl, string? name = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _baseUrl = (baseUrl ?? throw new ArgumentNullException(nameof(baseUrl))).TrimEnd('/');
        _name = name ?? new Uri(baseUrl).Host;
    }

    public string Kind => "http";

    public string DisplayName => $"http:{_name}";

    public string DescribePackage(string name) => $"{DisplayName}:{name}";

    public async Task<PackageManifest> GetPackageAsync(string name, CancellationToken ct = default)
    {
        var manifestUrl = await ResolveManifestUrlAsync(name, ct);
        var json = await GetStringAsync(manifestUrl, $"manifest for '{name}'", ct);

        var manifest = JsonDefaults.Deserialize<PackageManifest>(json);
        if (manifest is null)
            throw new BinPmException($"The manifest at '{manifestUrl}' is not valid JSON.");

        if (string.IsNullOrWhiteSpace(manifest.Name))
            manifest.Name = name;

        ResolveAssetUrls(manifest);
        return manifest;
    }

    public async Task<IReadOnlyList<PackageSummary>> SearchAsync(string term, CancellationToken ct = default)
    {
        var index = await LoadIndexAsync(ct);
        var results = new List<PackageSummary>();

        foreach (var entry in index)
        {
            var matches = entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                          (entry.Description ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                results.Add(new PackageSummary
                {
                    Name = entry.Name,
                    Description = entry.Description,
                    Registry = DisplayName,
                });
            }
        }

        return results;
    }

    private void ResolveAssetUrls(PackageManifest manifest)
    {
        if (string.IsNullOrEmpty(manifest.AssetUrlTemplate))
            return;

        foreach (var version in manifest.Versions)
        {
            foreach (var asset in version.Assets)
            {
                if (!string.IsNullOrEmpty(asset.Url))
                    continue;

                asset.Url = manifest.AssetUrlTemplate
                    .Replace("{version}", version.Version)
                    .Replace("{name}", asset.Name)
                    .Replace("{baseUrl}", _baseUrl);
            }
        }
    }

    private async Task<string> ResolveManifestUrlAsync(string name, CancellationToken ct)
    {
        var index = await LoadIndexAsync(ct);
        var entry = index.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (entry?.ManifestUrl is not null)
        {
            return IsAbsoluteUrl(entry.ManifestUrl)
                ? entry.ManifestUrl
                : new Uri(new Uri(_baseUrl + "/"), entry.ManifestUrl).ToString();
        }

        return $"{_baseUrl}/{Uri.EscapeDataString(name)}/manifest.json";
    }

    private async Task<List<IndexEntry>> LoadIndexAsync(CancellationToken ct)
    {
        var indexUrl = $"{_baseUrl}/index.json";
        List<IndexEntry>? entries = null;

        try
        {
            var json = await GetStringAsync(indexUrl, "package index", ct);
            try
            {
                entries = JsonDefaults.Deserialize<List<IndexEntry>>(json);
            }
            catch (JsonException)
            {
                var wrapper = JsonDefaults.Deserialize<IndexWrapper>(json);
                entries = wrapper?.Packages;
            }
        }
        catch (BinPmException)
        {
            // A registry without an index is still usable if manifests are fetched directly.
            entries = null;
        }

        return entries ?? new List<IndexEntry>();
    }

    private static bool IsAbsoluteUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out _);

    private async Task<string> GetStringAsync(string url, string description, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new BinPmException($"{description} not found at '{url}'.");

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private sealed class IndexWrapper
    {
        [System.Text.Json.Serialization.JsonPropertyName("packages")]
        public List<IndexEntry>? Packages { get; set; }
    }

    private sealed class IndexEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string? Description { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("manifestUrl")]
        public string? ManifestUrl { get; set; }
    }
}
