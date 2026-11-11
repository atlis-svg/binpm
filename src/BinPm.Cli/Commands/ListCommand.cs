using BinPm.Cli.Cli;
using BinPm.Core;
using BinPm.Core.Install;
using BinPm.Core.Models;
using BinPm.Core.Semver;

namespace BinPm.Cli.Commands;

public static class ListCommand
{
    public static IReadOnlyList<CliOption> Options { get; } = new List<CliOption>
    {
        new("available", false, "List packages available in the configured registries."),
        new("outdated", false, "Compare installed packages against the latest available versions."),
        new("registry", true, "Registry to query with --available."),
    };

    public static async Task<int> HandleAsync(CommandContext context)
    {
        var services = AppServices.Create(context);

        if (context.Flag("available"))
        {
            await ListAvailableAsync(services, context);
            return 0;
        }

        var installed = services.Installer.GetInstalled();
        if (installed.Count == 0)
        {
            Output.Info("No packages installed. Try 'binpm search <term>' or 'binpm install <package>'.");
            return 0;
        }

        if (context.Flag("outdated"))
        {
            await ShowOutdatedAsync(services, installed);
            return 0;
        }

        var rows = installed
            .Select(package => (IReadOnlyList<string>)new List<string>
            {
                package.Name,
                package.Version,
                package.Source,
                string.Join(", ", package.Bins.Select(BinaryInstaller.GetBinName)),
            })
            .ToList();

        Output.Table(new[] { "NAME", "VERSION", "SOURCE", "COMMANDS" }, rows);
        Output.Info($"{installed.Count} package(s) installed. Root: {services.Paths.Root}");
        return 0;
    }

    private static async Task ListAvailableAsync(AppServices services, CommandContext context)
    {
        var registryName = context.Opt("registry");

        if (!string.IsNullOrWhiteSpace(registryName))
        {
            var client = services.Resolver.CreateClient(registryName);
            var packages = await client.SearchAsync("");
            PrintAvailable(packages);
            return;
        }

        var httpRegistries = services.Resolver.GetRegistries()
            .Where(r => r.Type.Equals("http", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (httpRegistries.Count == 0)
        {
            Output.Info("No HTTP registries are configured. Add one to the config file or use --registry.");
            return;
        }

        foreach (var registry in httpRegistries)
        {
            Output.Write($"Registry '{registry.Name}' ({registry.Url}):", ConsoleColor.Cyan);
            try
            {
                var client = services.Resolver.CreateClient(registry.Name);
                var packages = await client.SearchAsync("");
                PrintAvailable(packages);
            }
            catch (BinPmException ex)
            {
                Output.Warning(ex.Message);
            }

            Output.Write("");
        }
    }

    private static void PrintAvailable(IReadOnlyList<PackageSummary> packages)
    {
        if (packages.Count == 0)
        {
            Output.Info("  (no packages)");
            return;
        }

        var rows = packages
            .Select(p => (IReadOnlyList<string>)new List<string>
            {
                p.Name,
                Truncate(p.Description ?? "", 70),
            })
            .ToList();

        Output.Table(new[] { "NAME", "DESCRIPTION" }, rows);
    }

    private static async Task ShowOutdatedAsync(AppServices services, IReadOnlyList<InstalledPackage> installed)
    {
        var rows = new List<IReadOnlyList<string>>();

        foreach (var package in installed)
        {
            if (string.IsNullOrWhiteSpace(package.Source))
            {
                rows.Add(new List<string> { package.Name, package.Version, "?", "no source recorded" });
                continue;
            }

            try
            {
                var resolvedReference = services.Resolver.Resolve(package.Source);
                var manifest = await resolvedReference.Client.GetPackageAsync(resolvedReference.PackageName);
                var latest = new PackageResolver().Resolve(manifest, null, resolvedReference.Source);

                var latestVersion = latest.PackageVersion.Version;
                var status = "up to date";

                if (SemVersion.TryParse(latestVersion, out var latestSemver) &&
                    SemVersion.TryParse(package.Version, out var installedSemver))
                {
                    status = latestSemver > installedSemver ? "update available" : "up to date";
                }

                rows.Add(new List<string> { package.Name, package.Version, latestVersion, status });
            }
            catch (Exception ex) when (ex is BinPmException or HttpRequestException)
            {
                rows.Add(new List<string> { package.Name, package.Version, "?", ex.Message });
            }
        }

        Output.Table(new[] { "NAME", "INSTALLED", "LATEST", "STATUS" }, rows);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value.Substring(0, maxLength - 1) + "…";
}
