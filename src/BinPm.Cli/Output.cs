namespace BinPm.Cli;

/// <summary>Console output helpers with optional color and simple table rendering.</summary>
public static class Output
{
    private static readonly object Gate = new();

    public static bool UseColors { get; private set; } = true;

    public static bool IsVerbose { get; set; }

    public static object SyncRoot => Gate;

    public static void Initialize(bool noColor)
    {
        UseColors = !noColor &&
                    !Console.IsOutputRedirected &&
                    Environment.GetEnvironmentVariable("NO_COLOR") is null;
    }

    public static void Write(string message, ConsoleColor? color = null, bool stderr = false)
    {
        var writer = stderr ? Console.Error : Console.Out;

        if (color is null || !UseColors)
        {
            lock (Gate)
            {
                writer.WriteLine(message);
            }
            return;
        }

        lock (Gate)
        {
            var previous = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = color.Value;
                writer.WriteLine(message);
            }
            finally
            {
                Console.ForegroundColor = previous;
            }
        }
    }

    public static void Info(string message) => Write(message);

    public static void Success(string message) => Write("✓ " + message, ConsoleColor.Green);

    public static void Warning(string message) => Write("! " + message, ConsoleColor.Yellow);

    public static void Error(string message) => Write("error: " + message, ConsoleColor.Red, stderr: true);

    public static void Verbose(string message)
    {
        if (IsVerbose)
            Write(message, ConsoleColor.DarkGray);
    }

    public static void Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0)
        {
            Write("(none)", ConsoleColor.DarkGray);
            return;
        }

        var columnCount = headers.Count;
        var widths = new int[columnCount];

        for (var i = 0; i < columnCount; i++)
            widths[i] = headers[i].Length;

        foreach (var row in rows)
        {
            for (var i = 0; i < columnCount && i < row.Count; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);
        }

        Write(RenderRow(headers, widths), ConsoleColor.Cyan);

        foreach (var row in rows)
        {
            var cells = new string[columnCount];
            for (var i = 0; i < columnCount; i++)
                cells[i] = i < row.Count ? row[i] : "";
            Write(RenderRow(cells, widths));
        }
    }

    private static string RenderRow(IReadOnlyList<string> cells, int[] widths)
    {
        var parts = new string[cells.Count];
        for (var i = 0; i < cells.Count; i++)
            parts[i] = cells[i].PadRight(widths[i]);
        return string.Join("  ", parts).TrimEnd();
    }
}
