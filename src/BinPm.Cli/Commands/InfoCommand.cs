using BinPm.Cli.Cli;

namespace BinPm.Cli.Commands;

public static class InfoCommand
{
    public static IReadOnlyList<CliOption> Options { get; } = new List<CliOption>
    {
        new("registry", true, "Registry to use instead of the default."),
    };

    public static async Task<int> HandleAsync(CommandContext context)
    {
        var services = AppServices.Create(context);
        var reference = context.RequiredArgument(0, "package");

        var resolvedReference = services.Resolver.Resolve(reference, context.Opt("registry"));
        var manifest = await resolvedReference.Client.GetPackageAsync(resolvedReference.PackageName);
        var installed = services.Installer.GetInstalled(manifest.Name);

        Output.Write(manifest.Name, ConsoleColor.Cyan);

        if (!string.IsNullOrWhiteSpace(manifest.Description))
        {
            Output.Write("");
            Output.Write(manifest.Description);
        }

        Output.Write("");
        if (!string.IsNullOrWhiteSpace(manifest.Homepage))
            Output.Write($"Homepage:   {manifest.Homepage}");
        if (!string.IsNullOrWhiteSpace(manifest.Repository))
            Output.Write($"Repository: {manifest.Repository}");
        if (!string.IsNullOrWhiteSpace(manifest.License))
            Output.Write($"License:    {manifest.License}");

        Output.Write($"Source:     {resolvedReference.Client.DisplayName}");
        Output.Write($"Installed:  {(installed is null ? "no" : installed.Version)}");

        var versions = manifest.GetSortedVersions().Take(10).ToList();
        if (versions.Count > 0)
        {
            Output.Write("");
            Output.Write("Recent versions:", ConsoleColor.Cyan);

            foreach (var version in versions)
            {
                var packageVersion = manifest.FindVersion(version);
                var assetCount = packageVersion?.Assets.Count ?? 0;
                var marker = installed is not null && installed.Version == version.ToString() ? "  (installed)" : "";
                Output.Write($"  {version}  ({assetCount} asset(s)){marker}");
            }
        }

        return 0;
    }
}
