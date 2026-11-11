using BinPm.Core.Models;
using BinPm.Core.Semver;

namespace BinPm.Core.Install;

/// <summary>Selects a version and asset from a package manifest for a target platform.</summary>
public sealed class PackageResolver
{
    private readonly Platform _platform;

    public PackageResolver(Platform? platform = null)
    {
        _platform = platform ?? Platform.Current;
    }

    public Platform Platform => _platform;

    /// <summary>Resolves the manifest to a specific version and asset.</summary>
    public ResolvedPackage Resolve(PackageManifest manifest, string? versionSpec, string source, string? assetFilter = null)
    {
        if (manifest.Versions.Count == 0)
            throw new BinPmException($"Package '{manifest.Name}' has no versions.");

        var range = VersionRange.Parse(versionSpec ?? manifest.DefaultVersion);
        var sortedVersions = manifest.GetSortedVersions().ToList();
        var matching = range.Select(sortedVersions).ToList();

        if (matching.Count == 0)
        {
            var spec = versionSpec ?? manifest.DefaultVersion ?? "latest";
            var available = string.Join(", ", sortedVersions.Take(10));
            throw new BinPmException(
                $"No version of '{manifest.Name}' matches '{spec}'. Available versions: {available}");
        }

        var selectedVersion = matching[0];
        var packageVersion = manifest.FindVersion(selectedVersion);

        if (packageVersion is null)
            throw new BinPmException($"Version '{selectedVersion}' is listed but could not be resolved.");

        if (packageVersion.Assets.Count == 0)
            throw new BinPmException($"Version '{selectedVersion}' of '{manifest.Name}' has no assets.");

        var (asset, matchedPlatform) = SelectAsset(packageVersion, assetFilter);
        return new ResolvedPackage(manifest, packageVersion, asset, source, matchedPlatform);
    }

    /// <summary>Picks the best asset for the target platform.</summary>
    public (Asset Asset, bool MatchedPlatform) SelectAsset(PackageVersion version, string? assetFilter = null)
    {
        var assets = version.Assets;

        if (!string.IsNullOrWhiteSpace(assetFilter))
        {
            var filtered = assets
                .Where(a => a.Name.Contains(assetFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (filtered.Count == 0)
            {
                throw new BinPmException(
                    $"No asset matching '{assetFilter}' in version {version.Version}. " +
                    $"Available: {DescribeAssets(assets)}");
            }

            if (filtered.Count == 1)
                return (filtered[0], AssetMatchesPlatform(filtered[0]));

            var best = PickBestScore(filtered);
            if (best is not null)
                return (best, true);

            throw new BinPmException(
                $"Multiple assets match '{assetFilter}' but none targets { _platform }. " +
                $"Candidates: {DescribeAssets(filtered)}");
        }

        // 1. Explicit platform declarations win.
        var explicitMatches = assets
            .Where(a => a.Platforms?.Any(p => Platform.TryParse(p, out var platform) && platform == _platform) == true)
            .ToList();

        if (explicitMatches.Count == 1)
            return (explicitMatches[0], true);

        if (explicitMatches.Count > 1)
        {
            throw new BinPmException(
                $"Multiple assets declare the platform {_platform}: {DescribeAssets(explicitMatches)}");
        }

        // Metadata sidecars (checksums, signatures) are never installable payloads.
        var installable = assets.Where(a => AssetClassification.IsInstallableAsset(a.Name)).ToList();
        if (installable.Count == 0)
            installable = assets;

        // 2. Match by asset name.
        var bestMatch = PickBestScore(installable);
        if (bestMatch is not null)
            return (bestMatch, true);

        // 3. A single asset that says nothing about platforms is assumed to be platform-independent.
        if (installable.Count == 1 && !Platform.HasPlatformMarkers(installable[0].Name))
            return (installable[0], false);

        throw new BinPmException(
            $"No asset for {_platform} in version {version.Version}. Available: {DescribeAssets(assets)}");
    }

    private Asset? PickBestScore(IReadOnlyList<Asset> assets)
    {
        Asset? best = null;
        var bestScore = 0;
        var bestSegments = int.MaxValue;

        foreach (var asset in assets)
        {
            if (!AssetClassification.IsInstallableAsset(asset.Name))
                continue;

            var score = Platform.Score(asset.Name, _platform);
            if (score <= 0)
                continue;

            // Projects sometimes publish one archive per binary ("atuin", "atuin-client", ...).
            // The main binary has the fewest name segments before the platform decoration.
            var segments = asset.Name.Split('-', '_', '.').Length;

            if (best is null || score > bestScore || (score == bestScore && segments < bestSegments))
            {
                best = asset;
                bestScore = score;
                bestSegments = segments;
            }
        }

        return best;
    }

    private bool AssetMatchesPlatform(Asset asset)
    {
        if (asset.Platforms is not null)
        {
            return asset.Platforms.Any(p => Platform.TryParse(p, out var platform) && platform == _platform);
        }

        return Platform.TryMatch(asset.Name, _platform, out _);
    }

    private static string DescribeAssets(IReadOnlyList<Asset> assets) =>
        string.Join(", ", assets.Select(a => a.Name));
}
