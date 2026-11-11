using BinPm.Cli.Cli;
using BinPm.Core;
using BinPm.Core.Install;
using BinPm.Core.Models;

namespace BinPm.Cli.Commands;

public static class UpdateCommand
{
    public static IReadOnlyList<CliOption> Options { get; } = new List<CliOption>
    {
        new("bin", true, "Binary to shim, as a path relative to the package root."),
        new("asset", true, "Select a specific asset by name pattern."),
        new("registry", true, "Registry to use instead of the recorded source."),
        new("force", false, "Reinstall even when the version is unchanged."),
        new("no-shim", false, "Do not create shims."),
        new("overwrite-shims", false, "Replace shims owned by another package."),
    };

    public static async Task<int> HandleAsync(CommandContext context)
    {
        var services = AppServices.Create(context);
        var reference = context.Arguments.FirstOrDefault();
        string? versionSpec = null;

        if (reference is not null)
        {
            var (packageReference, inlineVersion) = Util.SplitVersion(reference);
            reference = packageReference;
            versionSpec = inlineVersion;
        }

        List<InstalledPackage> targets;
        if (reference is null)
        {
            targets = services.Installer.GetInstalled().ToList();
        }
        else
        {
            var installed = services.Installer.GetInstalled(reference);
            if (installed is null)
            {
                Output.Warning($"Package '{reference}' is not installed.");
                return 0;
            }

            targets = new List<InstalledPackage> { installed };
        }

        if (targets.Count == 0)
        {
            Output.Info("No packages installed. Use 'binpm install <package>' first.");
            return 0;
        }

        var updated = 0;
        var upToDate = 0;
        var skipped = 0;

        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.Source))
            {
                Output.Warning($"Skipping {target.Name}: no source is recorded for this installation.");
                skipped++;
                continue;
            }

            try
            {
                var resolvedReference = services.Resolver.Resolve(target.Source, context.Opt("registry"));
                var manifest = await resolvedReference.Client.GetPackageAsync(resolvedReference.PackageName);

                var options = new InstallOptions
                {
                    BinOverride = context.Opt("bin"),
                    Force = context.Flag("force"),
                    NoShims = context.Flag("no-shim"),
                    OverwriteShims = context.Flag("overwrite-shims"),
                    Offline = context.Flag("offline"),
                };

                var result = await services.Installer.UpdateAsync(
                    target.Name, versionSpec, resolvedReference.Source, manifest, options);

                if (result.AlreadyInstalled)
                {
                    Output.Info($"{target.Name} is up to date ({target.Version}).");
                    upToDate++;
                }
                else
                {
                    Output.Success($"{target.Name} updated to {result.Record.Version}.");
                    updated++;
                }
            }
            catch (BinPmException ex)
            {
                Output.Warning($"Skipping {target.Name}: {ex.Message}");
                skipped++;
            }
            catch (HttpRequestException ex)
            {
                Output.Warning($"Skipping {target.Name}: network error ({ex.Message}).");
                skipped++;
            }
        }

        if (updated == 0 && skipped == 0)
            Output.Info($"All {upToDate} package(s) are up to date.");

        return 0;
    }
}
