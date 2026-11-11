using BinPm.Core.Install;
using BinPm.Core.Models;

namespace BinPm.Core.Tests;

public class PackageResolverTests
{
    private static PackageManifest BuildManifest() => new()
    {
        Name = "demo",
        Versions = new List<PackageVersion>
        {
            new()
            {
                Version = "1.0.0",
                Assets = new List<Asset>
                {
                    new() { Name = "demo-1.0.0-linux-x64.tar.gz", Url = "http://x/1.0.0.tar.gz" },
                    new() { Name = "demo-1.0.0-windows-x64.zip", Url = "http://x/1.0.0.zip" },
                },
            },
            new()
            {
                Version = "1.2.0",
                Assets = new List<Asset>
                {
                    new() { Name = "demo-1.2.0-linux-x64.tar.gz", Url = "http://x/1.2.0.tar.gz" },
                    new() { Name = "demo-1.2.0-windows-x64.zip", Url = "http://x/1.2.0-win.zip" },
                    new() { Name = "demo-1.2.0-linux-x64.tar.gz.sha256", Url = "http://x/1.2.0.sha256" },
                },
            },
            new()
            {
                Version = "2.0.0",
                Assets = new List<Asset>
                {
                    new() { Name = "demo-2.0.0-linux-x64.tar.gz", Url = "http://x/2.0.0.tar.gz" },
                },
            },
        },
    };

    [Fact]
    public void Picks_the_highest_version_by_default()
    {
        var resolver = new PackageResolver(new Platform(OsKind.Linux, ArchKind.X64));

        var resolved = resolver.Resolve(BuildManifest(), null, "test:demo");

        Assert.Equal("2.0.0", resolved.PackageVersion.Version);
        Assert.Equal("demo-2.0.0-linux-x64.tar.gz", resolved.Asset.Name);
    }

    [Fact]
    public void Honours_an_exact_version()
    {
        var resolver = new PackageResolver(new Platform(OsKind.Linux, ArchKind.X64));

        var resolved = resolver.Resolve(BuildManifest(), "1.2.0", "test:demo");

        Assert.Equal("1.2.0", resolved.PackageVersion.Version);
    }

    [Fact]
    public void Honours_a_version_range()
    {
        var resolver = new PackageResolver(new Platform(OsKind.Linux, ArchKind.X64));

        var resolved = resolver.Resolve(BuildManifest(), "^1.0.0", "test:demo");

        Assert.Equal("1.2.0", resolved.PackageVersion.Version);
    }

    [Fact]
    public void Throws_when_no_version_matches()
    {
        var resolver = new PackageResolver(new Platform(OsKind.Linux, ArchKind.X64));

        var exception = Assert.Throws<BinPmException>(() =>
            resolver.Resolve(BuildManifest(), "3.0.0", "test:demo"));

        Assert.Contains("No version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Selects_the_asset_for_the_target_platform()
    {
        var resolver = new PackageResolver(new Platform(OsKind.Linux, ArchKind.X64));
        var manifest = BuildManifest();

        var resolved = resolver.Resolve(manifest, "1.0.0", "test:demo");

        Assert.Contains("linux-x64", resolved.Asset.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Skips_checksum_sidecars()
    {
        var resolver = new PackageResolver(new Platform(OsKind.Linux, ArchKind.X64));

        var resolved = resolver.Resolve(BuildManifest(), "1.2.0", "test:demo");

        Assert.DoesNotContain(".sha256", resolved.Asset.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Throws_when_no_asset_matches_the_platform()
    {
        var resolver = new PackageResolver(new Platform(OsKind.Linux, ArchKind.Arm64));
        var manifest = new PackageManifest
        {
            Name = "demo",
            Versions = new List<PackageVersion>
            {
                new()
                {
                    Version = "1.0.0",
                    Assets = new List<Asset> { new() { Name = "demo-windows-x64.zip" } },
                },
            },
        };

        var exception = Assert.Throws<BinPmException>(() => resolver.Resolve(manifest, null, "test:demo"));

        Assert.Contains("No asset", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicit_platform_declarations_win_over_name_matching()
    {
        var resolver = new PackageResolver(new Platform(OsKind.Linux, ArchKind.X64));
        var manifest = new PackageManifest
        {
            Name = "demo",
            Versions = new List<PackageVersion>
            {
                new()
                {
                    Version = "1.0.0",
                    Assets = new List<Asset>
                    {
                        new() { Name = "demo-generic.tar.gz", Platforms = new List<string> { "windows-x64" } },
                        new() { Name = "demo-special.tar.gz", Platforms = new List<string> { "linux-x64" } },
                    },
                },
            },
        };

        var resolved = resolver.Resolve(manifest, null, "test:demo");

        Assert.Equal("demo-special.tar.gz", resolved.Asset.Name);
    }

    [Fact]
    public void An_asset_filter_selects_a_specific_asset()
    {
        var resolver = new PackageResolver(new Platform(OsKind.Windows, ArchKind.X64));

        var resolved = resolver.Resolve(BuildManifest(), "1.0.0", "test:demo", "windows");

        Assert.Contains("windows", resolved.Asset.Name, StringComparison.OrdinalIgnoreCase);
    }
}
