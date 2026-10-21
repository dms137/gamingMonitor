namespace GamingMonitor.Monitors;

/// <summary>
/// Wraps a monitor with inactivity hysteresis, so brief pauses do not
/// immediately drop the system back to idle.
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
        InactivityThresholdCycles = Math.Max(1, inactivityThresholdCycles);
        // Start inactive, so nothing is active before the first real check.
        InactivityCounter = InactivityThresholdCycles + 1;
    }

    public void SetThreshold(int inactivityThresholdCycles)
    {
        bool wasActive = IsTrulyActive;
        InactivityThresholdCycles = Math.Max(1, inactivityThresholdCycles);

        if (!wasActive)
        {
            // Changing the threshold must never activate a source: only a
            // real activity check may do that. Without this, growing the
            // threshold (e.g. the loaded value overriding the startup
            // default) could leave the counter inside the new range and
            // report a phantom active state with no real activity.
            InactivityCounter = InactivityThresholdCycles + 1;
        }
        else if (InactivityCounter > InactivityThresholdCycles)
        {
            InactivityCounter = InactivityThresholdCycles + 1;
        }
    }

    public void Update(bool activeThisCycle)
    {
        if (activeThisCycle)
        {
            InactivityCounter = 0;
            return;
        }

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
