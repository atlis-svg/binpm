using System.Runtime.InteropServices;
using BinPm.Core.Configuration;

namespace BinPm.Core.Install;

/// <summary>Creates and removes shims (symlinks, scripts or .cmd wrappers) in the shim directory.</summary>
public sealed class ShimManager
{
    private readonly string _binDir;

    public ShimManager(string binDir)
    {
        _binDir = binDir ?? throw new ArgumentNullException(nameof(binDir));
    }

    public string BinDirectory => _binDir;

    public void EnsureBinDirectory() => Directory.CreateDirectory(_binDir);

    public string GetShimPath(string binName)
    {
        var name = SanitizeName(binName);
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(_binDir, name + ".cmd")
            : Path.Combine(_binDir, name);
    }

    /// <summary>Creates shims for the given binaries. Existing shims pointing at the same target are left untouched.</summary>
    public IReadOnlyList<ShimInfo> CreateShims(IReadOnlyList<ShimRequest> requests, bool overwrite = false)
    {
        EnsureBinDirectory();
        var results = new List<ShimInfo>();

        foreach (var request in requests)
        {
            var shimPath = GetShimPath(request.Name);
            var target = Path.GetFullPath(request.TargetPath);

            if (ShimExists(shimPath))
            {
                var existingTarget = GetShimTarget(shimPath);
                if (PathsEqual(existingTarget, target))
                {
                    results.Add(new ShimInfo(request.Name, shimPath, GetShimKind(shimPath), existedAlready: true));
                    continue;
                }

                if (!overwrite)
                {
                    throw new BinPmException(
                        $"A shim named '{request.Name}' already exists at '{shimPath}'" +
                        (existingTarget is null ? "." : $" and points to '{existingTarget}'.") +
                        " Use --overwrite-shims to replace it.");
                }

                DeleteShim(shimPath);
            }

            var kind = WriteShim(shimPath, target);
            results.Add(new ShimInfo(request.Name, shimPath, kind, existedAlready: false));
        }

        return results;
    }

    /// <summary>Removes a shim when it exists and (if given) points at the expected target.</summary>
    public bool RemoveShim(string binName, string? expectedTarget = null)
    {
        var shimPath = GetShimPath(binName);
        if (!ShimExists(shimPath))
            return false;

        if (expectedTarget is not null && !PathsEqual(GetShimTarget(shimPath), Path.GetFullPath(expectedTarget)))
            return false;

        DeleteShim(shimPath);
        return true;
    }

    public IReadOnlyList<bool> RemoveShims(IEnumerable<(string Name, string? ExpectedTarget)> shims)
    {
        var results = new List<bool>();
        foreach (var (name, expectedTarget) in shims)
        {
            results.Add(RemoveShim(name, expectedTarget));
        }

        return results;
    }

    public bool ShimExists(string shimPath)
    {
        if (File.Exists(shimPath) || Directory.Exists(shimPath))
            return true;

        try
        {
            return File.ResolveLinkTarget(shimPath, returnFinalTarget: false) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reads the target recorded in a shim, or null when it cannot be determined.</summary>
    public string? GetShimTarget(string shimPath)
    {
        if (!ShimExists(shimPath))
            return null;

        var linkTarget = File.ResolveLinkTarget(shimPath, returnFinalTarget: false);
        if (linkTarget is not null)
            return linkTarget.ToString();

        try
        {
            foreach (var line in File.ReadLines(shimPath))
            {
                var trimmed = line.Trim();

                const string scriptMarker = "# binpm: ";
                if (trimmed.StartsWith(scriptMarker, StringComparison.Ordinal))
                    return trimmed.Substring(scriptMarker.Length).Trim().Trim('"');

                const string cmdMarker = "rem binpm: ";
                if (trimmed.StartsWith(cmdMarker, StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(cmdMarker.Length).Trim().Trim('"');
            }
        }
        catch { /* unreadable shim */ }

        return null;
    }

    public ShimKind GetShimKind(string shimPath)
    {
        if (File.ResolveLinkTarget(shimPath, returnFinalTarget: false) is not null)
            return ShimKind.SymbolicLink;

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ShimKind.WindowsCommand
            : ShimKind.Script;
    }

    /// <summary>True when the shim directory is already present in the PATH environment variable.</summary>
    public bool IsBinDirOnPath()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var binDirFull = Path.GetFullPath(_binDir);
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var entry in pathVariable.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var expanded = BinPmConfig.Expand(entry.Trim());
                if (Path.GetFullPath(expanded).Equals(binDirFull, comparison))
                    return true;
            }
            catch { /* ignore malformed PATH entries */ }
        }

        return false;
    }

    /// <summary>Shell commands the user can run to add the shim directory to PATH.</summary>
    public string GetPathInstructions(string shell)
    {
        var binDir = _binDir;
        return shell.ToLowerInvariant() switch
        {
            "fish" => $"set -gx PATH \"{binDir}\" $PATH",
            "powershell" or "pwsh" => $"$env:PATH = \"{binDir};\" + $env:PATH",
            "cmd" => $"setx PATH \"%PATH%;{binDir}\"",
            _ => $"export PATH=\"{binDir}:$PATH\"",
        };
    }

    private ShimKind WriteShim(string shimPath, string target)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var content =
                "@echo off" + Environment.NewLine +
                $"rem binpm: {target}" + Environment.NewLine +
                $"\"{target}\" %*" + Environment.NewLine;
            File.WriteAllText(shimPath, content);
            return ShimKind.WindowsCommand;
        }

        try
        {
            File.CreateSymbolicLink(shimPath, target);
            return ShimKind.SymbolicLink;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            var content =
                "#!/bin/sh" + Environment.NewLine +
                $"# binpm: {target}" + Environment.NewLine +
                $"exec \"{target}\" \"$@\"" + Environment.NewLine;
            File.WriteAllText(shimPath, content);

            try
            {
                File.SetUnixFileMode(shimPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch { /* best-effort */ }

            return ShimKind.Script;
        }
    }

    private void DeleteShim(string shimPath)
    {
        if (File.ResolveLinkTarget(shimPath, returnFinalTarget: false) is not null || File.Exists(shimPath))
            File.Delete(shimPath);
        else if (Directory.Exists(shimPath))
            Directory.Delete(shimPath, recursive: false);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (left is null || right is null)
            return false;

        try
        {
            var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return Path.GetFullPath(left).Equals(Path.GetFullPath(right), comparison);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }

    private static string SanitizeName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            throw new BinPmException("A binary name cannot be empty.");

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            if (trimmed.Contains(c))
                throw new BinPmException($"Binary name '{name}' contains invalid characters.");
        }

        if (trimmed is "." or "..")
            throw new BinPmException($"Binary name '{name}' is not valid.");

        return trimmed;
    }
}
