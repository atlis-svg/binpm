using System.Runtime.InteropServices;
using BinPm.Core.Configuration;

namespace BinPm.Core.Install;

/// <summary>Filesystem locations used by BinPm.</summary>
public sealed class Paths
{
    public string Root { get; }
    public string PackagesDir { get; }
    public string BinDir { get; }
    public string CacheDir { get; }
    public string InstalledDbPath { get; }

    public Paths(string root, string packagesDir, string binDir, string cacheDir, string installedDbPath)
    {
        Root = root;
        PackagesDir = packagesDir;
        BinDir = binDir;
        CacheDir = cacheDir;
        InstalledDbPath = installedDbPath;
    }

    /// <summary>Default root directory for the current platform.</summary>
    public static string DefaultRoot
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "binpm");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "binpm");
            }

            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(dataHome))
                dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

            return Path.Combine(dataHome, "binpm");
        }
    }

    /// <summary>Default shim directory for the current platform.</summary>
    public static string DefaultBinDir
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(DefaultRoot, "bin");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");
        }
    }

    public static Paths Create(BinPmConfig? config, string? rootOverride = null, string? binDirOverride = null)
    {
        config ??= new BinPmConfig();

        var rootValue = rootOverride ??
                        Environment.GetEnvironmentVariable("BINPM_ROOT") ??
                        config.Root;
        var root = BinPmConfig.Expand(string.IsNullOrEmpty(rootValue) ? DefaultRoot : rootValue);

        var binDirValue = binDirOverride ??
                          Environment.GetEnvironmentVariable("BINPM_BIN_DIR") ??
                          config.BinDir;
        var binDir = BinPmConfig.Expand(string.IsNullOrEmpty(binDirValue) ? DefaultBinDir : binDirValue);

        var packagesDir = string.IsNullOrEmpty(config.InstallDir)
            ? Path.Combine(root, "packages")
            : BinPmConfig.Expand(config.InstallDir);

        var cacheDir = Path.Combine(root, "cache");

        return new Paths(root, packagesDir, binDir, cacheDir, Path.Combine(root, "installed.json"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(PackagesDir);
        Directory.CreateDirectory(BinDir);
        Directory.CreateDirectory(CacheDir);
    }
}
