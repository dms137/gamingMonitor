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
    // A single stray input event (mouse jitter, software injecting input)
    // used to abort the grace period on its own. Someone who is really
    // back keeps producing input, so it must show up in this many cycles.
    private const int GraceAbortInputCycles = 2;

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
    private readonly Dictionary<MonitoredSource, Task<(bool Active, Exception? Error)>> _pendingChecks = new();
    private readonly ManualResetEventSlim _stopEvent = new ManualResetEventSlim(false);
    private Thread? _thread;

    // Timers use the monotonic tick count (ms), so clock changes and
    // DST cannot stretch or cut the AFK timeout or the shutdown countdown.
    private long _lastGamepadInputMs = Environment.TickCount64;

    // Shutdown tracking is touched by the worker and by toast activation
    // (Cancel button) on another thread.
    private readonly object _shutdownLock = new object();
    private long? _graceStartedAtMs;
    private long? _graceEndsAtMs;
    private long? _finalEndsAtMs;
    private uint _graceLastInputTick;
    private int _graceInputCycles;

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

    /// <summary>
    /// Stops the worker, which releases its power requests on the way out,
    /// then disposes hardware handles. Call from the UI thread on exit.
    /// </summary>
    public void Dispose()
    {
        _stopEvent.Set();
        if (_thread != null && !_thread.Join(TimeSpan.FromSeconds(5)))
        {
            // Process exit releases the power requests anyway.
            Log.Warning("[Engine] Worker did not stop in time.");
        }

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
        lock (_shutdownLock)
        {
            bool wasArmed = _graceEndsAtMs != null || _finalEndsAtMs != null;
            ClearShutdownTimers();
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
    }

    private long LastAnyInputMs()
    {
        return Math.Max(GetLastKeyboardMouseInputMs(), _lastGamepadInputMs);
    }

    /// <summary>
    /// System-wide last keyboard/mouse input on the TickCount64 scale.
    /// Fail-open: on error reports now so a broken API can never force sleep.
    /// </summary>
    private static long GetLastKeyboardMouseInputMs()
    {
        long now = Environment.TickCount64;
        if (!TryGetLastInputTick(out uint tick))
        {
            return now;
        }

        // Both ticks are 32-bit and wrap together, unsigned math keeps the age right.
        uint msAgo = unchecked((uint)Environment.TickCount - tick);
        return now - msAgo;
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
    /// Counts the cycles in which new keyboard/mouse input arrived during
    /// the grace period and reports when the user is clearly back.
    /// Uses the raw input tick so clock-resolution jitter cannot fake an
    /// input event. Every hit is logged, so phantom input can be traced.
    /// </summary>
    private bool IsUserBackDuringGrace()
    {
        // Gamepad activity is already covered by the state change (it flips
        // to Gaming), so only keyboard/mouse is checked here. This keeps a
        // drifting stick from silently aborting the countdown.
        // Fail-safe: if the API cannot be read, never abort on that alone.
        if (_graceLastInputTick == 0 ||
            !TryGetLastInputTick(out uint tick) ||
            tick == _graceLastInputTick)
        {
            return false;
        }

        _graceLastInputTick = tick;
        _graceInputCycles++;

        long sinceStartSec = (Environment.TickCount64 - (_graceStartedAtMs ?? Environment.TickCount64)) / 1000;
        uint idleMs = unchecked((uint)Environment.TickCount - tick);
        Log.Information($"[Shutdown] Input during grace period ({_graceInputCycles}/{GraceAbortInputCycles}): " +
            $"{sinceStartSec}s after start, last input {idleMs} ms ago.");

        return _graceInputCycles >= GraceAbortInputCycles;
    }

    private void MainCheckLoop()
    {
        // The first cycle always reports, so startup shows the same
        // state notification as any other transition.
        bool firstCycle = true;
        int cycle = 0;
        while (!_stopEvent.IsSet)
        {
            // Never let one failed cycle kill the worker thread: that used
            // to stop all monitoring (and the shutdown timer) silently.
            try
            {
                RunCycle(ref firstCycle, ref cycle);
            }
            catch (Exception ex)
            {
                Log.Error($"[Engine ERROR] Cycle failed: {ex.GetType().Name}: {ex.Message}");
            }

            _stopEvent.Wait(AppSettings.CheckIntervalMs);
        }

        // SetThreadExecutionState is per thread: release on the thread that set it.
        lock (_shutdownLock)
        {
            DisarmShutdown("dropped: application exiting");
        }

        PowerManagement.SetDisplayRequired(false);
        PowerManagement.SetSystemRequired(false);
    }

    private void RunCycle(ref bool firstCycle, ref int cycle)
    {
        if (++cycle % 75 == 0)
        {
            using var self = Process.GetCurrentProcess();
            Log.Debug($"[Perf] Managed: {GC.GetTotalMemory(false) / 1048576} MB, " +
                $"working set: {self.WorkingSet64 / 1048576} MB.");
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
        var started = new List<(MonitoredSource Source, Task<(bool Active, Exception? Error)> Task)>();
        foreach (var source in _sources)
        {
            // A check that hung in an earlier cycle is still running: never
            // start a second one on the same monitor, its state is not thread-safe.
            if (_pendingChecks.TryGetValue(source, out var pending) && !pending.IsCompleted)
            {
                Log.Warning($"[Engine] {source.Monitor.GetType().Name} check still running from an earlier cycle, skipped.");
                continue;
            }

            var task = Task.Run(() =>
            {
                try
                {
                    return (source.Monitor.CheckActive(), (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (false, ex);
                }
            });
            _pendingChecks[source] = task;
            started.Add((source, task));
        }

        // A hung or broken monitor must not stall the worker (and with it the
        // shutdown timer) forever. Unfinished sources keep their old state.
        if (!Task.WaitAll(started.Select(s => (Task)s.Task).ToArray(), TimeSpan.FromSeconds(30)))
        {
            Log.Error("[Engine ERROR] Activity check timed out; keeping previous source states.");
        }

        foreach (var (source, task) in started)
        {
            if (!task.IsCompletedSuccessfully)
            {
                continue;
            }

            var (active, error) = task.Result;
            if (error != null)
            {
                Log.Error($"[Engine ERROR] {source.Monitor.GetType().Name} check failed: " +
                    $"{error.GetType().Name}: {error.Message}");
                continue;
            }

            source.Update(active);
            if (active && source.Monitor is DualSenseMonitor or XInputMonitor or GamepadMonitor)
            {
                _lastGamepadInputMs = Environment.TickCount64;
            }
        }

        MonitoredSource? activeSource = _sources.FirstOrDefault(s => s.IsTrulyActive);
        AppState previousState = CurrentState;
        AppState newlyCalculatedState = activeSource?.Monitor.ActiveState ?? AppState.Idle;
        string reason = activeSource?.Monitor.DescribeActive() ?? "No activity";

        // Nice-to-have: when a gamepad drives the Gaming state, prefer the
        // name of the process loading the GPU. Falls back to "Gamepad
        // input" when no name is available. Reuses the top PID the GPU
        // check already measured; the trigger logic itself is untouched.
        if (activeSource?.Monitor is DualSenseMonitor or XInputMonitor or GamepadMonitor &&
            _gpuSource.IsTrulyActive &&
            _gpuMonitor.TryGetActiveProcessName(out string gpuName))
        {
            reason = gpuName;
        }

        // Long absence kills Gaming even if the GPU still renders something.
        // Downloads stay exempt. Disabled when the timeout is 0 (Never).
        if (AppSettings.AfkTimeoutMinutes > 0 &&
            newlyCalculatedState == AppState.Gaming &&
            Environment.TickCount64 - LastAnyInputMs() > AppSettings.AfkTimeoutMinutes * 60_000L)
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

        lock (_shutdownLock)
        {
            UpdateShutdownTracking(previousState, CurrentState);
        }
    }

    /// <summary>
    /// Arms on the Downloading-to-Idle transition, aborts on sustained input
    /// or a state change, fires shutdown after the grace period plus
    /// a one-minute force toast countdown. Caller holds _shutdownLock.
    /// </summary>
    private void UpdateShutdownTracking(AppState previousState, AppState currentState)
    {
        int graceMinutes = AppSettings.ShutdownGraceMinutes;
        if (graceMinutes <= 0)
        {
            DisarmShutdown();
            return;
        }

        long now = Environment.TickCount64;
        if (previousState == AppState.Downloading && currentState == AppState.Idle)
        {
            _graceStartedAtMs = now;
            _graceEndsAtMs = now + graceMinutes * 60_000L;
            _finalEndsAtMs = null;
            _graceInputCycles = 0;
            TryGetLastInputTick(out _graceLastInputTick);
            Log.Information($"[Shutdown] Downloads finished, grace period started ({graceMinutes} min).");
        }

        if (_finalEndsAtMs != null)
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

            int remaining = (int)Math.Ceiling((_finalEndsAtMs.Value - now) / 1000.0);
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

        if (_graceEndsAtMs != null)
        {
            if (currentState != AppState.Idle)
            {
                DisarmShutdown("aborted: state became active");
                return;
            }

            if (IsUserBackDuringGrace())
            {
                DisarmShutdown("aborted: input detected");
                return;
            }

            if (now >= _graceEndsAtMs.Value)
            {
                _graceEndsAtMs = null;
                _finalEndsAtMs = now + 60_000L;
                NotificationHelper.ShowShutdownCountdownNotification(60);
                Log.Warning("[Shutdown] Grace over, final countdown started.");
            }
        }
    }

    private void ClearShutdownTimers()
    {
        _graceStartedAtMs = null;
        _graceEndsAtMs = null;
        _finalEndsAtMs = null;
    }

    /// <summary>
    /// Drops a pending shutdown. Caller holds _shutdownLock.
    /// </summary>
    private void DisarmShutdown(string? reason = null, bool notifyUser = false)
    {
        bool wasArmed = _graceEndsAtMs != null || _finalEndsAtMs != null;
        ClearShutdownTimers();
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
