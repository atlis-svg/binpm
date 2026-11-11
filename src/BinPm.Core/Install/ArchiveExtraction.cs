using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Formats.Tar;

namespace BinPm.Core.Install;

public sealed class ExtractionResult
{
    /// <summary>True when the source was a single file rather than an archive.</summary>
    public bool SingleFile { get; }

    /// <summary>Name of the single extracted file when <see cref="SingleFile"/> is true.</summary>
    public string? FileName { get; }

    public ExtractionResult(bool singleFile, string? fileName)
    {
        SingleFile = singleFile;
        FileName = fileName;
    }
}

/// <summary>
/// Detects and extracts zip, tar, tar.gz/tgz and gzipped single files.
/// Archives are inspected by magic bytes rather than file extension so that misnamed files still work.
/// </summary>
public static class ArchiveExtraction
{
    public static readonly string[] KnownArchiveExtensions =
    {
        ".zip", ".tar.gz", ".tgz", ".tar", ".gz", ".tar.xz", ".txz", ".tar.bz2", ".tbz2", ".7z", ".rar",
    };

    private const long CopyBufferSize = 81920;

    public static bool IsArchiveFileName(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        foreach (var extension in KnownArchiveExtensions)
        {
            if (name.EndsWith(extension, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static Task<ExtractionResult> ExtractAsync(
        string sourcePath,
        string destinationDir,
        CancellationToken ct = default) =>
        ExtractAsync(sourcePath, destinationDir, null, ct);

    /// <summary>
    /// Extracts an archive (or copies a raw file). <paramref name="originalFileName"/> is the name the
    /// asset was published under; it is used for raw and gzipped single files so that the installed
    /// file keeps its published name instead of the name of the local cache file.
    /// </summary>
    public static async Task<ExtractionResult> ExtractAsync(
        string sourcePath,
        string destinationDir,
        string? originalFileName,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationDir);
        var kind = await DetectAsync(sourcePath, ct);

        switch (kind)
        {
            case ArchiveKind.Zip:
                ExtractZip(sourcePath, destinationDir);
                return new ExtractionResult(false, null);

            case ArchiveKind.Gzip:
                if (await IsTarCompressedAsync(sourcePath, ct))
                {
                    await ExtractTarAsync(sourcePath, destinationDir, gzip: true, ct);
                    return new ExtractionResult(false, null);
                }

                var singleName = ExtractGzipSingleFile(sourcePath, destinationDir, originalFileName);
                return new ExtractionResult(true, singleName);

            case ArchiveKind.Tar:
                await ExtractTarAsync(sourcePath, destinationDir, gzip: false, ct);
                return new ExtractionResult(false, null);

            case ArchiveKind.Xz:
                throw new BinPmException(
                    $"'{Path.GetFileName(sourcePath)}' is xz-compressed, which BinPm cannot extract. " +
                    "Use a zip, tar.gz or tar asset, or decompress it and publish a plain executable.");

            case ArchiveKind.Bzip2:
                throw new BinPmException(
                    $"'{Path.GetFileName(sourcePath)}' is bzip2-compressed, which BinPm cannot extract. " +
                    "Use a zip, tar.gz or tar asset, or decompress it and publish a plain executable.");

            default:
                var rawName = Path.GetFileName(originalFileName ?? Path.GetFileName(sourcePath));
                var rawDestination = Path.Combine(destinationDir, rawName);
                File.Copy(sourcePath, rawDestination, overwrite: true);
                MakeExecutable(rawDestination);
                return new ExtractionResult(true, rawName);
        }
    }

    private enum ArchiveKind
    {
        Zip,
        Gzip,
        Tar,
        Xz,
        Bzip2,
        Raw,
    }

    private static async Task<ArchiveKind> DetectAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var header = new byte[512];
        var read = await ReadAtLeastAsync(stream, header, ct);
        return DetectFromHeader(header.AsSpan(0, read));
    }

    private static ArchiveKind DetectFromHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B)
            return ArchiveKind.Zip;

        if (header.Length >= 2 && header[0] == 0x1F && header[1] == 0x8B)
            return ArchiveKind.Gzip;

        if (header.Length >= 262 &&
            header[257] == (byte)'u' && header[258] == (byte)'s' &&
            header[259] == (byte)'t' && header[260] == (byte)'a' && header[261] == (byte)'r')
        {
            return ArchiveKind.Tar;
        }

        if (header.Length >= 6 &&
            header[0] == 0xFD && header[1] == 0x37 && header[2] == 0x7A &&
            header[3] == 0x58 && header[4] == 0x5A && header[5] == 0x00)
        {
            return ArchiveKind.Xz;
        }

        if (header.Length >= 3 && header[0] == 0x42 && header[1] == 0x5A && header[2] == 0x68)
            return ArchiveKind.Bzip2;

        return ArchiveKind.Raw;
    }

