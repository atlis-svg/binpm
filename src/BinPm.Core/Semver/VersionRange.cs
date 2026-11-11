using System.Globalization;

namespace BinPm.Core.Semver;

/// <summary>
/// A version selector supporting:
/// <list type="bullet">
/// <item><c>latest</c> / <c>*</c> / empty — any version</item>
/// <item>exact: <c>1.2.3</c> (missing components default to 0)</item>
/// <item>wildcards: <c>1.x</c>, <c>1.2.x</c>, <c>1</c>, <c>1.2</c></item>
/// <item>comparisons: <c>&gt;=1.2.3</c>, <c>&gt;1.0</c>, <c>&lt;2.0</c>, <c>!=1.2.3</c></item>
/// <item>caret: <c>^1.2.3</c></item>
/// <item>tilde: <c>~1.2.3</c></item>
/// </list>
/// Multiple space/comma separated clauses are combined with AND.
/// </summary>
public sealed class VersionRange
{
    private readonly List<RangeClause> _clauses = new();

    /// <summary>A range that matches every version.</summary>
    public static VersionRange Any { get; } = new VersionRange();

    private VersionRange()
    {
    }

    public static VersionRange Parse(string? spec)
    {
        var range = new VersionRange();
        if (string.IsNullOrWhiteSpace(spec))
            return range;

        var text = spec.Trim();
        if (text.Length == 0 || text is "*" or "x" or "X" or "latest" or "LATEST")
            return range;

        foreach (var rawClause in SplitClauses(text))
        {
            var clause = ParseClause(rawClause);
            if (clause != null)
                range._clauses.Add(clause);
        }

        return range;
    }

    private static IEnumerable<string> SplitClauses(string text)
    {
        var current = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c == ',')
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static RangeClause? ParseClause(string clause)
    {
        var text = clause.Trim();
        if (text.Length == 0)
            return null;

        if (text.StartsWith("^", StringComparison.Ordinal))
        {
            var (caretLoose, _) = ParseLoose(text.Substring(1));
            return new CaretClause(caretLoose.Version);
        }

        if (text.StartsWith("~", StringComparison.Ordinal))
        {
            var (tildeLoose, tildeParts) = ParseLoose(text.Substring(1));
            return new TildeClause(tildeLoose.Version, tildeParts);
        }

        if (text.StartsWith(">=", StringComparison.Ordinal))
            return new CompareClause(VersionOp.GreaterThanOrEqual, ParseStrict(text.Substring(2)));
        if (text.StartsWith("<=", StringComparison.Ordinal))
            return new CompareClause(VersionOp.LessThanOrEqual, ParseStrict(text.Substring(2)));
        if (text.StartsWith("==", StringComparison.Ordinal))
            return new CompareClause(VersionOp.Equal, ParseStrict(text.Substring(2)));
        if (text.StartsWith("!=", StringComparison.Ordinal))
            return new CompareClause(VersionOp.NotEqual, ParseStrict(text.Substring(2)));
        if (text.StartsWith('>'))
            return new CompareClause(VersionOp.GreaterThan, ParseStrict(text.Substring(1)));
        if (text.StartsWith('<'))
            return new CompareClause(VersionOp.LessThan, ParseStrict(text.Substring(1)));
        if (text.StartsWith('='))
            return new CompareClause(VersionOp.Equal, ParseStrict(text.Substring(1)));

        var (loose, partsSpecified) = ParseLoose(text);
        if (loose.IsWildcard || partsSpecified < 3)
            return new WildcardClause(loose.Major, loose.Minor, loose.Patch);

        return new ExactClause(loose.Version);
    }

    private static SemVersion ParseStrict(string text)
    {
        if (!SemVersion.TryParse(text, out var version))
            throw new FormatException($"'{text}' is not a valid version.");
        return version;
    }

