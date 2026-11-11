using BinPm.Cli.Cli;
using BinPm.Core.Install;

namespace BinPm.Cli.Commands;

public static class InstallCommand
{
    public static IReadOnlyList<CliOption> Options { get; } = new List<CliOption>
    {
        new("bin", true, "Binary to shim, as a path relative to the package root."),
        new("asset", true, "Select a specific asset by name pattern."),
        new("registry", true, "Registry to use instead of the default."),
        new("force", false, "Reinstall even when the same version is installed."),
        new("no-shim", false, "Do not create shims."),
        new("overwrite-shims", false, "Replace shims owned by another package."),
    };

    public static async Task<int> HandleAsync(CommandContext context)
    {
        var services = AppServices.Create(context);
        var reference = context.RequiredArgument(0, "package");

        var (packageReference, inlineVersion) = Util.SplitVersion(reference);
        var versionSpec = inlineVersion;

        if (string.IsNullOrEmpty(versionSpec) && context.Arguments.Count > 1)
            versionSpec = context.Arguments[1];

        var resolvedReference = services.Resolver.Resolve(packageReference, context.Opt("registry"));
        Output.Verbose($"Resolved '{packageReference}' to {resolvedReference.Client.DisplayName}");

        Output.Info($"Fetching manifest for '{resolvedReference.PackageName}'...");
        var manifest = await resolvedReference.Client.GetPackageAsync(resolvedReference.PackageName);

        var package = new PackageResolver()
            .Resolve(manifest, versionSpec, resolvedReference.Source, context.Opt("asset"));

        Output.Info($"Selected {manifest.Name} {package.PackageVersion.Version} ({package.Asset.Name})");

        var existing = services.Installer.GetInstalled(manifest.Name);
        if (existing is not null)
        {
            if (existing.Version == package.PackageVersion.Version && !context.Flag("force"))
            {
                Output.Info($"{manifest.Name} {existing.Version} is already installed. Use --force to reinstall.");
                return 0;
            }

            Output.Info($"Replacing {manifest.Name} {existing.Version} with {package.PackageVersion.Version}.");
        }

        var options = new InstallOptions
        {
            BinOverride = context.Opt("bin"),
            Force = context.Flag("force"),
            NoShims = context.Flag("no-shim"),
            OverwriteShims = context.Flag("overwrite-shims"),
            Offline = context.Flag("offline"),
        };

        var result = await services.Installer.InstallAsync(package, options);
        CommandHelpers.PrintInstallResult(services, result);
        return 0;
    }
}
