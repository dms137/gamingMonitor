namespace GamingMonitor.Monitors;

using GamingMonitor.Infrastructure;
using Serilog;
using System.Diagnostics;

public class NetworkMonitor : IActivityMonitor
{
    private const int DOWNLOAD_THRESHOLD_BYTES_PER_SEC = 1_310_720; // ~10 Mbps

    private readonly PerformanceCounterCategory _category = new PerformanceCounterCategory("Process");

    private readonly Dictionary<string, ProcessMonitor> _monitors =
        new Dictionary<string, ProcessMonitor>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Syncs tracked processes with the configuration, disposing removed ones.
    /// </summary>
    private void SyncMonitors(IEnumerable<string> processNames)
    {
        var wanted = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);

        foreach (string name in wanted)
        {
            _monitors.TryAdd(name, new ProcessMonitor(name));
        }

        foreach (string expired in _monitors.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            try { _monitors[expired].Dispose(); }
            catch { }
            _monitors.Remove(expired);
        }
    }

    public AppState ActiveState => AppState.Downloading;

    public bool CheckActive() => CheckAllDownloads();

    public string DescribeActive() => "Game download";

    /// <summary>
    /// Main check method that calls GetInstanceNames() once per cycle.
    /// </summary>
    public bool CheckAllDownloads()
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

        SyncMonitors(AppSettings.DownloadProcesses);

        bool isAnyActive = false;

        foreach (var monitor in _monitors.Values)
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
