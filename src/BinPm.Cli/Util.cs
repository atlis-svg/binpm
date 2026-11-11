namespace BinPm.Cli;

public static class Util
{
    /// <summary>Splits <c>package@version</c> into its parts.</summary>
    public static (string Package, string? Version) SplitVersion(string reference)
    {
        var at = reference.IndexOf('@');
        if (at > 0)
            return (reference.Substring(0, at), reference.Substring(at + 1));

        return (reference, null);
    }

    /// <summary>Best-effort detection of the user's shell.</summary>
    public static string DetectShell()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell))
        {
            var name = Path.GetFileName(shell).ToLowerInvariant();
            if (name.Contains("zsh")) return "zsh";
            if (name.Contains("fish")) return "fish";
            if (name.Contains("bash")) return "bash";
            if (name.Contains("sh")) return "sh";
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PSModulePath")))
            return "powershell";

        return "bash";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    public static long DirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try { return new FileInfo(file).Length; }
                    catch { return 0; }
                });
        }
        catch
        {
            return 0;
        }
    }
}
