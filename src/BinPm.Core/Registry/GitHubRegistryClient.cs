using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BinPm.Core.Install;
using BinPm.Core.Models;
using BinPm.Core.Semver;

namespace BinPm.Core.Registry;

/// <summary>
/// GitHub-backed registry. Package names use the <c>owner/repo</c> form.
/// BinPm first looks for a <c>binpm.json</c> manifest in the repository; when that is not present
/// it derives a manifest automatically from the repository's releases.
/// </summary>
public sealed class GitHubRegistryClient : IRegistryClient
{
    private readonly HttpClient _http;
    private readonly string? _token;
    private readonly string _ref;

    public GitHubRegistryClient(HttpClient http, string? token = null, string? @ref = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _token = string.IsNullOrWhiteSpace(token) ? null : token;
        _ref = string.IsNullOrWhiteSpace(@ref) ? "main" : @ref;

        _http.DefaultRequestHeaders.UserAgent.ParseAdd("binpm");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        if (_token is not null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    public string Kind => "github";

    public string DisplayName => "github";

    public string DescribePackage(string name) => $"github:{name}";

    public async Task<PackageManifest> GetPackageAsync(string name, CancellationToken ct = default)
    {
        var (owner, repo) = SplitName(name);
        var escapedRef = Uri.EscapeDataString(_ref);

        // 1. Explicit manifest in the repository.
        var manifestUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{escapedRef}/binpm.json";
        try
        {
            var json = await GetStringAsync(manifestUrl, $"manifest for '{name}'", ct);
            var manifest = JsonDefaults.Deserialize<PackageManifest>(json);
            if (manifest is null)
                throw new BinPmException($"The manifest at '{manifestUrl}' is not valid JSON.");

            if (string.IsNullOrWhiteSpace(manifest.Name))
                manifest.Name = repo;

            return manifest;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // 2. Fall back to release auto-detection.
        }

        return await BuildManifestFromReleasesAsync(owner, repo, ct);
    }

    public async Task<IReadOnlyList<PackageSummary>> SearchAsync(string term, CancellationToken ct = default)
    {
        var url = $"https://api.github.com/search/repositories?q={Uri.EscapeDataString(term)}&per_page=50";
        var json = await GetStringAsync(url, $"search results for '{term}'", ct);

        try
        {
            using var document = JsonDocument.Parse(json);
            var results = new List<PackageSummary>();

            if (document.RootElement.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var fullName = item.GetProperty("full_name").GetString();
                    if (string.IsNullOrEmpty(fullName))
                        continue;

                    var description = item.TryGetProperty("description", out var d) ? d.GetString() : null;

                    results.Add(new PackageSummary
                    {
                        Name = fullName,
                        Description = description,
                        Registry = DisplayName,
                    });
                }
            }

            return results;
        }
        catch (JsonException ex)
        {
            throw new BinPmException($"GitHub returned an unexpected response for '{term}'.", ex);
        }
    }

    private async Task<PackageManifest> BuildManifestFromReleasesAsync(string owner, string repo, CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=100";
        var json = await GetStringAsync(url, $"releases for '{owner}/{repo}'", ct);

        var manifest = new PackageManifest
        {
            Name = repo,
            Repository = $"https://github.com/{owner}/{repo}",
            IsSynthetic = true,
        };

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var messageElement))
            {
                throw new BinPmException(
                    $"GitHub API error for '{owner}/{repo}': {messageElement.GetString()}");
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                throw new BinPmException($"GitHub returned an unexpected response for '{owner}/{repo}'.");
            }

            foreach (var release in root.EnumerateArray())
            {
                var tagName = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
                if (string.IsNullOrWhiteSpace(tagName))
                    continue;

                if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                    continue;

                if (!TryParseReleaseTag(tagName, out var version))
                    continue;

                var packageVersion = new PackageVersion
                {
                    Version = version.ToString(),
                    Notes = release.TryGetProperty("body", out var body) ? body.GetString() : null,
                    Published = release.TryGetProperty("published_at", out var published) &&
                                DateTimeOffset.TryParse(published.GetString(), out var date)
                        ? date
                        : null,
                };

                if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var assetName = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                        if (string.IsNullOrWhiteSpace(assetName))
                            continue;

                        var downloadUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() : null;
                        if (string.IsNullOrWhiteSpace(downloadUrl))
                            continue;

                        var mapped = new Asset
                        {
                            Name = assetName,
                            Url = downloadUrl,
                            Size = asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var sizeValue)
                                ? sizeValue
                                : null,
                            Sha256 = NormalizeDigest(asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null),
                        };

                        // For single-file executables we can infer the binary name reliably.
                        if (!ArchiveExtraction.IsArchiveFileName(assetName) &&
                            !AssetClassification.IsMetadataFileName(assetName))
                        {
                            var binName = BinaryInstaller.GetAssetBinaryName(assetName);
                            if (!string.IsNullOrEmpty(binName))
                                mapped.Bins = new List<string> { binName };
                        }

                        packageVersion.Assets.Add(mapped);
                    }
                }

                manifest.Versions.Add(packageVersion);
            }

            if (manifest.Versions.Count == 0)
            {
                throw new BinPmException(
                    $"'{owner}/{repo}' has no usable releases and no binpm.json manifest. " +
                    "Add a binpm.json to the repository or publish releases with version-like tags.");
            }

            return manifest;
        }
        catch (JsonException ex)
        {
            throw new BinPmException($"GitHub returned an unexpected response for '{owner}/{repo}'.", ex);
        }
    }

    /// <summary>
    /// Parses a release tag into a version. Tags such as <c>v1.2.3</c>, <c>jq-1.7.1</c> or
    /// <c>release-2.0</c> all carry a version behind a prefix.
    /// </summary>
    public static bool TryParseReleaseTag(string? tag, out SemVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var text = tag.Trim();

        if (SemVersion.TryParse(text, out version))
            return true;

        var index = 0;
        while (index < text.Length && !char.IsDigit(text[index]))
            index++;

        if (index > 0 && index < text.Length)
            return SemVersion.TryParse(text.Substring(index), out version);

        version = default;
        return false;
    }

    private static string? NormalizeDigest(string? digest)    {
        if (string.IsNullOrWhiteSpace(digest))
            return null;

        const string prefix = "sha256:";
        return digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? digest.Substring(prefix.Length)
            : digest;
    }

    private static (string Owner, string Repo) SplitName(string name)
    {
        var parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new BinPmException(
                $"GitHub packages must be specified as 'owner/repo' (got '{name}').");
        }

        return (parts[0], parts[1]);
    }

    private async Task<string> GetStringAsync(string url, string description, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new HttpRequestException($"{description} not found.", null, HttpStatusCode.NotFound);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var remaining = response.Headers.TryGetValues("x-ratelimit-remaining", out var values)
                ? values.FirstOrDefault()
                : null;

            if (remaining == "0")
            {
                throw new BinPmException(
                    "GitHub API rate limit exceeded. Set 'githubToken' in the config file or use a registry manifest instead.");
            }
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
