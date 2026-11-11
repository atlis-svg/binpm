namespace BinPm.Core.Models;

/// <summary>A short description of a package as returned by registry searches.</summary>
public sealed class PackageSummary
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Registry the package comes from, for display purposes (e.g. <c>github</c>).</summary>
    public string? Registry { get; set; }
}
