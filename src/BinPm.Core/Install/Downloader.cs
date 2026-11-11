namespace BinPm.Core.Install;

public interface IInstallReporter
{
    void Report(string message);

    void ReportWarning(string message);

    void ReportProgress(string operation, long bytesReceived, long? totalBytes);
}

/// <summary>Downloads files to disk with progress reporting and optional checksum verification.</summary>
public static class Downloader
{
    public static async Task DownloadAsync(
        HttpClient http,
        string url,
        string destinationPath,
        string? expectedSha256,
        IInstallReporter? reporter,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new BinPmException($"Download failed: '{url}' was not found (404).");

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var fileName = Path.GetFileName(destinationPath);

        long received;
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(destinationPath))
        {
            var buffer = new byte[81920];
            received = 0;
            int read;

            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
                reporter?.ReportProgress(fileName, received, totalBytes);
            }

            await output.FlushAsync(ct);
        }

        if (totalBytes is not null && received != totalBytes)
        {
            File.Delete(destinationPath);
            throw new BinPmException(
                $"Download of '{fileName}' was truncated ({received} of {totalBytes} bytes).");
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actual = await HashChecker.ComputeSha256Async(destinationPath, ct);
            if (!HashChecker.Matches(actual, expectedSha256))
            {
                File.Delete(destinationPath);
                throw new BinPmException(
                    $"Checksum mismatch for '{fileName}'. Expected {HashChecker.Normalize(expectedSha256)}, got {actual}.");
            }
        }
    }
}
