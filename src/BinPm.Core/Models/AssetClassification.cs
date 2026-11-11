namespace BinPm.Core.Models;

/// <summary>Classifies asset file names so metadata files are not mistaken for installable payloads.</summary>
public static class AssetClassification
{
    private static readonly string[] MetadataExtensions =
    {
        ".sha256", ".sha512", ".sha384", ".sha1", ".md5",
        ".asc", ".sig", ".sign", ".minisig", ".pem",
        ".sbom", ".spdx", ".json", ".txt", ".checksum", ".checksums",
    };

    /// <summary>
    /// Operating-system package formats that BinPm cannot unpack. They are skipped when
    /// choosing an asset so that a tarball or zip is used instead.
    /// </summary>
    private static readonly string[] UnsupportedPackageExtensions =
    {
        ".deb", ".rpm", ".apk", ".msi", ".msix", ".pkg", ".dmg", ".snap",
    };

    /// <summary>True for checksum, signature and other metadata sidecar files.</summary>
    public static bool IsMetadataFileName(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        foreach (var extension in MetadataExtensions)
        {
            if (name.EndsWith(extension, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>True for packages BinPm cannot extract (deb, rpm, apk, msi, ...).</summary>
    public static bool IsUnsupportedPackageFormat(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        foreach (var extension in UnsupportedPackageExtensions)
        {
            if (name.EndsWith(extension, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>True for assets that can actually be installed.</summary>
    public static bool IsInstallableAsset(string fileName) =>
        !IsMetadataFileName(fileName) && !IsUnsupportedPackageFormat(fileName);
}
