using BinPm.Core.Models;

namespace BinPm.Core.Registry;

/// <summary>A source of package manifests, e.g. a static HTTP registry or GitHub.</summary>
public interface IRegistryClient
{
    /// <summary>Short kind identifier: <c>http</c> or <c>github</c>.</summary>
    string Kind { get; }

    /// <summary>Human-readable label, e.g. <c>http:mirror</c> or <c>github</c>.</summary>
    string DisplayName { get; }

    /// <summary>Fetches the manifest for a package name understood by this registry.</summary>
    Task<PackageManifest> GetPackageAsync(string name, CancellationToken ct = default);

    /// <summary>Searches for packages matching <paramref name="term"/>.</summary>
    Task<IReadOnlyList<PackageSummary>> SearchAsync(string term, CancellationToken ct = default);

    /// <summary>Builds the source string stored in the local database for a package.</summary>
    string DescribePackage(string name);
}
