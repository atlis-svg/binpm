using System.Formats.Tar;
using System.IO.Compression;
using BinPm.Core.Install;

namespace BinPm.Core.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "binpm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}

public class ArchiveExtractionTests
{
    [Fact]
    public async Task Extracts_a_zip_archive()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "tool.zip");
        var destination = Path.Combine(temp.Path, "out");

        using (var stream = File.Create(archivePath))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("bin/tool");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("#!/bin/sh\necho hi\n");
        }

        await ArchiveExtraction.ExtractAsync(archivePath, destination);

        Assert.Equal("#!/bin/sh\necho hi\n", File.ReadAllText(Path.Combine(destination, "bin", "tool")));
    }

    [Fact]
    public async Task Extracts_a_tar_gz_archive_and_preserves_the_executable_bit()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "tool.tar.gz");
        var destination = Path.Combine(temp.Path, "out");

        await using (var file = File.Create(archivePath))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            using var writer = new TarWriter(gzip, TarEntryFormat.Pax);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "tool")
            {
                DataStream = new MemoryStream("binary"u8.ToArray()),
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                       UnixFileMode.OtherRead | UnixFileMode.OtherExecute,
            };
            await writer.WriteEntryAsync(entry);
        }

        var result = await ArchiveExtraction.ExtractAsync(archivePath, destination);

        Assert.False(result.SingleFile);
        Assert.Equal("binary", File.ReadAllText(Path.Combine(destination, "tool")));

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(Path.Combine(destination, "tool"));
            Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public async Task Extracts_a_gzipped_single_file()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "tool.gz");
        var destination = Path.Combine(temp.Path, "out");

        await using (var file = File.Create(archivePath))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            await gzip.WriteAsync("plain"u8.ToArray());
        }

        var result = await ArchiveExtraction.ExtractAsync(archivePath, destination);

        Assert.True(result.SingleFile);
        Assert.Equal("tool", result.FileName);
        Assert.Equal("plain", File.ReadAllText(Path.Combine(destination, "tool")));
    }

    [Fact]
    public async Task Treats_an_unrecognised_file_as_a_raw_executable()
    {
        using var temp = new TempDirectory();
        var filePath = Path.Combine(temp.Path, "mytool");
        var destination = Path.Combine(temp.Path, "out");

        await File.WriteAllTextAsync(filePath, "raw binary");

        var result = await ArchiveExtraction.ExtractAsync(filePath, destination);

        Assert.True(result.SingleFile);
        Assert.Equal("mytool", result.FileName);
        Assert.Equal("raw binary", File.ReadAllText(Path.Combine(destination, "mytool")));
    }

    [Fact]
    public async Task Rejects_zip_entries_that_escape_the_destination()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "evil.zip");
        var destination = Path.Combine(temp.Path, "out");

        using (var stream = File.Create(archivePath))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../escaped.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("pwned");
        }

        await ArchiveExtraction.ExtractAsync(archivePath, destination);

        Assert.False(File.Exists(Path.Combine(temp.Path, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "..", "escaped.txt")));
    }

    [Fact]
    public async Task Rejects_tar_entries_that_escape_the_destination()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "evil.tar.gz");
        var destination = Path.Combine(temp.Path, "out");

        await using (var file = File.Create(archivePath))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            using var writer = new TarWriter(gzip, TarEntryFormat.Ustar);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "../escaped.txt")
            {
                DataStream = new MemoryStream("pwned"u8.ToArray()),
            };
            await writer.WriteEntryAsync(entry);
        }

        await ArchiveExtraction.ExtractAsync(archivePath, destination);

        Assert.False(File.Exists(Path.Combine(temp.Path, "escaped.txt")));
    }

    [Fact]
    public async Task Reports_a_clear_error_for_unsupported_compression()
    {
        using var temp = new TempDirectory();
        var archivePath = Path.Combine(temp.Path, "tool.xz");

        // XZ magic bytes.
        await File.WriteAllBytesAsync(archivePath, new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00, 0x00 });

        var exception = await Assert.ThrowsAsync<BinPmException>(() =>
            ArchiveExtraction.ExtractAsync(archivePath, Path.Combine(temp.Path, "out")));

        Assert.Contains("xz", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