    private static async Task<bool> IsTarCompressedAsync(string path, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        var header = new byte[512];
        var read = await ReadAtLeastAsync(gzip, header, ct);
        return DetectFromHeader(header.AsSpan(0, read)) == ArchiveKind.Tar;
    }

    private static async Task<int> ReadAtLeastAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0)
                break;
            total += read;
        }

        return total;
    }

    private static void ExtractZip(string sourcePath, string destinationDir)
    {
        using var archive = new ZipArchive(File.OpenRead(sourcePath), ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            var relative = NormalizeEntryPath(entry.FullName);
            if (relative is null)
                continue;

            var destinationPath = GetSafeDestinationPath(destinationDir, relative);
            if (destinationPath is null)
                continue;

            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            using var input = entry.Open();
            using var output = File.Create(destinationPath);
            input.CopyTo(output, (int)CopyBufferSize);
        }
    }

    private static async Task ExtractTarAsync(string sourcePath, string destinationDir, bool gzip, CancellationToken ct)
    {
        await using var file = File.OpenRead(sourcePath);
        Stream decompressed = gzip ? new GZipStream(file, CompressionMode.Decompress) : file;
        await using (decompressed)
        using (var reader = new TarReader(decompressed, leaveOpen: true))
        {
            TarEntry? entry;
            while ((entry = reader.GetNextEntry(copyData: false)) is not null)
            {
                ct.ThrowIfCancellationRequested();

                var relative = NormalizeEntryPath(entry.Name);
                if (relative is null)
                    continue;

                var destinationPath = GetSafeDestinationPath(destinationDir, relative);
                if (destinationPath is null)
                    continue;

                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(destinationPath);
                        break;

                    case TarEntryType.RegularFile:
                    {
                        var parent = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(parent))
                            Directory.CreateDirectory(parent);

                        using var output = File.Create(destinationPath);
                        if (entry.DataStream is not null)
                            await entry.DataStream.CopyToAsync(output, (int)CopyBufferSize, ct);

                        if (entry.Mode is UnixFileMode mode)
                        {
                            try
                            {
#pragma warning disable CA1416 // unix permissions only
                                File.SetUnixFileMode(destinationPath, mode);
#pragma warning restore CA1416
                            }
                            catch { /* permissions are best-effort */ }
                        }

                        break;
                    }

                    case TarEntryType.SymbolicLink:
                    {
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                            break;

                        try
                        {
                            var parent = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(parent))
                                Directory.CreateDirectory(parent);

                            File.CreateSymbolicLink(destinationPath, entry.LinkName ?? "");
                        }
                        catch { /* best-effort */ }

                        break;
                    }

                    default:
                        break;
                }
            }
        }
    }

    private static string? ExtractGzipSingleFile(string sourcePath, string destinationDir, string? originalFileName)
    {
        var name = originalFileName is not null
            ? Path.GetFileName(originalFileName)
            : Path.GetFileName(sourcePath);

        if (name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 3);
        if (name.Length == 0)
            name = Path.GetFileName(sourcePath);

        var destinationPath = Path.Combine(destinationDir, name);

        using var input = File.OpenRead(sourcePath);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = File.Create(destinationPath);
        gzip.CopyTo(output, (int)CopyBufferSize);

        MakeExecutable(destinationPath);
        return name;
    }

    /// <summary>Sets the executable bit on a file (no-op on Windows).</summary>
    public static void MakeExecutable(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
#pragma warning disable CA1416 // guarded by the platform check above
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
#pragma warning restore CA1416
            }
            catch { /* best-effort */ }
        }
    }

    /// <summary>Normalizes an archive entry name, returning null for absolute paths.</summary>
    private static string? NormalizeEntryPath(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
            return null;

        var normalized = entryName.Replace('\\', '/');
        if (normalized.StartsWith('/') || Path.IsPathRooted(normalized))
            return null;

        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized.Substring(2);

        return normalized;
    }

    /// <summary>Resolves an entry name under the destination directory, rejecting path traversal.</summary>
    private static string? GetSafeDestinationPath(string destinationDir, string relativePath)
    {
        foreach (var segment in relativePath.Split('/'))
        {
            if (segment == "..")
                return null;
        }

        var root = Path.GetFullPath(destinationDir);
        var combined = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        return combined;
    }
}
