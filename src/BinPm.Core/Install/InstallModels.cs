using BinPm.Core.Models;

namespace BinPm.Core.Install;

/// <summary>A manifest version and asset selected for installation.</summary>
public sealed class ResolvedPackage
{
    public ResolvedPackage(PackageManifest manifest, PackageVersion packageVersion, Asset asset, string source, bool assetMatchedPlatform)
    {
        Manifest = manifest;
        PackageVersion = packageVersion;
        Asset = asset;
        Source = source;
        AssetMatchedPlatform = assetMatchedPlatform;
    }

    public PackageManifest Manifest { get; }
    public PackageVersion PackageVersion { get; }
    public Asset Asset { get; }

    /// <summary>Where the package came from, e.g. <c>github:owner/repo</c>.</summary>
    public string Source { get; }

    /// <summary>False when the asset had no platform information and was selected as a fallback.</summary>
    public bool AssetMatchedPlatform { get; }
}

/// <summary>A binary inside an extracted package that should get a shim.</summary>
public sealed class ResolvedBin
{
    public ResolvedBin(string name, string relativePath)
    {
        Name = name;
        RelativePath = relativePath;
    }

    /// <summary>Name of the shim (the command users type).</summary>
    public string Name { get; }

    /// <summary>Path of the binary relative to the package root.</summary>
    public string RelativePath { get; }
}

public sealed class ShimRequest
{
    public ShimRequest(string name, string targetPath)
    {
        Name = name;
        TargetPath = targetPath;
    }

    public string Name { get; }
    public string TargetPath { get; }
}

public enum ShimKind
{
    SymbolicLink,
    Script,
    WindowsCommand,
}

public sealed class ShimInfo
{
    public ShimInfo(string name, string path, ShimKind kind, bool existedAlready)
    {
        Name = name;
        Path = path;
        Kind = kind;
        ExistedAlready = existedAlready;
    }

    public string Name { get; }
    public string Path { get; }
    public ShimKind Kind { get; }
    public bool ExistedAlready { get; }
}

public sealed record InstallOptions
{
    /// <summary>Overrides the binary to shim (by path relative to the package root, or a file name).</summary>
    public string? BinOverride { get; init; }

    /// <summary>Reinstall even when the same version is already installed.</summary>
    public bool Force { get; init; }

    /// <summary>Skip shim creation.</summary>
    public bool NoShims { get; init; }

    /// <summary>Replace shims that belong to another package.</summary>
    public bool OverwriteShims { get; init; }

    /// <summary>Skip downloads by using the local cache (fail when a file is missing).</summary>
    public bool Offline { get; init; }
}

public sealed class InstallResult
{
    public InstallResult(InstalledPackage record, IReadOnlyList<ShimInfo> shims, bool alreadyInstalled)
    {
        Record = record;
        Shims = shims;
        AlreadyInstalled = alreadyInstalled;
    }

    public InstalledPackage Record { get; }
    public IReadOnlyList<ShimInfo> Shims { get; }
    public bool AlreadyInstalled { get; }

    public IReadOnlyList<string> BinNames => Record.Bins.Select(b => Path.GetFileName(b) ?? b).ToList();
}
