namespace H3CSwitchPortMonitor;

public static class StartupDiagnostics
{
    private static int _fatalRecorded;

    public static void RecordFatal(string message, Exception exception)
    {
        try
        {
            RecordFatalAsync(message, exception, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            Console.Error.WriteLine(message);
            Console.Error.WriteLine(exception);
        }
    }

    public static async Task RecordFatalAsync(string message, Exception exception, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _fatalRecorded, 1) == 1)
        {
            return;
        }

        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "startup-error.log");
        var text = $"""
                   [{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}
                   {exception}

                   """;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.AppendAllTextAsync(logPath, text, cancellationToken);
        }
        catch
        {
            var fallbackPath = Path.Combine(Path.GetTempPath(), "H3CSwitchPortMonitor-startup-error.log");
            await File.AppendAllTextAsync(fallbackPath, text, cancellationToken);
            logPath = fallbackPath;
        }

        Console.Error.WriteLine(message);
        Console.Error.WriteLine(exception.Message);
        Console.Error.WriteLine($"错误日志：{logPath}");

        if (Environment.UserInteractive)
        {
            Console.Error.WriteLine();
            Console.Error.Write("按回车退出...");
            Console.ReadLine();
        }
    }
}
