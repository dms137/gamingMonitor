namespace GamingMonitor.Monitors;

using GamingMonitor.Infrastructure;
using Serilog;
using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// Power state machine: polls activity monitors on a worker thread,
/// resolves the application state and applies power requests.
/// Raises StateChanged on the worker thread.
/// </summary>
public sealed class ActivityEngine : IDisposable
{
    private readonly DualSenseMonitor _dualSenseMonitor = new DualSenseMonitor();
    private readonly XInputMonitor _xinputMonitor = new XInputMonitor();
    private readonly GamepadMonitor _rawGamepadMonitor = new GamepadMonitor();
    private readonly GpuMonitor _gpuMonitor = new GpuMonitor();
    private readonly NetworkMonitor _networkMonitor = new NetworkMonitor();
    private readonly MonitoredSource _gamepadSource;
    private readonly MonitoredSource _xinputSource;
    private readonly MonitoredSource _rawSource;
    private readonly MonitoredSource _gpuSource;
    private readonly MonitoredSource _downloadSource;
    private readonly List<MonitoredSource> _sources;
    private Thread? _thread;
    private volatile bool _stop;
    private DateTime _lastGamepadInput = DateTime.Now;
    private DateTime? _graceEndsAt;
    private DateTime? _finalEndsAt;
    private uint _graceBaselineInputTick;
    private DateTime _graceBaselineGamepadInput;

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
        _xinputSource = new MonitoredSource(_xinputMonitor, AppSettings.GamepadInactivityCycles);
        _rawSource = new MonitoredSource(_rawGamepadMonitor, AppSettings.GamepadInactivityCycles);
        _gpuSource = new MonitoredSource(_gpuMonitor, AppSettings.GpuInactivityCycles);
        _downloadSource = new MonitoredSource(_networkMonitor, AppSettings.DownloadInactivityCycles);
        _sources = new List<MonitoredSource> { _gamepadSource, _xinputSource, _rawSource, _gpuSource, _downloadSource };
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

    /// <summary>
    /// Aborts a pending shutdown countdown, if any.
    /// Safe to call from any thread.
    /// </summary>
    public void CancelShutdown()
    {
        bool wasArmed = _graceEndsAt != null || _finalEndsAt != null;
        _graceEndsAt = null;
        _finalEndsAt = null;
        if (!wasArmed)
        {
            return;
        }

        NotificationHelper.ShowShutdownCanceledNotification();
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/a")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch { }

        Log.Information("[Shutdown] Countdown canceled by user.");
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

    /// <summary>
    /// Raw GetLastInputInfo tick. Unlike the DateTime conversion it is stable
    /// across consecutive reads and only changes when new input arrives.
    /// </summary>
    private static bool TryGetLastInputTick(out uint tick)
    {
        try
        {
            var info = new NativeMethods.LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
            };

            if (NativeMethods.GetLastInputInfo(ref info))
            {
                tick = info.dwTime;
                return true;
            }
        }
        catch { }

        tick = 0;
        return false;
    }

    /// <summary>
    /// True when keyboard/mouse or gamepad input arrived after the grace
    /// period started. Uses the raw input tick so clock-resolution jitter
    /// cannot fake an input event (which used to abort the countdown
    /// immediately after it was armed).
    /// </summary>
    private bool HasInputSinceGraceBaseline()
    {
        if (_lastGamepadInput > _graceBaselineGamepadInput)
        {
            return true;
        }

        // Fail-safe: if the API cannot be read, never abort on that alone.
        return _graceBaselineInputTick != 0 &&
            TryGetLastInputTick(out uint tick) &&
            tick != _graceBaselineInputTick;
    }

