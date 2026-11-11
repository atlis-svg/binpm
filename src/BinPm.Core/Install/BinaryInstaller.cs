using System.Runtime.InteropServices;
using BinPm.Core.Models;

namespace BinPm.Core.Install;

/// <summary>Installs, updates and uninstalls binary packages.</summary>
public sealed class BinaryInstaller
{
    private readonly Paths _paths;
    private readonly HttpClient _http;
    private readonly ShimManager _shims;
    private readonly InstalledPackageStore _store;
    private readonly IInstallReporter? _reporter;

    public BinaryInstaller(Paths paths, HttpClient http, IInstallReporter? reporter = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _reporter = reporter;
        _shims = new ShimManager(paths.BinDir);
        _store = new InstalledPackageStore(paths.InstalledDbPath);
        _store.Load();
    }

    public ShimManager Shims => _shims;

    public InstalledPackageStore Store => _store;

    public IReadOnlyList<InstalledPackage> GetInstalled() => _store.GetAll();

    public InstalledPackage? GetInstalled(string name) => _store.Get(name);

    /// <summary>Installs a resolved package.</summary>
    public async Task<InstallResult> InstallAsync(
        ResolvedPackage resolved,
        InstallOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new InstallOptions();
        var manifest = resolved.Manifest;
        var name = manifest.Name;

        var existing = _store.Get(name);
        if (existing is not null &&
            existing.Version == resolved.PackageVersion.Version &&
            !options.Force)
        {
            return new InstallResult(existing, Array.Empty<ShimInfo>(), alreadyInstalled: true);
        }

        if (!resolved.AssetMatchedPlatform)
        {
            _reporter?.ReportWarning(
                $"Asset '{resolved.Asset.Name}' does not declare the platform {Platform.Current}; using it anyway.");
        }

        var installDir = GetInstallDir(name, resolved.PackageVersion.Version);
        var tempRoot = Path.Combine(Path.GetDirectoryName(installDir)!, ".binpm-tmp");
        var tempDir = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var cachePath = await DownloadAsync(resolved, options, ct);

            _reporter?.Report($"Extracting {resolved.Asset.Name}");
            var extraction = await ArchiveExtraction.ExtractAsync(cachePath, tempDir, resolved.Asset.Name, ct);

            MarkExecutableBinaries(tempDir);

            var bins = ResolveBins(resolved, tempDir, options, extraction);

            var parent = Path.GetDirectoryName(installDir)!;
            Directory.CreateDirectory(parent);

            if (Directory.Exists(installDir))
                Directory.Delete(installDir, recursive: true);

            Directory.Move(tempDir, installDir);
            tempDir = null!; // moved

            var shims = new List<ShimInfo>();
            if (!options.NoShims)
            {
                var requests = bins
                    .Select(bin => new ShimRequest(bin.Name, Path.Combine(installDir, bin.RelativePath)))
                    .ToList();

                shims.AddRange(_shims.CreateShims(requests, options.OverwriteShims));
            }

            var record = new InstalledPackage
            {
                Name = name,
                Version = resolved.PackageVersion.Version,
                InstalledAt = DateTimeOffset.UtcNow,
                Source = resolved.Source,
                AssetName = resolved.Asset.Name,
                InstallDir = installDir,
                Bins = bins.Select(b => b.RelativePath).ToList(),
                Sha256 = resolved.Asset.Sha256,
            };

            _store.Upsert(record);
            _store.Save();

            _reporter?.Report(
                $"Installed {name} {resolved.PackageVersion.Version} ({string.Join(", ", bins.Select(b => b.Name))})");

            return new InstallResult(record, shims, alreadyInstalled: false);
        }
        finally
        {
            if (tempDir is not null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
            }

            try
            {
                if (Directory.Exists(tempRoot) && !Directory.EnumerateFileSystemEntries(tempRoot).Any())
                    Directory.Delete(tempRoot);
            }
            catch { /* best-effort */ }
        }
    }

    /// <summary>Updates an installed package to the newest (or requested) version.</summary>
    public async Task<InstallResult> UpdateAsync(
        string name,
        string? versionSpec,
        string source,
        PackageManifest manifest,
        InstallOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new InstallOptions();

        var existing = _store.Get(name);
        if (existing is null)
            throw new BinPmException($"Package '{name}' is not installed. Use 'binpm install {name}' instead.");

        var resolver = new PackageResolver();
        var resolved = resolver.Resolve(manifest, versionSpec, source);

        if (existing.Version == resolved.PackageVersion.Version && !options.Force)
        {
            return new InstallResult(existing, Array.Empty<ShimInfo>(), alreadyInstalled: true);
        }

        // Remove the old shims first so the new version can claim the same names.
        var oldShims = existing.Bins
            .Select(bin => (Name: GetBinName(Path.Combine(existing.InstallDir, bin)), ExpectedTarget: (string?)Path.Combine(existing.InstallDir, bin)))
            .ToList();
        _shims.RemoveShims(oldShims);

        var updateOptions = options with { Force = true };
        var result = await InstallAsync(resolved, updateOptions, ct);

        if (Directory.Exists(existing.InstallDir) && !PathsEqual(existing.InstallDir, result.Record.InstallDir))
        {
            try { Directory.Delete(existing.InstallDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _reporter?.ReportWarning(
                    $"Could not remove the old version directory '{existing.InstallDir}': {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>Removes an installed package, its shims and its files.</summary>
    public Task UninstallAsync(string name, bool removeShims = true, CancellationToken ct = default)
    {
        var record = _store.Get(name);
        if (record is null)
            throw new BinPmException($"Package '{name}' is not installed.");

        if (removeShims)
        {
            var shims = record.Bins
                .Select(bin => (Name: GetBinName(Path.Combine(record.InstallDir, bin)), ExpectedTarget: (string?)Path.Combine(record.InstallDir, bin)))
                .ToList();
            _shims.RemoveShims(shims);
        }

        if (Directory.Exists(record.InstallDir))
            Directory.Delete(record.InstallDir, recursive: true);

        var packageDir = Path.GetDirectoryName(record.InstallDir);
        if (!string.IsNullOrEmpty(packageDir) && Directory.Exists(packageDir) &&
            !Directory.EnumerateFileSystemEntries(packageDir).Any())
        {
            Directory.Delete(packageDir);
        }

        _store.Remove(name);
        _store.Save();

        _reporter?.Report($"Uninstalled {name} {record.Version}");
        return Task.CompletedTask;
    }

    private async Task<string> DownloadAsync(ResolvedPackage resolved, InstallOptions options, CancellationToken ct)
    {
        var asset = resolved.Asset;
        var url = asset.Url;

        if (string.IsNullOrWhiteSpace(url))
            throw new BinPmException($"Asset '{asset.Name}' does not have a download URL.");

        var cachePath = Path.Combine(
            _paths.CacheDir,
            $"{SanitizeFileName(resolved.Manifest.Name)}-{SanitizeFileName(resolved.PackageVersion.Version)}-{SanitizeFileName(Path.GetFileName(asset.Name))}");

        if (File.Exists(cachePath))
        {
            if (string.IsNullOrWhiteSpace(asset.Sha256))
            {
                _reporter?.Report($"Using cached {asset.Name}");
                return cachePath;
            }

            var cachedHash = await HashChecker.ComputeSha256Async(cachePath, ct);
            if (HashChecker.Matches(cachedHash, asset.Sha256))
            {
                _reporter?.Report($"Using cached {asset.Name}");
                return cachePath;
            }

            File.Delete(cachePath);
        }

        if (options.Offline)
            throw new BinPmException($"Asset '{asset.Name}' is not in the local cache and --offline was specified.");

        await Downloader.DownloadAsync(_http, url, cachePath, asset.Sha256, _reporter, ct);
        return cachePath;
    }

    private List<ResolvedBin> ResolveBins(
        ResolvedPackage resolved,
        string extractDir,
        InstallOptions options,
        ExtractionResult extraction)
    {
        var explicitBins = options.BinOverride is not null
            ? new List<string> { options.BinOverride }
            : resolved.Asset.Bins ?? resolved.Manifest.Binaries;

        if (explicitBins is not null && explicitBins.Count > 0)
        {
            var bins = new List<ResolvedBin>();

            foreach (var bin in explicitBins)
            {
                var relative = bin.Replace('\\', '/').TrimStart('/');
                var fullPath = Path.GetFullPath(Path.Combine(extractDir, relative));

                if (!File.Exists(fullPath))
                {
                    // Archives often wrap their content in a single top-level directory.
                    var wrapper = FindSingleTopLevelDirectory(extractDir);
                    if (wrapper is not null)
                    {
                        var nested = Path.GetFullPath(Path.Combine(wrapper, relative));
                        if (File.Exists(nested))
                        {
                            bins.Add(new ResolvedBin(
                                GetBinName(nested),
                                Path.GetRelativePath(extractDir, nested)));
                            continue;
                        }
                    }

                    // The published file may carry platform decoration ("jq-linux-amd64" for "jq").
                    var byCleanName = FindExecutableCandidates(extractDir).FirstOrDefault(candidate =>
                        string.Equals(
                            GetAssetBinaryName(candidate),
                            Path.GetFileName(relative),
                            StringComparison.OrdinalIgnoreCase));

                    if (byCleanName is not null)
                    {
                        bins.Add(new ResolvedBin(
                            Path.GetFileName(relative),
                            Path.GetRelativePath(extractDir, byCleanName)));
                        continue;
                    }

                    throw new BinPmException(
                        $"Binary '{bin}' was not found in the package. Package contents: {DescribeContents(extractDir)}");
                }

                bins.Add(new ResolvedBin(GetBinName(fullPath), relative));
            }

            return bins;
        }

        var candidates = FindExecutableCandidates(extractDir);
        if (candidates.Count == 0)
        {
            throw new BinPmException(
                "No executable binaries were found in the package. Specify one with --bin <path>.");
        }

        // A package may ship several commands (flint, flint-player, ...). Shim all of them,
        // the way a Portage ebuild installs every file listed in bin/.
        var detectedBins = new List<ResolvedBin>();
        foreach (var candidate in candidates)
        {
            var relative = Path.GetRelativePath(extractDir, candidate);
            var name = extraction.SingleFile
                ? GetAssetBinaryName(resolved.Asset.Name)
                : GetBinName(candidate);

            detectedBins.Add(new ResolvedBin(name, relative));
        }

        return detectedBins;
    }

    private static List<string> FindExecutableCandidates(string root)
    {
        var files = TopLevelExecutables(root);
        if (files.Count > 0)
            return files;

        // Archives often wrap their content in a single top-level directory.
        var wrapper = FindSingleTopLevelDirectory(root);
        if (wrapper is not null)
        {
            files = TopLevelExecutables(wrapper);
            if (files.Count > 0)
                return files;
        }

        // Some packages nest the binary deeper ("gh_2.103.3_linux_amd64/bin/gh").
        try
        {
            files = Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(IsExecutableCandidate)
                .ToList();
        }
        catch
        {
            files = new List<string>();
        }

        return files;
    }

    private static List<string> TopLevelExecutables(string directory)
    {
        try
        {
            return Directory
                .GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(IsExecutableCandidate)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>Returns the only subdirectory of <paramref name="root"/>, or null when there is not exactly one.</summary>
    private static string? FindSingleTopLevelDirectory(string root)
    {
        var directories = Directory.GetDirectories(root);
        return directories.Length == 1 ? directories[0] : null;
    }

    /// <summary>
    /// Zip archives do not carry Unix permissions, so files inside them lose the executable bit.
    /// Files that look like an ELF binary or a script are marked executable again.
    /// </summary>
    private static void MarkExecutableBinaries(string root)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        foreach (var file in files)
        {
            if (IsExecutableCandidate(file))
                continue;

            if (LooksLikeExecutable(file))
                ArchiveExtraction.MakeExecutable(file);
        }
    }

    private static bool LooksLikeExecutable(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[4];

            var read = stream.Read(header);
            if (read >= 4 && header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F')
                return true;

            if (read >= 2 && header[0] == (byte)'#' && header[1] == (byte)'!')
                return true;
        }
        catch
        {
            // not readable: not a candidate
        }

        return false;
    }

    private static bool IsExecutableCandidate(string path)
    {
        var name = Path.GetFileName(path);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".com", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Derives the command name for a shim from a binary path.</summary>
    public static string GetBinName(string binaryPath)
    {
        var name = Path.GetFileName(binaryPath);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var extension in new[] { ".exe", ".com", ".bat", ".cmd" })
            {
                if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return name.Substring(0, name.Length - extension.Length);
            }
        }

        return name;
    }

    private static readonly string[] PlatformWords =
    {
        "linux", "windows", "win", "darwin", "macos", "osx", "apple",
        "gnu", "musl", "pc", "unknown", "static", "portable",
        "amd64", "x86_64", "x86-64", "x64", "x86", "i686", "i386", "ia32", "386",
        "aarch64", "arm64", "armv8", "armv7", "armv6", "armhf", "arm", "64", "32",
    };

    /// <summary>
    /// Derives a command name from a published asset name by dropping archive extensions and the
    /// trailing platform/version decoration that publishers add, e.g.
    /// <c>jq-linux-amd64</c> becomes <c>jq</c> and <c>sops-v3.13.3.linux.amd64</c> becomes <c>sops</c>.
    /// </summary>
    public static string GetAssetBinaryName(string assetName)
    {
        var name = Path.GetFileName(assetName);

        foreach (var extension in ArchiveExtraction.KnownArchiveExtensions)
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - extension.Length);
                break;
            }
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);

        var segments = name.Split(new[] { '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return Path.GetFileNameWithoutExtension(assetName);

        // Only trailing decoration is dropped, so "base64-tool" keeps its name.
        var end = segments.Length;
        while (end > 1 && (IsPlatformWord(segments[end - 1]) || IsVersionWord(segments[end - 1])))
            end--;

        return string.Join("-", segments.Take(end));
    }

    private static bool IsPlatformWord(string segment) =>
        PlatformWords.Contains(segment, StringComparer.OrdinalIgnoreCase) ||
        (segment.Length > 3 &&
         (segment.EndsWith("64", StringComparison.OrdinalIgnoreCase) ||
          segment.EndsWith("32", StringComparison.OrdinalIgnoreCase)));

    private static bool IsVersionWord(string segment)
    {
        if (segment.Length == 0)
            return false;

        if (segment.All(char.IsDigit))
            return true;

        return segment.Length > 1
            && (segment[0] == 'v' || segment[0] == 'V')
            && segment.Skip(1).All(c => char.IsDigit(c) || c == '.');
    }

    private static string DescribeContents(string root)
    {
        var entries = Directory.EnumerateFileSystemEntries(root).Take(10).ToList();
        var suffix = entries.Count == 10 ? ", ..." : "";
        return entries.Count == 0 ? "(empty)" : string.Join(", ", entries.Select(Path.GetFileName)) + suffix;
    }

    private string GetInstallDir(string name, string version) =>
        Path.Combine(_paths.PackagesDir, SanitizeFileName(name), SanitizeFileName(version));

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim();

        if (result.Length == 0)
            throw new BinPmException($"'{name}' is not a valid package name.");

        return result;
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return Path.GetFullPath(left).Equals(Path.GetFullPath(right), comparison);
    }
}
