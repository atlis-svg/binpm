using System.Globalization;

namespace BinPm.Core.Semver;

/// <summary>
/// A semantic version (MAJOR.MINOR.PATCH[-prerelease][+build]).
/// Supports an optional fourth numeric component (revision) for tools that use it.
/// </summary>
public readonly struct SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public int Revision { get; }

    /// <summary>Prerelease identifiers without the leading '-', e.g. "beta.1". Empty when not a prerelease.</summary>
    public string Prerelease { get; }

    /// <summary>Build metadata without the leading '+'. Never participates in comparison.</summary>
    public string Build { get; }

    public bool IsPrerelease => Prerelease.Length > 0;

    public SemVersion(int major, int minor, int patch, int revision = 0, string prerelease = "", string build = "")
    {
        if (major < 0 || minor < 0 || patch < 0 || revision < 0)
            throw new ArgumentOutOfRangeException(nameof(major), "Version components cannot be negative.");

        Major = major;
        Minor = minor;
        Patch = patch;
        Revision = revision;
        Prerelease = prerelease ?? "";
        Build = build ?? "";
    }

    /// <summary>
    /// Parses a version string. Tolerates a leading 'v', missing minor/patch components,
    /// a prerelease suffix and build metadata.
    /// </summary>
    public static bool TryParse(string? text, out SemVersion value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            s = s.Substring(1);

        var build = "";
        var plus = s.IndexOf('+');
        if (plus >= 0)
        {
            build = s.Substring(plus + 1);
            s = s.Substring(0, plus);
        }

        var prerelease = "";
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = s.Substring(dash + 1);
            s = s.Substring(0, dash);
        }

        var parts = s.Split('.');
        if (parts.Length == 0 || parts.Length > 4)
            return false;

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        if (!IsValidIdentifierList(prerelease))
            return false;

        value = new SemVersion(numbers[0], numbers[1], numbers[2], numbers[3], prerelease, build);
        return true;
    }

    public static SemVersion Parse(string text)
    {
        if (!TryParse(text, out var version))
            throw new FormatException($"'{text}' is not a valid version.");
        return version;
    }

    private static bool IsValidIdentifierList(string identifiers)
    {
        if (identifiers.Length == 0)
            return true;

        foreach (var identifier in identifiers.Split('.'))
        {
            if (identifier.Length == 0)
                return false;
            foreach (var c in identifier)
            {
                if (!char.IsLetterOrDigit(c) && c != '-')
                    return false;
            }
        }

        return true;
    }

    public int CompareTo(SemVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        result = Revision.CompareTo(other.Revision);
        if (result != 0) return result;

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0) return 0;
        if (left.Length == 0) return 1;   // release > prerelease
        if (right.Length == 0) return -1;

        var leftIds = left.Split('.');
        var rightIds = right.Split('.');

        for (var i = 0; i < Math.Max(leftIds.Length, rightIds.Length); i++)
        {
            if (i >= leftIds.Length) return -1;  // fewer fields => lower precedence
            if (i >= rightIds.Length) return 1;

            var a = leftIds[i];
            var b = rightIds[i];

            var aNumeric = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var aNum);
            var bNumeric = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bNum);

            if (aNumeric && bNumeric)
            {
                var c = aNum.CompareTo(bNum);
                if (c != 0) return c;
            }
            else if (aNumeric)
            {
                return -1; // numeric identifiers have lower precedence than alphanumeric
            }
            else if (bNumeric)
            {
                return 1;
            }
            else
            {
                var c = string.CompareOrdinal(a, b);
                if (c != 0) return c;
            }
        }

        return 0;
    }

    public bool Equals(SemVersion other) =>
        Major == other.Major &&
        Minor == other.Minor &&
        Patch == other.Patch &&
        Revision == other.Revision &&
        string.Equals(Prerelease, other.Prerelease, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SemVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Revision, Prerelease);

    public static bool operator ==(SemVersion left, SemVersion right) => left.Equals(right);
    public static bool operator !=(SemVersion left, SemVersion right) => !left.Equals(right);
    public static bool operator <(SemVersion left, SemVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(SemVersion left, SemVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(SemVersion left, SemVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(SemVersion left, SemVersion right) => left.CompareTo(right) >= 0;

    public override string ToString()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(Major).Append('.').Append(Minor).Append('.').Append(Patch);
        if (Revision != 0)
            builder.Append('.').Append(Revision);
        if (Prerelease.Length > 0)
            builder.Append('-').Append(Prerelease);
        if (Build.Length > 0)
            builder.Append('+').Append(Build);
        return builder.ToString();
    }
}
