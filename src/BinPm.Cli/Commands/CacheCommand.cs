using BinPm.Cli.Cli;

namespace BinPm.Cli.Commands;

public static class CacheCommand
{
    public static IReadOnlyList<CliOption> Options { get; } = new List<CliOption>
    {
        new("all", false, "Also remove installed packages and the package database."),
    };

    public static Task<int> HandleAsync(CommandContext context)
    {
        var action = context.Arguments.FirstOrDefault() ?? "clean";

        if (action != "clean")
        {
            Output.Error($"Unknown cache action '{action}'. Usage: binpm cache clean");
            return Task.FromResult(2);
        }

        var services = AppServices.Create(context);
        var cacheSize = Util.DirectorySize(services.Paths.CacheDir);

        if (Directory.Exists(services.Paths.CacheDir))
            Directory.Delete(services.Paths.CacheDir, recursive: true);

        services.Paths.EnsureDirectories();
        Output.Success($"Cleared the download cache ({Util.FormatBytes(cacheSize)} freed).");

        return Task.FromResult(0);
    }
}
