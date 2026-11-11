using BinPm.Core.Models;

namespace BinPm.Core.Tests;

public class PlatformTests
{
    [Theory]
    [InlineData("ripgrep-14.1.1-x86_64-unknown-linux-musl.tar.gz", OsKind.Linux, ArchKind.X64)]
    [InlineData("ripgrep-14.1.1-i686-pc-windows-msvc.zip", OsKind.Windows, ArchKind.X86)]
    [InlineData("ripgrep-14.1.1-aarch64-apple-darwin.tar.gz", OsKind.MacOS, ArchKind.Arm64)]
    [InlineData("ripgrep-14.1.1-armv7-unknown-linux-gnueabihf.tar.gz", OsKind.Linux, ArchKind.Arm)]
    [InlineData("node-v20.11.0-win-x64.zip", OsKind.Windows, ArchKind.X64)]
    [InlineData("node-v20.11.0-linux-arm64.tar.gz", OsKind.Linux, ArchKind.Arm64)]
    [InlineData("tool-linux64", OsKind.Linux, ArchKind.X64)]
    [InlineData("tool-macos-x86_64", OsKind.MacOS, ArchKind.X64)]
    public void TryMatch_detects_the_platform_from_the_asset_name(
        string assetName, OsKind expectedOs, ArchKind expectedArch)
    {
        var target = new Platform(expectedOs, expectedArch);

        Assert.True(Platform.TryMatch(assetName, target, out var score));
        Assert.True(score > 0);
    }

    [Theory]
    [InlineData("ripgrep-14.1.1-x86_64-unknown-linux-musl.tar.gz")]
    [InlineData("some-tool.tar.gz")]
    [InlineData("readme.txt")]
    public void TryMatch_rejects_assets_for_other_platforms(string assetName)
    {
        var target = new Platform(OsKind.Linux, ArchKind.X64);
        var windows = new Platform(OsKind.Windows, ArchKind.X64);

        // Either the asset does not target linux-x64, or it does not target windows-x64 either.
        var matchesLinux = Platform.TryMatch(assetName, target, out _);
        var matchesWindows = Platform.TryMatch(assetName, windows, out _);

        Assert.False(matchesLinux && matchesWindows);
    }

    [Fact]
    public void Score_prefers_explicit_platform_ids()
    {
        var target = new Platform(OsKind.Linux, ArchKind.X64);

        var explicitScore = Platform.Score("tool-linux-x64.tar.gz", target);
        var genericScore = Platform.Score("tool-x86_64-unknown-linux-gnu.tar.gz", target);

        Assert.True(explicitScore > genericScore);
    }

    [Theory]
    [InlineData("linux-x64", OsKind.Linux, ArchKind.X64)]
    [InlineData("linux_arm64", OsKind.Linux, ArchKind.Arm64)]
    [InlineData("win-x86", OsKind.Windows, ArchKind.X86)]
    [InlineData("osx-arm64", OsKind.MacOS, ArchKind.Arm64)]
    [InlineData("windows-x64", OsKind.Windows, ArchKind.X64)]
    public void TryParse_reads_platform_ids(string text, OsKind expectedOs, ArchKind expectedArch)
    {
        Assert.True(Platform.TryParse(text, out var platform));
        Assert.Equal(expectedOs, platform.Os);
        Assert.Equal(expectedArch, platform.Arch);
    }

    [Theory]
    [InlineData("linux")]
    [InlineData("x64")]
    [InlineData("")]
    public void TryParse_rejects_incomplete_ids(string text)
    {
        Assert.False(Platform.TryParse(text, out _));
    }

    [Fact]
    public void Id_uses_rid_style_identifiers()
    {
        Assert.Equal("linux-x64", new Platform(OsKind.Linux, ArchKind.X64).Id);
        Assert.Equal("win-arm64", new Platform(OsKind.Windows, ArchKind.Arm64).Id);
        Assert.Equal("osx-x86", new Platform(OsKind.MacOS, ArchKind.X86).Id);
    }

    [Fact]
    public void Current_is_detected()
    {
        Assert.NotEqual(default, Platform.Current);
        Assert.Contains(Platform.Current.Os, new[] { OsKind.Windows, OsKind.Linux, OsKind.MacOS });
    }
}

public class AssetClassificationTests
{
    [Theory]
    [InlineData("tool-1.0.0-linux-x64.tar.gz.sha256", true)]
    [InlineData("tool-1.0.0-linux-x64.tar.gz.asc", true)]
    [InlineData("tool-1.0.0-linux-x64.tar.gz.sig", true)]
    [InlineData("tool-1.0.0-linux-x64.tar.gz", false)]
    [InlineData("tool", false)]
    public void Metadata_sidecars_are_detected(string fileName, bool isMetadata)
    {
        Assert.Equal(isMetadata, AssetClassification.IsMetadataFileName(fileName));
    }
}
