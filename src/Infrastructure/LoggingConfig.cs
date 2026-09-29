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

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            //.WriteTo.Console(
            //    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/monitor_log.txt",
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: fileLevel,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information($"Serilog logger configured (file level: {fileLevel}).");
    }
}