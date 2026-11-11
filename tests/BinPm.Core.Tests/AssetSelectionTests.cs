using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BinPm.Core.Install;
using BinPm.Core.Models;
using BinPm.Core.Registry;

namespace BinPm.Core.Tests;

/// <summary>Regression tests for asset selection and binary discovery.</summary>
public class AssetSelectionTests
{
    private static readonly Platform Linux = new(OsKind.Linux, ArchKind.X64);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _responses = new();

        public void Add(string url, byte[] content) => _responses[url] = content;

        public void Add(string url, string content) => _responses[url] = Encoding.UTF8.GetBytes(content);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (_responses.TryGetValue(url, out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private static Paths CreatePaths(TempDirectory temp) => new(
        root: temp.Path,
        packagesDir: Path.Combine(temp.Path, "packages"),
        binDir: Path.Combine(temp.Path, "bin"),
        cacheDir: Path.Combine(temp.Path, "cache"),
        installedDbPath: Path.Combine(temp.Path, "installed.json"));

    private static async Task<InstallResult> InstallAsync(
        PackageManifest manifest,
        string assetUrl,
        byte[] assetBytes,
        Paths paths)
    {
        var handler = new StubHandler();
        handler.Add(assetUrl, assetBytes);
        handler.Add(
            "http://registry.test/demo/manifest.json",
            JsonDefaults.Serialize(manifest));

        using var http = new HttpClient(handler);
        var client = new HttpRegistryClient(http, "http://registry.test", "test");
        var loaded = await client.GetPackageAsync("demo");
        var resolved = new PackageResolver(Linux).Resolve(loaded, null, "test:demo");

        var installer = new BinaryInstaller(paths, http);
        return await installer.InstallAsync(resolved);
    }

    [Fact]
    public async Task Finds_a_binary_in_a_zip_that_carries_no_unix_permissions()
    {
        using var temp = new TempDirectory();
        var paths = CreatePaths(temp);
        paths.EnsureDirectories();

        var zip = BuildZip(("tool", "#!/bin/sh\necho hi\n"));

        var manifest = new PackageManifest
        {
            Name = "demo",
            AssetUrlTemplate = "http://registry.test/{name}",
            Versions = new List<PackageVersion>
            {
                new()
                {
                    Version = "1.0.0",
                    Assets = new List<Asset> { new() { Name = "demo-1.0.0-linux-x64.zip" } },
                },
            },
        };

        var result = await InstallAsync(
            manifest,
            "http://registry.test/demo-1.0.0-linux-x64.zip",
            zip,
            paths);

        Assert.Equal("tool", result.BinNames.Single());
        Assert.True(File.Exists(Path.Combine(paths.PackagesDir, "demo", "1.0.0", "tool")));
    }

    [Fact]
    public async Task Finds_a_binary_nested_in_subdirectories()
    {
        using var temp = new TempDirectory();
        var paths = CreatePaths(temp);
        paths.EnsureDirectories();

        var zip = BuildZip(
            ("demo-1.0.0_linux_amd64/bin/demo", "#!/bin/sh\necho hi\n"),
            ("demo-1.0.0_linux_amd64/LICENSE", "license text"));

        var manifest = new PackageManifest
        {
            Name = "demo",
            AssetUrlTemplate = "http://registry.test/{name}",
            Versions = new List<PackageVersion>
            {
                new()
                {
                    Version = "1.0.0",
                    Assets = new List<Asset> { new() { Name = "demo-1.0.0_linux_amd64.zip" } },
                },
            },
        };

        var result = await InstallAsync(
            manifest,
            "http://registry.test/demo-1.0.0_linux_amd64.zip",
            zip,
            paths);

        Assert.Equal("demo", result.BinNames.Single());
        Assert.True(File.Exists(
            Path.Combine(paths.PackagesDir, "demo", "1.0.0", "demo-1.0.0_linux_amd64", "bin", "demo")));
    }

    [Fact]
    public void Deb_and_rpm_packages_are_not_considered_installable()
    {
        Assert.False(AssetClassification.IsInstallableAsset("bat-musl_0.26.1_musl-linux-amd64.deb"));
        Assert.False(AssetClassification.IsInstallableAsset("gh_2.103.3_linux_amd64.rpm"));
        Assert.False(AssetClassification.IsInstallableAsset("tool_1.0_amd64.apk"));
        Assert.True(AssetClassification.IsInstallableAsset("tool-1.0.0-linux-x64.tar.gz"));
        Assert.True(AssetClassification.IsInstallableAsset("tool-1.0.0-linux-x64.zip"));
    }

    [Fact]
    public void Deb_assets_are_skipped_in_favour_of_the_archive()
    {
        var resolver = new PackageResolver(Linux);
        var version = new PackageVersion
        {
            Version = "1.0.0",
            Assets = new List<Asset>
            {
                new() { Name = "bat-musl_0.26.1_musl-linux-amd64.deb" },
                new() { Name = "bat-v0.26.1-x86_64-unknown-linux-musl.tar.gz" },
            },
        };

        var (asset, _) = resolver.SelectAsset(version);

        Assert.Equal("bat-v0.26.1-x86_64-unknown-linux-musl.tar.gz", asset.Name);
    }

    [Fact]
    public void The_main_binary_archive_wins_over_per_binary_archives()
    {
        var resolver = new PackageResolver(Linux);
        var version = new PackageVersion
        {
            Version = "1.0.0",
            Assets = new List<Asset>
            {
                new() { Name = "atuin-server-x86_64-unknown-linux-gnu.tar.gz" },
                new() { Name = "atuin-client-x86_64-unknown-linux-gnu.tar.gz" },
                new() { Name = "atuin-x86_64-unknown-linux-gnu.tar.gz" },
            },
        };

        var (asset, _) = resolver.SelectAsset(version);

        Assert.Equal("atuin-x86_64-unknown-linux-gnu.tar.gz", asset.Name);
    }
}
