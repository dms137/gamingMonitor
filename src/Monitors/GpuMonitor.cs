namespace GamingMonitor.Monitors;

using GamingMonitor.Infrastructure;
using Serilog;
using System.Diagnostics;

public class GpuMonitor : IActivityMonitor
{
    private const string CATEGORY_NAME = "GPU Engine";
    private const string COUNTER_NAME = "Utilization Percentage";
    private const int SAMPLE_DELAY_MS = 1000;

    private readonly PerformanceCounterCategory _category = new PerformanceCounterCategory(CATEGORY_NAME);
    private readonly Dictionary<string, PerformanceCounter> _counters = new Dictionary<string, PerformanceCounter>();
    private readonly Dictionary<int, string> _nameCache = new Dictionary<int, string>();

    public AppState ActiveState => AppState.Gaming;

    /// <summary>
    /// Last measured total GPU utilization. Updated on every check cycle.
    /// </summary>
    public float LastUtilization { get; private set; }

    /// <summary>
    /// PID of the process with the highest GPU usage, if identifiable.
    /// </summary>
    public int LastTopPid { get; private set; }

    private int _lastActiveTopPid;

    /// <summary>
    /// GPU share of the top process, in percent.
    /// </summary>
    public float LastTopPercent { get; private set; }

    public bool CheckActive() => IsGpuActive();

    public bool IsDefinitivelyInactive()
    {
        // The top consumer is gone and nothing replaced its load:
        // no point waiting out the grace period. PID reuse falls back
        // to normal hysteresis, which is the safe direction.
        if (_lastActiveTopPid <= 0)
        {
            return false;
        }

        try
        {
            Process.GetProcessById(_lastActiveTopPid);
            return false;
        }
        catch (ArgumentException)
        {
            _nameCache.Remove(_lastActiveTopPid);
            return true;
        }
    }

    public string DescribeActive()
    {
        // No percentages here: this text is frozen at transition time,
        // the live load is shown separately.
        return ResolveDisplayName(LastTopPid) ?? "GPU";
    }

    /// <summary>
    /// Task Manager style name. Resolution order: file description
    /// ("Cyberpunk 2077" for Cyberpunk2077.exe), main window title
    /// (covers packaged/WinGDK builds whose exe has no version info,
    /// e.g. "Solarpunk-WinGDK-Shipping" -> "Solarpunk"), then the raw
    /// exe name as the last resort.
    /// </summary>
    private string? ResolveDisplayName(int pid)
    {
        if (pid <= 0)
        {
            return null;
        }

        if (_nameCache.TryGetValue(pid, out string? cached))
        {
            return cached;
        }

        string name;
        string? pretty = null;
        try
        {
            using var process = Process.GetProcessById(pid);

            // Reading ProcessName is safe even for protected game processes.
            name = process.ProcessName;

            try
            {
                pretty = process.MainModule?.FileVersionInfo?.FileDescription;
                if (string.IsNullOrWhiteSpace(pretty))
                {
                    pretty = process.MainWindowTitle;
                }
            }
            catch
            {
                // MainModule throws for protected/elevated processes; ignore.
            }
        }
        catch
        {
            // Process exited or is inaccessible.
            return null;
        }

        if (!string.IsNullOrWhiteSpace(pretty))
        {
            name = pretty.Trim();
        }

        if (_nameCache.Count > 200)
        {
            _nameCache.Clear();
        }

        _nameCache[pid] = name;
        return name;
    }

    /// <summary>
    /// Returns total GPU utilization across all engines.
    /// </summary>
    public float GetTotalUtilization()
    {
        string[] instanceNames;

        try
        {
            instanceNames = _category.GetInstanceNames();
        }
        catch (Exception ex)
        {
            Log.Error($"[CRITICAL ERROR] Cannot access GPU performance counters: {ex.Message}");
            return 0;
        }

        if (instanceNames.Length == 0)
        {
            LastUtilization = 0;
            return 0;
        }

        // 1. Clean up non-existent counters
        var expiredKeys = _counters.Keys
            .Where(k => !instanceNames.Contains(k))
            .ToList();

        foreach (var key in expiredKeys)
        {
            try { _counters[key].Dispose(); }
            catch { }
            _counters.Remove(key);
        }

        // 2. Initialize new counters
        foreach (string instanceName in instanceNames)
        {
            if (_counters.ContainsKey(instanceName))
            {
                continue;
            }

            try
            {
                var newCounter = new PerformanceCounter(CATEGORY_NAME, COUNTER_NAME, instanceName, true);
                _counters.Add(instanceName, newCounter);
                newCounter.NextValue();
            }
            catch { }
        }

        if (_counters.Count == 0)
        {
            LastUtilization = 0;
            return 0;
        }

        // 3. Wait for valid sample, then sum all engines
        Thread.Sleep(SAMPLE_DELAY_MS);

        float total = 0;
        var perPid = new Dictionary<int, float>();
        foreach (var pair in _counters)
        {
            try
            {
                float value = pair.Value.NextValue();
                total += value;
                if (TryParsePid(pair.Key, out int pid))
                {
                    perPid[pid] = perPid.TryGetValue(pid, out float current) ? current + value : value;
                }
            }
            catch { }
        }

        LastUtilization = total;
        if (perPid.Count > 0)
        {
            var top = perPid.MaxBy(kv => kv.Value);
            LastTopPid = top.Key;
            LastTopPercent = top.Value;
        }
        else
        {
            LastTopPid = 0;
            LastTopPercent = 0;
        }

        return total;
    }

    /// <summary>
    /// Instance names look like pid_1234_luid_..._phys_0_eng_0_engtype_3d.
    /// </summary>
    private static bool TryParsePid(string instanceName, out int pid)
    {
        pid = 0;
        string[] parts = instanceName.Split('_');
        return parts.Length > 1 &&
            string.Equals(parts[0], "pid", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[1], out pid);
    }

    /// <summary>
    /// Checks if total GPU utilization exceeds the threshold.
    /// </summary>
    public bool IsGpuActive(float? thresholdPercent = null)
    {
        float threshold = thresholdPercent ?? AppSettings.GpuThresholdPercent;
        float total = GetTotalUtilization();
        bool isActive = total > threshold;

        if (isActive)
        {
            _lastActiveTopPid = LastTopPid;
        }

        Log.Debug($"[GPU Monitor]: {(isActive ? "Active" : "Inactive")}. Utilization: {total:F1}%.");

        return isActive;
    }
}
