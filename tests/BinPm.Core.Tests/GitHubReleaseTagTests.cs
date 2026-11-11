using BinPm.Core.Registry;

namespace BinPm.Core.Tests;

public class GitHubReleaseTagTests
{
    [Theory]
    [InlineData("1.7.1", "1.7.1")]
    [InlineData("v1.7.1", "1.7.1")]
    [InlineData("jq-1.7.1", "1.7.1")]
    [InlineData("release-2.0.0", "2.0.0")]
    [InlineData("v0.9.0-rc1", "0.9.0-rc1")]
    [InlineData("yq_v4.44.1", "4.44.1")]
    public void Release_tags_with_prefixes_are_parsed(string tag, string expected)
    {
        Assert.True(GitHubRegistryClient.TryParseReleaseTag(tag, out var version));
        Assert.Equal(expected, version.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("nightly")]
    [InlineData("no-version-here")]
    public void Non_version_tags_are_rejected(string tag)
    {
        Assert.False(GitHubRegistryClient.TryParseReleaseTag(tag, out _));
    }
}
