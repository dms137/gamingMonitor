namespace GamingMonitor.Infrastructure;

using Serilog;

public static class LoggingConfig
{
    /// <summary>
    /// Set from the --debug-logs command line flag before configuring.
    /// </summary>
    public static bool DebugLogs { get; set; }

    public static void ConfigureLogger()
    {
        var fileLevel = DebugLogs
            ? Serilog.Events.LogEventLevel.Debug
            : Serilog.Events.LogEventLevel.Information;

        string logPath = ResolveLogPath();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            //.WriteTo.Console(
            //    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: fileLevel,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information($"Serilog logger configured (file level: {fileLevel}, path: {logPath}).");
        Log.Information($"[Startup] GamingMonitor v{typeof(LoggingConfig).Assembly.GetName().Version} " +
            $"exe='{Environment.ProcessPath}', cwd='{Environment.CurrentDirectory}'.");
    }

    /// <summary>
    /// Absolute log path under the executable, independent of the working
    /// directory. Autostart via the Run key does not set a working
    /// directory, which previously sent logs (or the write attempt) to an
    /// unexpected location. Falls back to %TEMP% if the install folder
    /// is not writable.
    /// </summary>
    private static string ResolveLogPath()
    {
        foreach (string root in new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(Path.GetTempPath(), "GamingMonitor")
        })
        {
            try
            {
                string dir = Path.Combine(root, "logs");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "monitor_log.txt");
            }
            catch { }
        }

        return Path.Combine(Path.GetTempPath(), "GamingMonitor", "logs", "monitor_log.txt");
    }
}