namespace GamingMonitor.Monitors;

/// <summary>
/// Wraps a monitor with an inactivity hysteresis counter, so brief
/// pauses do not immediately drop the system back to idle.
/// </summary>
public sealed class MonitoredSource
{
    public IActivityMonitor Monitor { get; }
    public int InactivityThresholdCycles { get; private set; }
    public int InactivityCounter { get; private set; }
    public bool IsTrulyActive => InactivityCounter <= InactivityThresholdCycles;

    public MonitoredSource(IActivityMonitor monitor, int inactivityThresholdCycles)
    {
        Monitor = monitor;
        InactivityThresholdCycles = inactivityThresholdCycles;
        InactivityCounter = inactivityThresholdCycles;
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
            InactivityCounter = 0;
        }
        else if (InactivityCounter <= InactivityThresholdCycles)
        {
            InactivityCounter++;
        }
    }
}
