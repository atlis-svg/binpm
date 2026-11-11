namespace BinPm.Cli.Cli;

/// <summary>Describes a command and its handler.</summary>
public sealed class CliCommand
{
    public CliCommand(
        string name,
        string description,
        string usage,
        IReadOnlyList<CliOption> options,
        Func<CommandContext, Task<int>> handler,
        params string[] aliases)
    {
        Name = name;
        Description = description;
        Usage = usage;
        Options = options;
        Handler = handler;
        Aliases = aliases ?? Array.Empty<string>();
    }

    public string Name { get; }
    public string[] Aliases { get; }
    public string Description { get; }
    public string Usage { get; }
    public IReadOnlyList<CliOption> Options { get; }
    public Func<CommandContext, Task<int>> Handler { get; }
}
