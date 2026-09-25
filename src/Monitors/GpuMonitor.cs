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

    public AppState ActiveState => AppState.Gaming;

    /// <summary>
    /// Last measured total GPU utilization. Updated on every check cycle.
    /// </summary>
    public float LastUtilization { get; private set; }

    public bool CheckActive() => IsGpuActive();

    public string DescribeActive() => $"GPU {LastUtilization:F0}%";

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
        foreach (var pair in _counters)
        {
            try
            {
                total += pair.Value.NextValue();
            }
            catch { }
        }

        LastUtilization = total;
        return total;
    }

    /// <summary>
    /// Checks if total GPU utilization exceeds the threshold.
    /// </summary>
    public bool IsGpuActive(float? thresholdPercent = null)
    {
        float threshold = thresholdPercent ?? AppSettings.GpuThresholdPercent;
        float total = GetTotalUtilization();
        bool isActive = total > threshold;

        Log.Information($"[GPU Monitor]: {(isActive ? "Active" : "Inactive")}. Utilization: {total:F1}%.");

        return isActive;
    }
}
