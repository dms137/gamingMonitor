namespace GamingMonitor;

public enum AppState
{
    Idle,
    Gaming,
    Downloading
}

public record StateSnapshot(AppState State, string Reason, DateTime Since, float GpuUtilization);