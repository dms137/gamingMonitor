using Serilog;
using System.Diagnostics;
using System.IO.Compression;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private readonly SynchronizationContext? _uiContext;
    private AppState _currentState = AppState.Idle;
    private SettingsForm? _settingsForm;
    private DateTime _flyoutClosedAt = DateTime.MinValue;
    private volatile bool _updateInProgress;
    private readonly string? _justUpdatedTo;

    private DualSenseMonitor _dualSenseMonitor = new DualSenseMonitor();
    private static volatile bool _isShuttingDown = false;

    private const int INACTIVITY_THRESHOLD_CYCLES = 30;
    private static int _gamepadInactivityCounter = INACTIVITY_THRESHOLD_CYCLES;

    private const int DOWNLOAD_INACTIVITY_THRESHOLD_CYCLES = 10;
    private static int _downloadInactivityCounter = DOWNLOAD_INACTIVITY_THRESHOLD_CYCLES;

    private const int GPU_INACTIVITY_THRESHOLD_CYCLES = 10;
    private static int _gpuInactivityCounter = GPU_INACTIVITY_THRESHOLD_CYCLES;

    private const int CHECK_INTERVAL_MS = 10000;

    public TrayApplicationContext(string? justUpdatedTo = null)
    {
        _justUpdatedTo = justUpdatedTo;

        LoggingConfig.ConfigureLogger();
        AppSettings.Load();
        CleanStaleUpdateFiles();

        Log.Information("Application started and configured.");

        trayIcon = new NotifyIcon()
        {
            Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "assets", "GM.ico")),
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true,
            Text = "Gaming Monitor - Idle"
        };
        trayIcon.MouseClick += TrayIcon_MouseClick;

        ToolStripMenuItem stateItem = new ToolStripMenuItem($"Current state: {_currentState}")
        {
            Enabled = false
        };

        trayIcon.ContextMenuStrip.Items.Add(stateItem);

        trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem settingsItem = new ToolStripMenuItem("Settings", null, Settings_Click);
        trayIcon.ContextMenuStrip.Items.Add(settingsItem);

        ToolStripMenuItem updateItem = new ToolStripMenuItem("Check for updates", null, CheckUpdates_Click);
        trayIcon.ContextMenuStrip.Items.Add(updateItem);

        trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit", null, Exit_Click)
        {
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        trayIcon.ContextMenuStrip.Items.Add(exitItem);

        // Capture UI thread context, monitor loop runs on a background thread
        _uiContext = SynchronizationContext.Current;

        Thread mainThread = new Thread(MainCheckLoop);
        mainThread.IsBackground = true;
        mainThread.Start();

        // Fire-and-forget update check, must not block startup
        _ = Task.Run(() => UpdateChecker.CheckOnStartupAsync());
    }

    private void MainCheckLoop()
    {
        if (_justUpdatedTo != null)
        {
            NotificationHelper.ShowUpdatedNotification(_justUpdatedTo);
        }
        else
        {
            NotificationHelper.ShowWelcomeNotification();
        }
        AppState newlyCalculatedState;
        while (true)
        {
            if (_isShuttingDown)
            {
                break;
            }

            // 1. Gamepad activity check and counter
            bool isInputDetectedThisCycle = _dualSenseMonitor.IsGamepadActive();

            if (isInputDetectedThisCycle)
            {
                _gamepadInactivityCounter = 0;
            }
            else
            {
                _gamepadInactivityCounter++;
                if (_gamepadInactivityCounter > INACTIVITY_THRESHOLD_CYCLES)
                {
                    _gamepadInactivityCounter = INACTIVITY_THRESHOLD_CYCLES + 1;
                }
            }

            bool isGamepadTrulyActive = _gamepadInactivityCounter <= INACTIVITY_THRESHOLD_CYCLES;

            // 2. Download activity check
            bool isDownloadingThisCycle = NetworkMonitor.CheckAllDownloads();

            if (isDownloadingThisCycle)
            {
                _downloadInactivityCounter = 0;
            }
            else
            {
                _downloadInactivityCounter++;
                if (_downloadInactivityCounter > DOWNLOAD_INACTIVITY_THRESHOLD_CYCLES)
                {
                    _downloadInactivityCounter = DOWNLOAD_INACTIVITY_THRESHOLD_CYCLES + 1;
                }
            }

            bool isDownloadTrulyActive = _downloadInactivityCounter <= DOWNLOAD_INACTIVITY_THRESHOLD_CYCLES;

            // 3. GPU activity check
            bool isGpuThisCycle = GpuMonitor.IsGpuActive();

            if (isGpuThisCycle)
            {
                _gpuInactivityCounter = 0;
            }
            else
            {
                _gpuInactivityCounter++;
                if (_gpuInactivityCounter > GPU_INACTIVITY_THRESHOLD_CYCLES)
                {
                    _gpuInactivityCounter = GPU_INACTIVITY_THRESHOLD_CYCLES + 1;
                }
            }

            bool isGpuTrulyActive = _gpuInactivityCounter <= GPU_INACTIVITY_THRESHOLD_CYCLES;

            // --- Power Management Logic ---

            bool isDisplayControlled;
            bool isSleepControlled;
            if (isGamepadTrulyActive || isGpuTrulyActive)
            {
                isDisplayControlled = true;
                isSleepControlled = true;
                newlyCalculatedState = AppState.Gaming;
            }
            else if (isDownloadTrulyActive)
            {
                isDisplayControlled = false;
                isSleepControlled = true;
                newlyCalculatedState = AppState.DownloadingActive;
            }
            else
            {
                isDisplayControlled = false;
                isSleepControlled = false;
                newlyCalculatedState = AppState.Idle;
            }

            if (newlyCalculatedState != _currentState)
            {
                _currentState = newlyCalculatedState;

                Log.Information($"[State] New state: {_currentState.ToString()}.");

                PowerManagement.SetDisplayRequired(isDisplayControlled);
                PowerManagement.SetSystemRequired(isSleepControlled);

                UpdateUIAndSystemState(isDisplayControlled, isSleepControlled);
            }
            Thread.Sleep(CHECK_INTERVAL_MS);
        }
    }

    private void UpdateUIAndSystemState(bool isDisplayControlled, bool isSleepControlled)
    {
        // Called from the background monitor thread, marshal UI work to the UI thread
        void apply()
        {
            trayIcon.Text = $"Gaming Monitor - {_currentState}";

            var menu = trayIcon.ContextMenuStrip;
            if (menu != null && menu.Items.Count > 0)
            {
                menu.Items[0].Text = $"State: {_currentState}";
            }

            NotificationHelper.ShowStateChangeNotification(_currentState.ToString(), isDisplayControlled, isSleepControlled);
        }

        if (_uiContext != null)
        {
            _uiContext.Post(_ => apply(), null);
        }
        else
        {
            apply();
        }
    }

    private void TrayIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ToggleSettings();
        }
    }

    private void Settings_Click(object? sender, EventArgs e)
    {
        ToggleSettings();
    }

    private void CheckUpdates_Click(object? sender, EventArgs e)
    {
        _ = Task.Run(() => UpdateChecker.CheckOnStartupAsync(manual: true));
    }

    /// <summary>
    /// Opens the settings flyout. Safe to call from any thread (e.g. toast activation).
    /// </summary>
    public void ShowSettings()
    {
        if (_uiContext != null)
        {
            _uiContext.Post(_ => ToggleSettings(), null);
        }
        else
        {
            ToggleSettings();
        }
    }

    /// <summary>
    /// Removes leftover update files from previous sessions.
    /// </summary>
    private static void CleanStaleUpdateFiles()
    {
        try
        {
            string updateRoot = Path.Combine(Path.GetTempPath(), "GamingMonitor", "update");
            if (Directory.Exists(updateRoot))
            {
                Directory.Delete(updateRoot, true);
                Log.Information("[Update] Cleaned stale update files.");
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[Update] Stale update cleanup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads the release zip and applies it via a helper process.
    /// Safe to call from any thread.
    /// </summary>
    public void DownloadAndApplyUpdate(string tag, string zipUrl)
    {
        if (_updateInProgress)
        {
            return;
        }

        _updateInProgress = true;
        _ = Task.Run(() => DownloadAndApplyUpdateAsync(tag, zipUrl));
    }

    private async Task DownloadAndApplyUpdateAsync(string tag, string zipUrl)
    {
        string updateRoot = Path.Combine(Path.GetTempPath(), "GamingMonitor", "update");
        string tempRoot = Path.Combine(updateRoot, tag);
        try
        {
            Log.Information($"[Update] Downloading {tag}...");

            foreach (string dir in Directory.Exists(updateRoot) ? Directory.GetDirectories(updateRoot) : Array.Empty<string>())
            {
                if (!string.Equals(dir, tempRoot, StringComparison.OrdinalIgnoreCase))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }

            Directory.CreateDirectory(tempRoot);
            string zipPath = Path.Combine(tempRoot, "app.zip");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("GamingMonitor");
                using var response = await http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                await using var content = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(zipPath);
                await content.CopyToAsync(file);
            }

            string extractDir = Path.Combine(tempRoot, "new");
            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, true);
            }

            ZipFile.ExtractToDirectory(zipPath, extractDir);

            string? currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                throw new InvalidOperationException("Cannot locate current executable.");
            }

            string updaterDir = Path.Combine(tempRoot, "updater");
            Directory.CreateDirectory(updaterDir);
            string updaterExe = Path.Combine(updaterDir, "GamingMonitor.Updater.exe");
            File.Copy(currentExe, updaterExe, true);

            string installDir = AppContext.BaseDirectory;
            var startInfo = new ProcessStartInfo(updaterExe, $"--apply-update \"{extractDir}\" {Environment.ProcessId} \"{installDir}\"")
            {
                UseShellExecute = false
            };
            Process.Start(startInfo);

            Log.Information("[Update] Updater launched, exiting...");
            if (_uiContext != null)
            {
                _uiContext.Post(_ => Application.Exit(), null);
            }
            else
            {
                Application.Exit();
            }
        }
        catch (Exception ex)
        {
            _updateInProgress = false;
            Log.Error($"[Update ERROR] Auto-update failed: {ex.Message}");
            NotificationHelper.ShowUpdateFailedNotification();
        }
    }

    private void ToggleSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            // Guard against the deactivate/close and click race:
            // a click that just auto-closed the flyout counts as closing it.
            if ((DateTime.Now - _flyoutClosedAt).TotalMilliseconds < 300)
            {
                return;
            }

            _settingsForm = new SettingsForm(() => _currentState);
            _settingsForm.FormClosed += (s, e) => _flyoutClosedAt = DateTime.Now;
            _settingsForm.Show();
        }
        else
        {
            _settingsForm.Close();
        }
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        _isShuttingDown = true;

        Log.Information("System shutdown detected. Releasing power requests...");
        PowerManagement.SetDisplayRequired(false);
        PowerManagement.SetSystemRequired(false);
        _dualSenseMonitor.Dispose();
        Log.Information("----------------------------");
        Log.CloseAndFlush();

        trayIcon.Visible = false;
        Application.Exit();
    }
}