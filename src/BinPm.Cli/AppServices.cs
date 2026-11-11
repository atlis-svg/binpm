using System.Net;
using BinPm.Cli.Cli;
using BinPm.Core.Configuration;
using BinPm.Core.Install;
using BinPm.Core.Registry;

namespace BinPm.Cli;

/// <summary>Shared services used by command handlers.</summary>
public sealed class AppServices
{
    public AppServices(
        BinPmConfig config,
        Paths paths,
        HttpClient http,
        RegistryResolver resolver,
        BinaryInstaller installer,
        ConsoleProgress progress)
    {
        Config = config;
        Paths = paths;
        Http = http;
        Resolver = resolver;
        Installer = installer;
        Progress = progress;
    }

    public BinPmConfig Config { get; }
    public Paths Paths { get; }
    public HttpClient Http { get; }
    public RegistryResolver Resolver { get; }
    public BinaryInstaller Installer { get; }
    public ConsoleProgress Progress { get; }

    public ShimManager Shims => Installer.Shims;

    public static AppServices Create(CommandContext context)
    {
        var configPath = context.Opt("config");
        var config = BinPmConfig.Load(configPath);
        var paths = Paths.Create(config, context.Opt("root"), context.Opt("bin-dir"));
        paths.EnsureDirectories();

        var http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"binpm/{Program.Version}");

        var progress = new ConsoleProgress(context.Flag("verbose"));
        var installer = new BinaryInstaller(paths, http, progress);

        return new AppServices(config, paths, http, new RegistryResolver(config, http), installer, progress);
    }
}