    private static (LooseVersion Version, int PartsSpecified) ParseLoose(string text)
    {
        var raw = text.Trim();
        if (raw.Length > 0 && (raw[0] == 'v' || raw[0] == 'V'))
            raw = raw.Substring(1);

        var plus = raw.IndexOf('+');
        if (plus >= 0) raw = raw.Substring(0, plus);
        var dash = raw.IndexOf('-');
        if (dash >= 0) raw = raw.Substring(0, dash);

        var parts = raw.Split('.');
        if (parts.Length == 0 || parts.Length > 4)
            throw new FormatException($"'{text}' is not a valid version range component.");

        var numbers = new int[4];
        var wildcard = new bool[4];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part is "x" or "X" or "*")
            {
                wildcard[i] = true;
                numbers[i] = 0;
            }
            else if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]) || numbers[i] < 0)
            {
                throw new FormatException($"'{text}' is not a valid version range component.");
            }
        }

        return (new LooseVersion(new SemVersion(numbers[0], numbers[1], numbers[2], numbers[3]), wildcard, parts.Length), parts.Length);
    }

    public bool IsMatch(SemVersion version)
    {
        if (_clauses.Count == 0)
            return true;

        if (version.IsPrerelease)
        {
            // Prereleases are only selectable when a clause explicitly targets the same core version.
            var prereleaseAllowed = false;
            foreach (var clause in _clauses)
            {
                if (clause.TargetVersion is { IsPrerelease: true } target &&
                    target.Major == version.Major &&
                    target.Minor == version.Minor &&
                    target.Patch == version.Patch &&
                    target.Revision == version.Revision)
                {
                    prereleaseAllowed = true;
                    break;
                }
            }

            if (!prereleaseAllowed)
                return false;
        }

        foreach (var clause in _clauses)
        {
            if (!clause.Matches(version))
                return false;
        }

        return true;
    }

    /// <returns>Matching versions ordered from highest to lowest.</returns>
    public IEnumerable<SemVersion> Select(IEnumerable<SemVersion> versions) =>
        versions.Where(IsMatch).OrderByDescending(v => v);

    public override string ToString() =>
        _clauses.Count == 0 ? "latest" : string.Join(" ", _clauses);

    private readonly struct LooseVersion
    {
        private readonly SemVersion _version;
        private readonly bool[] _wildcard;
        private readonly int _partsSpecified;

        public bool IsWildcard { get; }
        public int? Major => _wildcard[0] || _partsSpecified < 1 ? null : _version.Major;
        public int? Minor => _wildcard[1] || _partsSpecified < 2 ? null : _version.Minor;
        public int? Patch => _wildcard[2] || _partsSpecified < 3 ? null : _version.Patch;
        public SemVersion Version => _version;

        public LooseVersion(SemVersion version, bool[] wildcard, int partsSpecified)
        {
            _version = version;
            _wildcard = wildcard;
            _partsSpecified = partsSpecified;
            IsWildcard = wildcard.Any(w => w);
        }
    }

    private enum VersionOp
    {
        Equal,
        NotEqual,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
    }

    private abstract class RangeClause
    {
        /// <summary>The concrete version this clause is anchored to, if any. Used for prerelease filtering.</summary>
        public virtual SemVersion? TargetVersion => null;

        public abstract bool Matches(SemVersion version);

        public abstract override string ToString();
    }

    private sealed class AnyClause : RangeClause
    {
        public override bool Matches(SemVersion version) => true;
        public override string ToString() => "latest";
    }

    private sealed class ExactClause : RangeClause
    {
        private readonly SemVersion _target;

        public ExactClause(SemVersion target) => _target = target;

        public override SemVersion? TargetVersion => _target;

        public override bool Matches(SemVersion version) => version == _target;

        public override string ToString() => _target.ToString();
    }

    private sealed class WildcardClause : RangeClause
    {
        private readonly int? _major;
        private readonly int? _minor;
        private readonly int? _patch;

        public WildcardClause(int? major, int? minor, int? patch)
        {
            _major = major;
            _minor = minor;
            _patch = patch;
        }

        public override bool Matches(SemVersion version)
        {
            if (_major.HasValue && version.Major != _major.Value) return false;
            if (_minor.HasValue && version.Minor != _minor.Value) return false;
            if (_patch.HasValue && version.Patch != _patch.Value) return false;
            return true;
        }

        public override string ToString()
        {
            if (!_major.HasValue) return "*";
            if (!_minor.HasValue) return $"{_major}.x";
            if (!_patch.HasValue) return $"{_major}.{_minor}.x";
            return $"{_major}.{_minor}.{_patch}";
        }
    }

    private sealed class CompareClause : RangeClause
    {
        private readonly VersionOp _op;
        private readonly SemVersion _target;

        public CompareClause(VersionOp op, SemVersion target)
        {
            _op = op;
            _target = target;
        }

        public override SemVersion? TargetVersion => _target;

        public override bool Matches(SemVersion version)
        {
            var c = version.CompareTo(_target);
            return _op switch
            {
                VersionOp.Equal => c == 0,
                VersionOp.NotEqual => c != 0,
                VersionOp.GreaterThan => c > 0,
                VersionOp.GreaterThanOrEqual => c >= 0,
                VersionOp.LessThan => c < 0,
                VersionOp.LessThanOrEqual => c <= 0,
                _ => false,
            };
        }

        public override string ToString() => _op switch
        {
            VersionOp.Equal => $"={_target}",
            VersionOp.NotEqual => $"!={_target}",
            VersionOp.GreaterThan => $">{_target}",
            VersionOp.GreaterThanOrEqual => $">={_target}",
            VersionOp.LessThan => $"<{_target}",
            VersionOp.LessThanOrEqual => $"<={_target}",
            _ => _target.ToString(),
        };
    }

    private sealed class CaretClause : RangeClause
    {
        private readonly SemVersion _target;
        private readonly SemVersion _upper;

        public CaretClause(SemVersion target)
        {
            _target = target;
            _upper = ComputeUpperBound(target);
        }

        public override SemVersion? TargetVersion => _target;

        private static SemVersion ComputeUpperBound(SemVersion target)
        {
            if (target.Major > 0)
                return new SemVersion(target.Major + 1, 0, 0);
            if (target.Minor > 0)
                return new SemVersion(0, target.Minor + 1, 0);
            return new SemVersion(0, 0, target.Patch + 1);
        }

        public override bool Matches(SemVersion version) =>
            version >= _target && version < _upper;

        public override string ToString() => $"^{_target}";
    }

    private sealed class TildeClause : RangeClause
    {
        private readonly SemVersion _target;
        private readonly SemVersion _upper;

        public TildeClause(SemVersion target, int partsSpecified)
        {
            _target = target;
            _upper = partsSpecified >= 2
                ? new SemVersion(target.Major, target.Minor + 1, 0)
                : new SemVersion(target.Major + 1, 0, 0);
        }

        public override SemVersion? TargetVersion => _target;

        public override bool Matches(SemVersion version) =>
            version >= _target && version < _upper;

        public override string ToString() => $"~{_target}";
    }
}
