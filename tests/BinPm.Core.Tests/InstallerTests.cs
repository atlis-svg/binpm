using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using BinPm.Core.Install;
using BinPm.Core.Models;
using BinPm.Core.Registry;

namespace BinPm.Core.Tests;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (byte[] Content, string ContentType)> _responses = new();

    public List<string> Requests { get; } = new();

    public void AddText(string url, string content) =>
        _responses[url] = (Encoding.UTF8.GetBytes(content), "application/json");

    public void AddBytes(string url, byte[] content) =>
        _responses[url] = (content, "application/octet-stream");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);

        if (_responses.TryGetValue(url, out var entry))
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(entry.Content),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(entry.ContentType);
            return Task.FromResult(response);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("not found"),
        });
    }
}

public class InstallerTests
{
    private static readonly Platform Linux = new(OsKind.Linux, ArchKind.X64);

    private static byte[] BuildTarGz(string fileName, string content, UnixFileMode? mode = null)
    {
        using var buffer = new MemoryStream();

        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            using var writer = new TarWriter(gzip, TarEntryFormat.Pax);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, fileName)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
            };

            if (mode is not null)
                entry.Mode = mode.Value;

            writer.WriteEntry(entry);
        }

        return buffer.ToArray();
    }

    private static string Sha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static Paths CreatePaths(TempDirectory temp) => new(
        root: temp.Path,
        packagesDir: Path.Combine(temp.Path, "packages"),
        binDir: Path.Combine(temp.Path, "bin"),
        cacheDir: Path.Combine(temp.Path, "cache"),
        installedDbPath: Path.Combine(temp.Path, "installed.json"));

    [Fact]
    public async Task Installs_a_package_from_an_http_registry()
    {
        using var temp = new TempDirectory();
        var paths = CreatePaths(temp);
        paths.EnsureDirectories();

        var archive = BuildTarGz(
            "hello",
            "#!/bin/sh\necho hello\n",
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var manifestJson = $$"""
        {
          "name": "demo",
          "description": "Demo package",
          "assetUrlTemplate": "http://registry.test/{name}",
          "versions": [
            {
              "version": "1.0.0",
              "assets": [
                {
                  "name": "demo-1.0.0-linux-x64.tar.gz",
                  "sha256": "{{Sha256(archive)}}",
                  "bins": ["hello"]
                }
              ]
            }
          ]
        }
        """;

        var handler = new FakeHttpMessageHandler();
        handler.AddText("http://registry.test/demo/manifest.json", manifestJson);
        handler.AddBytes("http://registry.test/demo-1.0.0-linux-x64.tar.gz", archive);

        using var http = new HttpClient(handler);
        var client = new HttpRegistryClient(http, "http://registry.test", "test");

        var manifest = await client.GetPackageAsync("demo");
        var resolved = new PackageResolver(Linux).Resolve(manifest, null, "test:demo");

        var installer = new BinaryInstaller(paths, http);
        var result = await installer.InstallAsync(resolved);

        Assert.False(result.AlreadyInstalled);
        Assert.Equal("1.0.0", result.Record.Version);
        Assert.Equal(new[] { "hello" }, result.Record.Bins.ToArray());

        // The extracted binary exists.
        var installedBinary = Path.Combine(paths.PackagesDir, "demo", "1.0.0", "hello");
        Assert.True(File.Exists(installedBinary));

        // The shim exists and points at the installed binary.
        var shimPath = Path.Combine(paths.BinDir, OperatingSystem.IsWindows() ? "hello.cmd" : "hello");
        Assert.True(File.Exists(shimPath));
        Assert.Equal(installedBinary, installer.Shims.GetShimTarget(shimPath));

        // The package is recorded.
        var installed = installer.GetInstalled("demo");
        Assert.NotNull(installed);
        Assert.Equal("test:demo", installed!.Source);
    }

    [Fact]
    public async Task Rejects_an_asset_with_a_wrong_checksum()
    {
        using var temp = new TempDirectory();
        var paths = CreatePaths(temp);
        paths.EnsureDirectories();

        var archive = BuildTarGz("hello", "content");

        var manifestJson = $$"""
        {
          "name": "demo",
          "assetUrlTemplate": "http://registry.test/{name}",
          "versions": [
            {
              "version": "1.0.0",
              "assets": [
                { "name": "demo-1.0.0-linux-x64.tar.gz", "sha256": "{{Sha256("something else"u8.ToArray())}}" }
              ]
            }
          ]
        }
        """;

        var handler = new FakeHttpMessageHandler();
        handler.AddText("http://registry.test/demo/manifest.json", manifestJson);
        handler.AddBytes("http://registry.test/demo-1.0.0-linux-x64.tar.gz", archive);

        using var http = new HttpClient(handler);
        var client = new HttpRegistryClient(http, "http://registry.test", "test");

        var manifest = await client.GetPackageAsync("demo");
        var resolved = new PackageResolver(Linux).Resolve(manifest, null, "test:demo");

        var installer = new BinaryInstaller(paths, http);

        var exception = await Assert.ThrowsAsync<BinPmException>(() => installer.InstallAsync(resolved));

        Assert.Contains("Checksum mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(installer.GetInstalled("demo"));
    }

    [Fact]
    public async Task Uninstalling_removes_the_files_and_the_shim()
    {
        using var temp = new TempDirectory();
        var paths = CreatePaths(temp);
        paths.EnsureDirectories();

        var archive = BuildTarGz("hello", "content");
        var manifestJson = $$"""
        {
          "name": "demo",
          "assetUrlTemplate": "http://registry.test/{name}",
          "versions": [
            {
              "version": "1.0.0",
              "assets": [ { "name": "demo-1.0.0-linux-x64.tar.gz", "bins": ["hello"] } ]
            }
          ]
        }
        """;

        var handler = new FakeHttpMessageHandler();
        handler.AddText("http://registry.test/demo/manifest.json", manifestJson);
        handler.AddBytes("http://registry.test/demo-1.0.0-linux-x64.tar.gz", archive);

        using var http = new HttpClient(handler);
        var client = new HttpRegistryClient(http, "http://registry.test", "test");

        var manifest = await client.GetPackageAsync("demo");
        var resolved = new PackageResolver(Linux).Resolve(manifest, null, "test:demo");

        var installer = new BinaryInstaller(paths, http);
        await installer.InstallAsync(resolved);

        await installer.UninstallAsync("demo");

        Assert.Null(installer.GetInstalled("demo"));
        Assert.False(Directory.Exists(Path.Combine(paths.PackagesDir, "demo")));
        Assert.False(installer.Shims.ShimExists(installer.Shims.GetShimPath("hello")));
    }

    [Fact]
    public async Task Installing_the_same_version_twice_is_a_no_op()
    {
        using var temp = new TempDirectory();
        var paths = CreatePaths(temp);
        paths.EnsureDirectories();

        var archive = BuildTarGz("hello", "content");
        var manifestJson = $$"""
        {
          "name": "demo",
          "assetUrlTemplate": "http://registry.test/{name}",
          "versions": [
            {
              "version": "1.0.0",
              "assets": [ { "name": "demo-1.0.0-linux-x64.tar.gz", "bins": ["hello"] } ]
            }
          ]
        }
        """;

        var handler = new FakeHttpMessageHandler();
        handler.AddText("http://registry.test/demo/manifest.json", manifestJson);
        handler.AddBytes("http://registry.test/demo-1.0.0-linux-x64.tar.gz", archive);

        using var http = new HttpClient(handler);
        var client = new HttpRegistryClient(http, "http://registry.test", "test");

        var manifest = await client.GetPackageAsync("demo");
        var resolved = new PackageResolver(Linux).Resolve(manifest, null, "test:demo");

        var installer = new BinaryInstaller(paths, http);
        await installer.InstallAsync(resolved);

        var requestsAfterFirstInstall = handler.Requests.Count;

        var second = await installer.InstallAsync(resolved);

        Assert.True(second.AlreadyInstalled);
        Assert.Equal(requestsAfterFirstInstall, handler.Requests.Count);
    }

    [Fact]
    public async Task Updating_installs_the_new_version_and_removes_the_old_one()
    {
        using var temp = new TempDirectory();
        var paths = CreatePaths(temp);
        paths.EnsureDirectories();

        var archiveV1 = BuildTarGz("hello", "v1");
        var archiveV2 = BuildTarGz("hello", "v2");

        var manifestJson = $$"""
        {
          "name": "demo",
          "assetUrlTemplate": "http://registry.test/{name}",
          "versions": [
            {
              "version": "1.0.0",
              "assets": [ { "name": "demo-1.0.0-linux-x64.tar.gz", "bins": ["hello"] } ]
            },
            {
              "version": "2.0.0",
              "assets": [ { "name": "demo-2.0.0-linux-x64.tar.gz", "bins": ["hello"] } ]
            }
          ]
        }
        """;

        var handler = new FakeHttpMessageHandler();
        handler.AddText("http://registry.test/demo/manifest.json", manifestJson);
        handler.AddBytes("http://registry.test/demo-1.0.0-linux-x64.tar.gz", archiveV1);
        handler.AddBytes("http://registry.test/demo-2.0.0-linux-x64.tar.gz", archiveV2);

        using var http = new HttpClient(handler);
        var client = new HttpRegistryClient(http, "http://registry.test", "test");

        var manifest = await client.GetPackageAsync("demo");
        var resolver = new PackageResolver(Linux);

        var installer = new BinaryInstaller(paths, http);
        await installer.InstallAsync(resolver.Resolve(manifest, "1.0.0", "test:demo"));

        var updated = await installer.UpdateAsync("demo", null, "test:demo", manifest);

        Assert.Equal("2.0.0", updated.Record.Version);
        Assert.True(File.Exists(Path.Combine(paths.PackagesDir, "demo", "2.0.0", "hello")));
        Assert.False(Directory.Exists(Path.Combine(paths.PackagesDir, "demo", "1.0.0")));
    }

    [Fact]
    public async Task Finds_a_binary_inside_the_archives_top_level_directory()
    {
        using var temp = new TempDirectory();
        var paths = CreatePaths(temp);
        paths.EnsureDirectories();

        // The archive wraps its content in a single directory, like most release tarballs.
        var archive = BuildTarGz("tool-1.0.0/hello", "content");

        var manifestJson = $$"""
        {
          "name": "demo",
          "assetUrlTemplate": "http://registry.test/{name}",
          "versions": [
            {
              "version": "1.0.0",
              "assets": [ { "name": "demo-1.0.0-linux-x64.tar.gz", "bins": ["hello"] } ]
            }
          ]
        }
        """;

        var handler = new FakeHttpMessageHandler();
        handler.AddText("http://registry.test/demo/manifest.json", manifestJson);
        handler.AddBytes("http://registry.test/demo-1.0.0-linux-x64.tar.gz", archive);

        using var http = new HttpClient(handler);
        var client = new HttpRegistryClient(http, "http://registry.test", "test");

        var manifest = await client.GetPackageAsync("demo");
        var resolved = new PackageResolver(Linux).Resolve(manifest, null, "test:demo");

        var installer = new BinaryInstaller(paths, http);
        var result = await installer.InstallAsync(resolved);

        Assert.True(File.Exists(Path.Combine(paths.PackagesDir, "demo", "1.0.0", "tool-1.0.0", "hello")));
        Assert.Equal("hello", result.BinNames.Single());
    }

    [Fact]
    public async Task A_missing_binary_is_reported_with_the_package_contents()
    {
        using var temp = new TempDirectory();
        var paths = CreatePaths(temp);
        paths.EnsureDirectories();

        var archive = BuildTarGz("hello", "content");
        var manifestJson = $$"""
        {
          "name": "demo",
          "assetUrlTemplate": "http://registry.test/{name}",
          "versions": [
            {
              "version": "1.0.0",
              "assets": [ { "name": "demo-1.0.0-linux-x64.tar.gz", "bins": ["missing-binary"] } ]
            }
          ]
        }
        """;

        var handler = new FakeHttpMessageHandler();
        handler.AddText("http://registry.test/demo/manifest.json", manifestJson);
        handler.AddBytes("http://registry.test/demo-1.0.0-linux-x64.tar.gz", archive);

        using var http = new HttpClient(handler);
        var client = new HttpRegistryClient(http, "http://registry.test", "test");

        var manifest = await client.GetPackageAsync("demo");
        var resolved = new PackageResolver(Linux).Resolve(manifest, null, "test:demo");

        var installer = new BinaryInstaller(paths, http);

        var exception = await Assert.ThrowsAsync<BinPmException>(() => installer.InstallAsync(resolved));

        Assert.Contains("missing-binary", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
