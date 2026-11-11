using BinPm.Cli.Cli;
using BinPm.Core;
using BinPm.Core.Models;

namespace BinPm.Cli.Commands;

public static class SearchCommand
{
    public static IReadOnlyList<CliOption> Options { get; } = new List<CliOption>
    {
        new("registry", true, "Only search this registry."),
        new("limit", true, "Maximum number of results (default 50)."),
    };

    public static async Task<int> HandleAsync(CommandContext context)
    {
        var services = AppServices.Create(context);
        var term = context.RequiredArgument(0, "search term");

        var limit = 50;
        if (context.Opt("limit") is not null && int.TryParse(context.Opt("limit"), out var parsedLimit))
            limit = Math.Max(1, parsedLimit);

        var results = new List<PackageSummary>();
        var errors = new List<string>();

        var registryName = context.Opt("registry");
        if (!string.IsNullOrWhiteSpace(registryName))
        {
            await SearchRegistryAsync(services, registryName, term, errors, results);
        }
        else
        {
            foreach (var registry in services.Resolver.GetRegistries())
            {
                await SearchRegistryAsync(services, registry.Name, term, errors, results);
            }

            await SearchRegistryAsync(services, "github", term, errors, results);
        }

        if (results.Count == 0)
        {
            Output.Info($"No packages found for '{term}'.");
        }
        else
        {
            var rows = results
                .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(r => (IReadOnlyList<string>)new List<string>
                {
                    r.Name,
                    Truncate(r.Description ?? "", 60),
                    r.Registry ?? "",
                })
                .ToList();

            Output.Table(new[] { "NAME", "DESCRIPTION", "REGISTRY" }, rows);
        }

        foreach (var error in errors)
            Output.Warning(error);

        return 0;
    }

    private static async Task SearchRegistryAsync(
        AppServices services,
        string registryName,
        string term,
        List<string> errors,
        List<PackageSummary> results)
    {
        try
        {
            var client = services.Resolver.CreateClient(registryName);
            var matches = await client.SearchAsync(term);
            results.AddRange(matches);
        }
        catch (Exception ex) when (ex is BinPmException or HttpRequestException)
        {
            errors.Add($"{registryName}: {ex.Message}");
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value.Substring(0, maxLength - 1) + "…";
}
