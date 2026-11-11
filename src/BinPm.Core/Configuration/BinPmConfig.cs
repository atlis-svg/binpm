using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BinPm.Core.Configuration;

/// <summary>User configuration loaded from <c>config.json</c>.</summary>
public sealed class BinPmConfig
{
    /// <summary>Root directory overriding the platform default (BINPM_ROOT).</summary>
    [JsonPropertyName("root")]
    public string? Root { get; set; }

    /// <summary>Shim directory overriding the platform default.</summary>
    [JsonPropertyName("binDir")]
    public string? BinDir { get; set; }

    /// <summary>Package store directory overriding <c>{root}/packages</c>.</summary>
    [JsonPropertyName("installDir")]
    public string? InstallDir { get; set; }

    /// <summary>Name of the registry used when the user does not specify one.</summary>
    [JsonPropertyName("defaultRegistry")]
    public string? DefaultRegistry { get; set; }

    /// <summary>GitHub token used for API requests (raises the rate limit).</summary>
    [JsonPropertyName("githubToken")]
    public string? GitHubToken { get; set; }

    [JsonPropertyName("registries")]
    public List<RegistryConfig> Registries { get; set; } = new();

    public RegistryConfig? FindRegistry(string name) =>
        Registries.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Default location of the config file for the current platform.</summary>
    public static string DefaultConfigPath
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "binpm", "config.json");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "binpm", "config.json");

            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(configHome))
                configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            return Path.Combine(configHome, "binpm", "config.json");
        }
    }

    /// <summary>Loads the config from <paramref name="path"/> (or the default location). Missing files yield defaults.</summary>
    public static BinPmConfig Load(string? path = null)
    {
        var configPath = Expand(path ?? DefaultConfigPath);

        if (!File.Exists(configPath))
            return new BinPmConfig();

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonDefaults.Deserialize<BinPmConfig>(json) ?? new BinPmConfig();
            config.Root = Expand(config.Root);
            config.BinDir = Expand(config.BinDir);
            config.InstallDir = Expand(config.InstallDir);
            return config;
        }
        catch (JsonException ex)
        {
            throw new BinPmException($"Could not parse config file '{configPath}': {ex.Message}", ex);
        }
    }

    public void Save(string? path = null)
    {
        var configPath = Expand(path ?? DefaultConfigPath);
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(configPath, JsonDefaults.Serialize(this));
    }

    public static string Expand(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return path ?? "";

        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (path.StartsWith("~/") || path.StartsWith("~\\"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path.Substring(2));
        }

        return path;
    }
}
