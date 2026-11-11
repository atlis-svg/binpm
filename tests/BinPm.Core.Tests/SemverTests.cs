using BinPm.Core.Semver;

namespace BinPm.Core.Tests;

public class SemVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("1", 1, 0, 0)]
    [InlineData("1.2.3.4", 1, 2, 3)]
    [InlineData("1.2.3-beta.1", 1, 2, 3)]
    public void TryParse_reads_components(string text, int major, int minor, int patch)
    {
        Assert.True(SemVersion.TryParse(text, out var version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-version")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1..3")]
    public void TryParse_rejects_invalid_input(string? text)
    {
        Assert.False(SemVersion.TryParse(text, out _));
    }

    [Fact]
    public void Prerelease_sorts_before_release()
    {
        var beta = SemVersion.Parse("1.0.0-beta");
        var release = SemVersion.Parse("1.0.0");

        Assert.True(beta < release);
        Assert.True(release > beta);
    }

    [Fact]
    public void Prerelease_identifiers_follow_semver_precedence()
    {
        var versions = new[]
        {
            SemVersion.Parse("1.0.0-alpha"),
            SemVersion.Parse("1.0.0-alpha.1"),
            SemVersion.Parse("1.0.0-alpha.beta"),
            SemVersion.Parse("1.0.0-beta"),
            SemVersion.Parse("1.0.0-beta.2"),
            SemVersion.Parse("1.0.0-beta.11"),
            SemVersion.Parse("1.0.0-rc.1"),
            SemVersion.Parse("1.0.0"),
        };

        var sorted = versions.OrderBy(v => v).ToArray();

        Assert.Equal(
            new[] { "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0" },
            sorted.Select(v => v.ToString()).ToArray());
    }

    [Fact]
    public void Build_metadata_is_ignored_when_comparing()
    {
        Assert.Equal(SemVersion.Parse("1.2.3+abc"), SemVersion.Parse("1.2.3+def"));
    }

    [Fact]
    public void ToString_round_trips()
    {
        Assert.Equal("1.2.3", SemVersion.Parse("1.2.3").ToString());
        Assert.Equal("1.2.3-beta.1", SemVersion.Parse("1.2.3-beta.1").ToString());
        Assert.Equal("1.2.3+build", SemVersion.Parse("1.2.3+build").ToString());
    }
}

public class VersionRangeTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("1.2.x", "1.2.9", true)]
    [InlineData("1.2.x", "1.3.0", false)]
    [InlineData("1.x", "1.5.0", true)]
    [InlineData("1.x", "2.0.0", false)]
    [InlineData("1.2", "1.2.7", true)]
    [InlineData("1.2", "1.3.0", false)]
    [InlineData(">=1.2.3", "1.2.3", true)]
    [InlineData(">=1.2.3", "1.2.2", false)]
    [InlineData("<2.0.0", "1.9.9", true)]
    [InlineData("!=1.2.3", "1.2.4", true)]
    [InlineData("^1.2.3", "1.9.0", true)]
    [InlineData("^1.2.3", "2.0.0", false)]
    [InlineData("^0.2.3", "0.2.9", true)]
    [InlineData("^0.2.3", "0.3.0", false)]
    [InlineData("^0.0.3", "0.0.3", true)]
    [InlineData("^0.0.3", "0.0.4", false)]
    [InlineData("~1.2.3", "1.2.9", true)]
    [InlineData("~1.2.3", "1.3.0", false)]
    [InlineData("~1.2", "1.2.9", true)]
    [InlineData("~1.2", "1.3.0", false)]
    [InlineData("latest", "9.9.9", true)]
    [InlineData("*", "9.9.9", true)]
    public void IsMatch_honours_the_spec(string spec, string version, bool expected)
    {
        Assert.Equal(expected, VersionRange.Parse(spec).IsMatch(SemVersion.Parse(version)));
    }

    [Fact]
    public void Multiple_clauses_are_combined_with_and()
    {
        var range = VersionRange.Parse(">=1.0.0 <2.0.0");

        Assert.True(range.IsMatch(SemVersion.Parse("1.5.0")));
        Assert.False(range.IsMatch(SemVersion.Parse("2.0.1")));
        Assert.False(range.IsMatch(SemVersion.Parse("0.9.0")));
    }

    [Fact]
    public void Prereleases_are_only_selected_when_the_range_mentions_them()
    {
        Assert.False(VersionRange.Parse("^1.0.0").IsMatch(SemVersion.Parse("1.5.0-beta.1")));
        Assert.True(VersionRange.Parse(">=1.5.0-beta.1").IsMatch(SemVersion.Parse("1.5.0-beta.1")));
    }

    [Fact]
    public void Select_returns_versions_highest_first()
    {
        var versions = new[] { "1.0.0", "2.1.0", "2.0.0", "0.9.0" }
            .Select(SemVersion.Parse)
            .ToArray();

        var selected = VersionRange.Parse(">=1.0.0").Select(versions).ToArray();

        Assert.Equal(new[] { "2.1.0", "2.0.0", "1.0.0" }, selected.Select(v => v.ToString()).ToArray());
    }

    [Fact]
    public void Caret_range_stays_below_the_next_major_version()
    {
        var versions = new[] { "1.0.0", "2.1.0", "2.0.0", "0.9.0" }
            .Select(SemVersion.Parse)
            .ToArray();

        var selected = VersionRange.Parse("^1.0.0").Select(versions).ToArray();

        Assert.Equal(new[] { "1.0.0" }, selected.Select(v => v.ToString()).ToArray());
    }

    [Fact]
    public void Invalid_range_throws_a_clear_error()
    {
        Assert.Throws<FormatException>(() => VersionRange.Parse(">=not-a-version"));
    }
}
