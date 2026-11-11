namespace BinPm.Cli.Cli;

/// <summary>Describes a command-line option.</summary>
public sealed class CliOption
{
    public CliOption(string name, bool takesValue, string description, params string[] aliases)
    {
        Name = name;
        TakesValue = takesValue;
        Description = description;
        Aliases = aliases ?? Array.Empty<string>();
    }

    public string Name { get; }

    /// <summary>Single-character aliases, e.g. <c>h</c> for <c>--help</c>.</summary>
    public string[] Aliases { get; }

    public bool TakesValue { get; }

    public string Description { get; }
}
