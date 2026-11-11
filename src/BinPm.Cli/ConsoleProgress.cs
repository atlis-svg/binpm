using BinPm.Core.Install;

namespace BinPm.Cli;

/// <summary>Renders install progress to the console.</summary>
public sealed class ConsoleProgress : IInstallReporter
{
    private readonly bool _verbose;
    private int _lastPercent = -1;

    public ConsoleProgress(bool verbose)
    {
        _verbose = verbose;
    }

    public void Report(string message)
    {
        if (_verbose)
            Output.Verbose(message);
        else
            Output.Info(message);
    }

    public void ReportWarning(string message) => Output.Warning(message);

    public void ReportProgress(string operation, long bytesReceived, long? totalBytes)
    {
        if (totalBytes is null or <= 0)
            return;

        var percent = (int)Math.Clamp(bytesReceived * 100 / totalBytes.Value, 0, 100);
        if (percent == _lastPercent)
            return;

        _lastPercent = percent;

        if (Console.IsOutputRedirected)
        {
            if (percent >= 100)
                Output.Info($"  {operation}: {Util.FormatBytes(bytesReceived)}");
            return;
        }

        const int barWidth = 28;
        var filled = percent * barWidth / 100;
        var bar = new string('=', filled) + new string(' ', barWidth - filled);

        lock (Output.SyncRoot)
        {
            Console.Write($"\r  [{bar}] {percent,3}% {Util.FormatBytes(bytesReceived)} / {Util.FormatBytes(totalBytes.Value)}");
            if (percent >= 100)
                Console.WriteLine();
        }
    }
}
