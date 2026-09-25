namespace GamingMonitor.Monitors;

using System.Diagnostics;

public class ProcessMonitor
{
    private Dictionary<string, PerformanceCounter> _counters = new Dictionary<string, PerformanceCounter>();
    private const string COUNTER_NAME = "IO Data Bytes/sec";
    private const int SAMPLE_DELAY_MS = 1000;

    private readonly string _baseName;

    public string BaseName => _baseName;

    public ProcessMonitor(string baseName)
    {
        _baseName = baseName;
    }

    /// <summary>
    /// Updates counter cache using the provided instance list and reads total rate.
    /// </summary>
    /// <param name="allInstanceNames">Full list of "Process" instances.</param>
    public float GetTotalRateBytesPerSec(string[] allInstanceNames)
    {
        // 1. Clean up non-existent counters
        var expiredKeys = _counters.Keys
            .Where(k => !allInstanceNames.Contains(k))
            .ToList();

        foreach (var key in expiredKeys)
        {
            try { _counters[key].Dispose(); }
            catch { }
            _counters.Remove(key);
        }

        var targetInstanceNames = allInstanceNames
            .Where(name => name.Equals(_baseName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 2. Initialize new counters
        foreach (string instanceName in targetInstanceNames)
        {
            bool matches = instanceName.Equals(_baseName, StringComparison.OrdinalIgnoreCase);

            if (!matches) continue;

            if (!_counters.ContainsKey(instanceName))
            {
                try
                {
                    var newCounter = new PerformanceCounter("Process", COUNTER_NAME, instanceName, true);
                    _counters.Add(instanceName, newCounter);
                    newCounter.NextValue();
                }
                catch { continue; }
            }
        }

        // 3. Read values
        float totalRate = 0;
        foreach (var pair in _counters)
        {
            try
            {
                Thread.Sleep(SAMPLE_DELAY_MS);
                totalRate += pair.Value.NextValue();
            }
            catch { }
        }

        return totalRate;
    }
}