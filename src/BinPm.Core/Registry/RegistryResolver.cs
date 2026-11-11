using BinPm.Core.Configuration;

namespace BinPm.Core.Registry;

/// <summary>Turns a user-supplied package reference into a registry client and a registry-local package name.</summary>
public sealed class RegistryResolver
{
    private readonly BinPmConfig _config;
    private readonly HttpClient _http;

    public RegistryResolver(BinPmConfig config, HttpClient http)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <summary>
    /// Resolves a reference. Accepted forms:
    /// <list type="bullet">
    /// <item><c>name</c> — looked up in the default registry</item>
    /// <item><c>owner/repo</c> — GitHub</item>
    /// <item><c>registry:package</c> — a configured registry, or <c>github:owner/repo</c></item>
    /// </list>
    /// </summary>
    public ResolvedReference Resolve(string reference, string? registryName = null)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new BinPmException("No package name was given.");

        var package = reference.Trim();
        string? explicitRegistry = string.IsNullOrWhiteSpace(registryName) ? null : registryName.Trim();

        // Explicit "registry:package" form.
        var separator = package.IndexOf(':');
        if (separator > 0)
        {
            var prefix = package.Substring(0, separator);
            var known = _config.FindRegistry(prefix) is not null ||
                        prefix.Equals("github", StringComparison.OrdinalIgnoreCase) ||
                        prefix.Equals("gh", StringComparison.OrdinalIgnoreCase) ||
                        prefix.Equals("http", StringComparison.OrdinalIgnoreCase);

            if (known)
            {
                explicitRegistry = prefix;
                package = package.Substring(separator + 1);
            }
        }

        if (explicitRegistry is not null)
        {
            var client = CreateClient(explicitRegistry);
            return new ResolvedReference(client, package, reference.Trim());
        }

        // GitHub owner/repo shorthand.
        if (package.Contains('/'))
        {
            var github = CreateClient("github");
            return new ResolvedReference(github, package, reference.Trim());
        }

        // Default registry.
        var defaultName = _config.DefaultRegistry;
        if (string.IsNullOrWhiteSpace(defaultName))
        {
            var httpRegistries = _config.Registries
                .Where(r => r.Type.Equals("http", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (httpRegistries.Count == 1)
                defaultName = httpRegistries[0].Name;
        }

        if (string.IsNullOrWhiteSpace(defaultName))
        {
            throw new BinPmException(
                $"Cannot determine which registry '{package}' belongs to. " +
                $"Use 'owner/repo' for GitHub, 'registry:{package}' for a configured registry, " +
                $"or set 'defaultRegistry' in the config.");
        }

        var defaultClient = CreateClient(defaultName);
        return new ResolvedReference(defaultClient, package, reference.Trim());
    }

    /// <summary>Creates a registry client by name (<c>github</c>, <c>gh</c>, or a configured registry).</summary>
    public IRegistryClient CreateClient(string name)
    {
        if (name.Equals("github", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("gh", StringComparison.OrdinalIgnoreCase))
        {
            return new GitHubRegistryClient(_http, _config.GitHubToken);
        }

        var registry = _config.FindRegistry(name);
        if (registry is null)
            throw new BinPmException(
                $"Unknown registry '{name}'. Configured registries: {DescribeConfiguredRegistries()}.");

        if (registry.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
        {
            return new GitHubRegistryClient(_http, _config.GitHubToken, registry.Ref);
        }

        if (string.IsNullOrWhiteSpace(registry.Url))
            throw new BinPmException($"Registry '{name}' does not define a 'url'.");

        return new HttpRegistryClient(_http, registry.Url, registry.Name);
    }

    /// <summary>All registries configured by the user.</summary>
    public IReadOnlyList<RegistryConfig> GetRegistries() => _config.Registries;

    private string DescribeConfiguredRegistries() =>
        _config.Registries.Count == 0
            ? "(none)"
            : string.Join(", ", _config.Registries.Select(r => r.Name));
}

public sealed class ResolvedReference
{
    public ResolvedReference(IRegistryClient client, string packageName, string source)
    {
        Client = client;
        PackageName = packageName;
        Source = source;
    }

    public IRegistryClient Client { get; }
    public string PackageName { get; }
    public string Source { get; }
}
