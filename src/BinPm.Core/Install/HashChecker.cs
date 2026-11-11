using System.Security.Cryptography;

namespace BinPm.Core.Install;

public static class HashChecker
{
    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Compares a computed hash against an expected value that may be prefixed with <c>sha256:</c>.</summary>
    public static bool Matches(string actual, string expected)
    {
        var normalizedExpected = Normalize(expected);
        return string.Equals(Normalize(actual), normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string hash) =>
        hash.Trim().TrimStart('"').TrimEnd('"').Replace("sha256:", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
}
