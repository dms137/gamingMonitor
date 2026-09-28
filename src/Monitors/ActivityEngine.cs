namespace GamingMonitor.Monitors;

using GamingMonitor.Infrastructure;
using Serilog;
using System.Runtime.InteropServices;

/// <summary>
/// Power state machine: polls activity monitors on a worker thread,
/// resolves the application state and applies power requests.
/// Raises StateChanged on the worker thread.
/// </summary>
public sealed class ActivityEngine : IDisposable
{
    private readonly DualSenseMonitor _dualSenseMonitor = new DualSenseMonitor();
    private readonly GpuMonitor _gpuMonitor = new GpuMonitor();
    private readonly NetworkMonitor _networkMonitor = new NetworkMonitor();
    private readonly MonitoredSource _gamepadSource;
    private readonly MonitoredSource _gpuSource;
    private readonly MonitoredSource _downloadSource;
    private readonly List<MonitoredSource> _sources;
    private Thread? _thread;
    private volatile bool _stop;
    private DateTime _lastGamepadInput = DateTime.Now;

    public event Action? StateChanged;

    public AppState CurrentState { get; private set; } = AppState.Idle;
    public string Reason { get; private set; } = "No activity";
    public DateTime ChangedAt { get; private set; } = DateTime.Now;
    public bool IsDisplayControlled { get; private set; }
    public bool IsSleepControlled { get; private set; }
    public float GpuUtilization => _gpuMonitor.LastUtilization;

    public ActivityEngine()
    {
        // Priority order: gamepad and GPU mean Gaming, downloads mean Downloading.
        _gamepadSource = new MonitoredSource(_dualSenseMonitor, AppSettings.GamepadInactivityCycles);
        _gpuSource = new MonitoredSource(_gpuMonitor, AppSettings.GpuInactivityCycles);
        _downloadSource = new MonitoredSource(_networkMonitor, AppSettings.DownloadInactivityCycles);
        _sources = new List<MonitoredSource> { _gamepadSource, _gpuSource, _downloadSource };
    }

    public void Start()
    {
        _thread = new Thread(MainCheckLoop) { IsBackground = true };
        _thread.Start();
    }

    public void Dispose()
    {
        _stop = true;
        foreach (var monitor in _sources.Select(s => s.Monitor).OfType<IDisposable>())
        {
            try { monitor.Dispose(); } catch { }
        }
    }

    private DateTime LastAnyInput()
    {
        DateTime keyboardMouse = GetLastKeyboardMouseInput();
        return keyboardMouse > _lastGamepadInput ? keyboardMouse : _lastGamepadInput;
    }

    /// <summary>
    /// System-wide last keyboard/mouse input. Fail-open: on error
    /// reports now so a broken API can never force sleep.
    /// </summary>
    private static DateTime GetLastKeyboardMouseInput()
    {
        try
        {
            var info = new NativeMethods.LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
            };

            if (!NativeMethods.GetLastInputInfo(ref info))
            {
                return DateTime.Now;
            }

            int msAgo = unchecked(Environment.TickCount - (int)info.dwTime);
            return DateTime.Now.AddMilliseconds(-Math.Max(0, msAgo));
        }
        catch
        {
            return DateTime.Now;
        }
    }

    private void MainCheckLoop()
    {
        // The first cycle always reports, so startup shows the same
        // state notification as any other transition.
        bool firstCycle = true;
        while (!_stop)
        {
            // Pick up hand edits to settings.json without a restart.
            AppSettings.Reload();
            _gamepadSource.SetThreshold(AppSettings.GamepadInactivityCycles);
            _gpuSource.SetThreshold(AppSettings.GpuInactivityCycles);
            _downloadSource.SetThreshold(AppSettings.DownloadInactivityCycles);

            // Activity checks run concurrently: each one blocks
            // on hardware/counter sampling, so sequential calls add up.
            // The monitors own disjoint state, sharing is safe here.
            Task<(MonitoredSource Source, bool Active)>[] checkTasks = _sources
                .Select(source => Task.Run(() => (source, source.Monitor.CheckActive())))
                .ToArray();
            Task.WaitAll(checkTasks);

            foreach (var (source, active) in checkTasks.Select(t => t.Result))
            {
                source.Update(active);
                if (active && source.Monitor is DualSenseMonitor)
                {
                    _lastGamepadInput = DateTime.Now;
                }
            }

            MonitoredSource? activeSource = _sources.FirstOrDefault(s => s.IsTrulyActive);
            AppState newlyCalculatedState = activeSource?.Monitor.ActiveState ?? AppState.Idle;
            string reason = activeSource?.Monitor.DescribeActive() ?? "No activity";

            // Long absence kills Gaming even if the GPU still renders something.
            // Downloads stay exempt. Disabled when the timeout is 0 (Never).
            if (AppSettings.AfkTimeoutMinutes > 0 &&
                newlyCalculatedState == AppState.Gaming &&
                DateTime.Now - LastAnyInput() > TimeSpan.FromMinutes(AppSettings.AfkTimeoutMinutes))
            {
                newlyCalculatedState = AppState.Idle;
                reason = "AFK timeout";
            }

            if (firstCycle || newlyCalculatedState != CurrentState)
            {
                firstCycle = false;
                CurrentState = newlyCalculatedState;
                Reason = reason;
                ChangedAt = DateTime.Now;

                Log.Information($"[State] New state: {CurrentState} ({Reason}).");

                IsDisplayControlled = CurrentState == AppState.Gaming;
                IsSleepControlled = CurrentState != AppState.Idle;
                PowerManagement.SetDisplayRequired(IsDisplayControlled);
                PowerManagement.SetSystemRequired(IsSleepControlled);

                StateChanged?.Invoke();
            }

            Thread.Sleep(AppSettings.CheckIntervalMs);
        }
    }
}
