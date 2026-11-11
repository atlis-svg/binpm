using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace BinPm.Core.Models;

public enum OsKind
{
    Windows,
    Linux,
    MacOS,
}

public enum ArchKind
{
    X86,
    X64,
    Arm,
    Arm64,
}

/// <summary>An operating system / CPU architecture pair, e.g. <c>linux-x64</c>.</summary>
public readonly record struct Platform(OsKind Os, ArchKind Arch)
{
    public static Platform Current { get; } = DetectCurrent();

    public string Id => Os switch
    {
        OsKind.Windows => "win",
        OsKind.Linux => "linux",
        OsKind.MacOS => "osx",
        _ => "unknown",
    } + "-" + Arch switch
    {
        ArchKind.X86 => "x86",
        ArchKind.X64 => "x64",
        ArchKind.Arm => "arm",
        ArchKind.Arm64 => "arm64",
        _ => "unknown",
    };

    public bool IsUnix => Os != OsKind.Windows;

    public override string ToString() => Id;

    private static Platform DetectCurrent()
    {
        OsKind os;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            os = OsKind.Windows;
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            os = OsKind.Linux;
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            os = OsKind.MacOS;
        else
            os = OsKind.Linux;

        ArchKind arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => ArchKind.X64,
            Architecture.X86 => ArchKind.X86,
            Architecture.Arm64 => ArchKind.Arm64,
            Architecture.Arm => ArchKind.Arm,
            _ => ArchKind.X64,
        };

        return new Platform(os, arch);
    }

    /// <summary>Parses a platform id such as <c>linux-x64</c>, <c>osx-arm64</c> or <c>win-x86</c>.</summary>
    public static bool TryParse(string? text, out Platform platform)
    {
        platform = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var tokens = Tokenize(text);
        var os = FindOs(tokens);
        var arch = FindArch(tokens);

        if (os is OsKind osKind && arch is ArchKind archKind)
        {
            platform = new Platform(osKind, archKind);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to match free-form text (usually an asset file name) against a target platform.
    /// Returns a score; higher is better. A score &lt;= 0 means "not a match".
    /// </summary>
    public static bool TryMatch(string? text, Platform target, out int score)
    {
        score = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var tokens = Tokenize(text);

        var os = FindOs(tokens);
        var arch = FindArch(tokens);

        if (os is null || arch is null)
            return false;

        var matchedOs = os.Value == target.Os;
        var matchedArch = arch.Value == target.Arch;

        if (!matchedOs || !matchedArch)
        {
            score = -1;
            return false;
        }

        score = 20;

        // Prefer names that spell out the platform id exactly.
        var normalized = text.ToLowerInvariant().Replace('_', '-');
        if (normalized.Contains(target.Id))
            score += 50;

        // Penalize names that mention a different OS or architecture.
        foreach (var token in tokens)
        {
            if (OsTokens.TryGetValue(token, out var otherOs) && otherOs != target.Os)
                score -= 5;
            if (ArchTokens.TryGetValue(token, out var otherArch) && otherArch != target.Arch)
                score -= 5;
        }

        return true;
    }

    /// <summary>Convenience overload returning just the score (-1 when not a match).</summary>
    public static int Score(string? text, Platform target) =>
        TryMatch(text, target, out var score) ? score : -1;

    /// <summary>
    /// True when the text mentions an operating system or architecture at all.
    /// Used to tell "a generic asset" apart from "an asset for a different platform".
    /// </summary>
    public static bool HasPlatformMarkers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var tokens = Tokenize(text);

        foreach (var token in tokens)
        {
            if (token is "win64" or "win32")
                return true;
            if (OsTokens.ContainsKey(token) || ArchTokens.ContainsKey(token))
                return true;
        }

        return FindOs(tokens) is not null || FindArch(tokens) is not null;
    }

    private static OsKind? FindOs(List<string> tokens)
    {
        OsKind? result = null;

        foreach (var token in tokens)
        {
            if (token is "win64" or "win32")
            {
                result = OsKind.Windows;
                continue;
            }

            if (OsTokens.TryGetValue(token, out var os))
            {
                if (result is not null && result != os)
                    return result; // ambiguous; keep the first
                result = os;
            }
        }

        if (result is not null)
            return result;

        // Substring fallbacks for names like "linux64" or "unknown_linux_musl".
        foreach (var token in tokens)
        {
            if (token.Contains("windows", StringComparison.Ordinal)) return OsKind.Windows;
            if (token.Contains("linux", StringComparison.Ordinal)) return OsKind.Linux;
            if (token.Contains("darwin", StringComparison.Ordinal) ||
                token.Contains("macos", StringComparison.Ordinal) ||
                token.Contains("osx", StringComparison.Ordinal))
            {
                return OsKind.MacOS;
            }
        }

        return null;
    }

    private static ArchKind? FindArch(List<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (token is "arm64" or "aarch64" or "arm64e" or "armv8" or "armv8a")
                return ArchKind.Arm64;
        }

        foreach (var token in tokens)
        {
            if (token is "x64" or "x86_64" or "x86-64" or "amd64" or "x86_64h")
                return ArchKind.X64;
            if (token.EndsWith("64", StringComparison.Ordinal) && token.Length > 2)
                return ArchKind.X64;
        }

        foreach (var token in tokens)
        {
            if (token is "x86" or "i386" or "i486" or "i586" or "i686" or "ia32")
                return ArchKind.X86;
            if (token.EndsWith("32", StringComparison.Ordinal) && token.Length > 2)
                return ArchKind.X86;
        }

        foreach (var token in tokens)
        {
            if (token is "arm" or "armv7" or "armv7l" or "armv6" or "armhf" or "arm32" or "armel")
                return ArchKind.Arm;
        }

        return null;
    }

    private static List<string> Tokenize(string text)
    {
        var parts = Regex.Split(text.ToLowerInvariant(), "[^a-z0-9]+")
            .Where(p => p.Length > 0)
            .ToList();

        var tokens = new HashSet<string>(parts);

        // Re-join adjacent parts with '_' to recognize forms like x86_64 and unknown_linux_musl.
        for (var i = 0; i < parts.Count; i++)
        {
            if (i + 1 < parts.Count)
                tokens.Add(parts[i] + "_" + parts[i + 1]);
            if (i + 2 < parts.Count)
                tokens.Add(parts[i] + "_" + parts[i + 1] + "_" + parts[i + 2]);
        }

        return tokens.ToList();
    }

    private static readonly Dictionary<string, OsKind> OsTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["windows"] = OsKind.Windows,
        ["win"] = OsKind.Windows,
        ["win32"] = OsKind.Windows,
        ["win64"] = OsKind.Windows,
        ["windows32"] = OsKind.Windows,
        ["windows64"] = OsKind.Windows,

        ["linux"] = OsKind.Linux,

        ["osx"] = OsKind.MacOS,
        ["macos"] = OsKind.MacOS,
        ["macosx"] = OsKind.MacOS,
        ["mac"] = OsKind.MacOS,
        ["darwin"] = OsKind.MacOS,
        ["apple"] = OsKind.MacOS,
        ["osx64"] = OsKind.MacOS,
        ["osx_arm64"] = OsKind.MacOS,
    };

    private static readonly Dictionary<string, ArchKind> ArchTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["x64"] = ArchKind.X64,
        ["x86_64"] = ArchKind.X64,
        ["x86-64"] = ArchKind.X64,
        ["amd64"] = ArchKind.X64,

        ["x86"] = ArchKind.X86,
        ["i386"] = ArchKind.X86,
        ["i486"] = ArchKind.X86,
        ["i586"] = ArchKind.X86,
        ["i686"] = ArchKind.X86,
        ["ia32"] = ArchKind.X86,
        ["386"] = ArchKind.X86,

        ["arm64"] = ArchKind.Arm64,
        ["aarch64"] = ArchKind.Arm64,
        ["arm64e"] = ArchKind.Arm64,

        ["arm"] = ArchKind.Arm,
        ["armv7"] = ArchKind.Arm,
        ["armv7l"] = ArchKind.Arm,
        ["armv6"] = ArchKind.Arm,
        ["armhf"] = ArchKind.Arm,
        ["arm32"] = ArchKind.Arm,
    };
}
