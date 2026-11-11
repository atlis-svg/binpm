using BinPm.Core.Install;

namespace BinPm.Core.Tests;

public class AssetBinaryNameTests
{
    [Theory]
    [InlineData("jq-linux-amd64", "jq")]
    [InlineData("yq_linux_amd64", "yq")]
    [InlineData("sops-v3.13.3.linux.amd64", "sops")]
    [InlineData("gh_2.62.0_windows_amd64.exe", "gh")]
    [InlineData("neofetch", "neofetch")]
    [InlineData("tool", "tool")]
    [InlineData("fd-v10.2.0-x86_64-unknown-linux-musl.tar.gz", "fd")]
    [InlineData("tool-linux64", "tool")]
    [InlineData("base64-tool", "base64-tool")]
    [InlineData("my-cool-tool-linux-amd64", "my-cool-tool")]
    public void Trailing_platform_and_version_decoration_is_dropped(string assetName, string expected)
    {
        Assert.Equal(expected, BinaryInstaller.GetAssetBinaryName(assetName));
    }
}
