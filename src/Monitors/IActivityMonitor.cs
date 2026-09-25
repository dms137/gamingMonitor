namespace GamingMonitor.Monitors;

/// <summary>
/// A single activity source (gamepad, GPU, downloads) feeding the power state machine.
/// </summary>
public interface IActivityMonitor
{
    AppState ActiveState { get; }
    bool CheckActive();
    string DescribeActive();
}