    private void MainCheckLoop()
    {
        // The first cycle always reports, so startup shows the same
        // state notification as any other transition.
        bool firstCycle = true;
        int cycle = 0;
        while (!_stop)
        {
            if (++cycle % 75 == 0)
            {
                Log.Debug($"[Perf] Managed: {GC.GetTotalMemory(false) / 1048576} MB, " +
                    $"working set: {Process.GetCurrentProcess().WorkingSet64 / 1048576} MB.");
            }

            // Pick up hand edits to settings.json without a restart.
            AppSettings.Reload();
            _gamepadSource.SetThreshold(AppSettings.GamepadInactivityCycles);
            _xinputSource.SetThreshold(AppSettings.GamepadInactivityCycles);
            _rawSource.SetThreshold(AppSettings.GamepadInactivityCycles);
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
                if (active && source.Monitor is DualSenseMonitor or XInputMonitor or GamepadMonitor)
                {
                    _lastGamepadInput = DateTime.Now;
                }
            }

            MonitoredSource? activeSource = _sources.FirstOrDefault(s => s.IsTrulyActive);
            AppState previousState = CurrentState;
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
            else if (reason != Reason)
            {
                // The display name can settle after startup (e.g. a game sets
                // its window title a little late). Refresh it silently, so the
                // flyout picks it up without firing another notification.
                Reason = reason;
            }

            UpdateShutdownTracking(previousState, CurrentState);

            Thread.Sleep(AppSettings.CheckIntervalMs);
        }
    }

    /// <summary>
    /// Arms on the Downloading-to-Idle transition, aborts on any input
    /// or state change, fires shutdown after the grace period plus
    /// a one-minute force toast countdown.
    /// </summary>
    private void UpdateShutdownTracking(AppState previousState, AppState currentState)
    {
        int graceMinutes = AppSettings.ShutdownGraceMinutes;
        if (graceMinutes <= 0)
        {
            DisarmShutdown();
            return;
        }

        if (previousState == AppState.Downloading && currentState == AppState.Idle)
        {
            _graceEndsAt = DateTime.Now.AddMinutes(graceMinutes);
            _finalEndsAt = null;
            TryGetLastInputTick(out _graceBaselineInputTick);
            _graceBaselineGamepadInput = _lastGamepadInput;
            Log.Information($"[Shutdown] Downloads finished, grace period started ({graceMinutes} min).");
        }

        if (_finalEndsAt != null)
        {
            // During the final countdown the toast is the contract: the user
            // cancels it deliberately via the button, plain input no longer
            // aborts. A new activity source still does, so we never shut down
            // in the middle of a fresh download or game.
            if (currentState != AppState.Idle)
            {
                DisarmShutdown("aborted", notifyUser: true);
                return;
            }

            int remaining = (int)Math.Ceiling((_finalEndsAt.Value - DateTime.Now).TotalSeconds);
            if (remaining <= 0)
            {
                DisarmShutdown(null);
                Log.Warning("[Shutdown] Countdown over, shutting down now.");
                try
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/s /t 5 /c \"GamingMonitor: shutting down after downloads.\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch (Exception ex)
                {
                    Log.Error($"[Shutdown ERROR] Failed to start shutdown: {ex.Message}");
                }

                return;
            }

            NotificationHelper.ShowShutdownCountdownNotification(remaining);
            return;
        }

        if (_graceEndsAt != null)
        {
            if (currentState != AppState.Idle || HasInputSinceGraceBaseline())
            {
                DisarmShutdown("aborted");
                return;
            }

            if (DateTime.Now >= _graceEndsAt.Value)
            {
                _graceEndsAt = null;
                _finalEndsAt = DateTime.Now.AddMinutes(1);
                NotificationHelper.ShowShutdownCountdownNotification(60);
                Log.Warning("[Shutdown] Grace over, final countdown started.");
            }
        }
    }

    private void DisarmShutdown(string? reason = null, bool notifyUser = false)
    {
        bool wasArmed = _graceEndsAt != null || _finalEndsAt != null;
        _graceEndsAt = null;
        _finalEndsAt = null;
        if (wasArmed)
        {
            if (notifyUser)
            {
                // Morph the countdown toast into a confirmation instead of
                // letting it silently disappear.
                NotificationHelper.ShowShutdownCanceledNotification();
            }
            else
            {
                NotificationHelper.HideShutdownNotification();
            }

            if (reason != null)
            {
                Log.Information($"[Shutdown] Countdown {reason}.");
            }
        }
    }
}
