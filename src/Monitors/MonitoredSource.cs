namespace GamingMonitor.Monitors;

/// <summary>
/// Wraps a monitor with inactivity hysteresis, so brief pauses do not
/// immediately drop the system back to idle, and (optionally) an entry
/// hysteresis, so a brief burst does not immediately activate it.
/// </summary>
public sealed class MonitoredSource
{
    public IActivityMonitor Monitor { get; }
    public int InactivityThresholdCycles { get; private set; }
    public int EntryThresholdCycles { get; }
    public int InactivityCounter { get; private set; }

    private int _activeStreak;

    public bool IsTrulyActive => InactivityCounter <= InactivityThresholdCycles;

    public MonitoredSource(IActivityMonitor monitor, int inactivityThresholdCycles, int entryThresholdCycles = 1)
    {
        Monitor = monitor;
        InactivityThresholdCycles = Math.Max(1, inactivityThresholdCycles);
        EntryThresholdCycles = Math.Max(1, entryThresholdCycles);
        // Start inactive: activation is gated by the entry streak below.
        InactivityCounter = InactivityThresholdCycles + 1;
    }

    public void SetThreshold(int inactivityThresholdCycles)
    {
        InactivityThresholdCycles = Math.Max(1, inactivityThresholdCycles);
        if (InactivityCounter > InactivityThresholdCycles + 1)
        {
            InactivityCounter = InactivityThresholdCycles + 1;
        }
    }

    public void Update(bool activeThisCycle)
    {
        if (activeThisCycle)
        {
            _activeStreak++;
            if (_activeStreak >= EntryThresholdCycles)
            {
                InactivityCounter = 0;
            }

            return;
        }

        _activeStreak = 0;
        if (Monitor.IsDefinitivelyInactive())
        {
            InactivityCounter = InactivityThresholdCycles + 1;
        }
        else if (InactivityCounter <= InactivityThresholdCycles)
        {
            InactivityCounter++;
        }
    }
}
