using Serilog;
using System.Diagnostics;

public static class NetworkMonitor
{
    private const int DOWNLOAD_THRESHOLD_BYTES_PER_SEC = 1_310_720; // ~10 Mbps

    private static readonly PerformanceCounterCategory _category = new PerformanceCounterCategory("Process");

    private static readonly List<ProcessMonitor> _monitors = new List<ProcessMonitor>
    {
        new ProcessMonitor("steam"), 
        new ProcessMonitor("gamingservicesnet"),
    };

    /// <summary>
    /// Main check method that calls GetInstanceNames() once per cycle.
    /// </summary>
    public static bool CheckAllDownloads()
    {
        string[] allInstanceNames;

        try
        {
            allInstanceNames = _category.GetInstanceNames();
        }
        catch (Exception ex)
        {
            Log.Error($"[CRITICAL ERROR] Cannot access Process performance counters: {ex.Message}");
            return false;
        }

        bool isAnyActive = false;

        foreach (var monitor in _monitors)
        {
            float rate = monitor.GetTotalRateBytesPerSec(allInstanceNames);
            bool isActive = rate > DOWNLOAD_THRESHOLD_BYTES_PER_SEC;
            float rateMB = rate / (1024f * 1024f);

            Log.Information($"[DL Monitor] {monitor.BaseName}: {(isActive ? "Active" : "Inactive")}. Rate: {rateMB:F2} MB/s.");

            if (isActive)
            {
                isAnyActive = true;
            }
        }

        return isAnyActive;
    }
}