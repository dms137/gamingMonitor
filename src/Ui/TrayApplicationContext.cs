namespace GamingMonitor.Ui;

using GamingMonitor.Infrastructure;
using GamingMonitor.Monitors;
using GamingMonitor.Updating;
using Serilog;
using System.Diagnostics;
using System.IO.Compression;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private readonly Bitmap _baseTrayBitmap;
    private readonly SynchronizationContext? _uiContext;
    private readonly ActivityEngine _engine = new ActivityEngine();
    private SettingsForm? _settingsForm;
    private DateTime _flyoutClosedAt = DateTime.MinValue;
    private volatile bool _updateInProgress;
    private readonly string? _justUpdatedTo;

    public TrayApplicationContext(string? justUpdatedTo = null)
    {
        _justUpdatedTo = justUpdatedTo;

        LoggingConfig.ConfigureLogger();
        AppSettings.Load();
        CleanStaleUpdateFiles();

        Log.Information("Application started and configured.");

        string iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "GM.ico");
        using (var baseIcon = new Icon(iconPath, new Size(32, 32)))
        {
            // ToBitmap can return the largest frame (256px), normalize to tray size
            using var raw = baseIcon.ToBitmap();
            _baseTrayBitmap = new Bitmap(raw, new Size(32, 32));
        }

        trayIcon = new NotifyIcon()
        {
            Icon = TrayIconFactory.Create(AppState.Idle, _baseTrayBitmap),
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true,
            Text = "Gaming Monitor - Idle"
        };
        trayIcon.MouseClick += TrayIcon_MouseClick;

        ToolStripMenuItem stateItem = new ToolStripMenuItem(_engine.CurrentState.ToString());
        stateItem.Click += (s, e) => ToggleSettings();
        trayIcon.ContextMenuStrip.Items.Add(stateItem);

        trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem updateItem = new ToolStripMenuItem("Check for updates", null, CheckUpdates_Click);
        trayIcon.ContextMenuStrip.Items.Add(updateItem);

        trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit", null, Exit_Click)
        {
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        trayIcon.ContextMenuStrip.Items.Add(exitItem);

        // Capture UI thread context, the engine loop runs on a background thread
        _uiContext = SynchronizationContext.Current;

        if (_justUpdatedTo != null)
        {
            NotificationHelper.ShowUpdatedNotification(_justUpdatedTo);
        }

        _engine.StateChanged += OnEngineStateChanged;
        _engine.Start();

        // Fire-and-forget update check, must not block startup
        _ = Task.Run(() => UpdateChecker.CheckOnStartupAsync());
    }

    private void SetTrayIcon(AppState state)
    {
        Icon? old = trayIcon.Icon;
        trayIcon.Icon = TrayIconFactory.Create(state, _baseTrayBitmap);
        old?.Dispose();
    }

    private void OnEngineStateChanged()
    {
        // Called on the engine worker thread, marshal UI work to the UI thread
        void apply()
        {
            trayIcon.Text = $"Gaming Monitor - {_engine.CurrentState}";
            SetTrayIcon(_engine.CurrentState);

            var menu = trayIcon.ContextMenuStrip;
            if (menu != null && menu.Items.Count > 0)
            {
                menu.Items[0].Text = _engine.CurrentState.ToString();
            }

            NotificationHelper.ShowStateChangeNotification(
                _engine.CurrentState.ToString(), _engine.Reason,
                _engine.IsDisplayControlled, _engine.IsSleepControlled);
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
                _uiContext.Post(_ => Shutdown("Update downloaded, restarting for install."), null);
            }
            else
            {
                Shutdown("Update downloaded, restarting for install.");
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

            _settingsForm = new SettingsForm(() => new StateSnapshot(_engine.CurrentState, _engine.Reason, _engine.ChangedAt, _engine.GpuUtilization));
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
        Shutdown("Exit requested from tray menu.");
    }

    /// <summary>
    /// Releases power requests, disposes hardware handles, flushes the log
    /// and exits. Shared by manual exit and update restart.
    /// </summary>
    private void Shutdown(string reason)
    {
        _engine.Dispose();

        Log.Information($"{reason} Releasing power requests...");
        PowerManagement.SetDisplayRequired(false);
        PowerManagement.SetSystemRequired(false);
        Log.Information("----------------------------");
        Log.CloseAndFlush();

        trayIcon.Visible = false;
        trayIcon.Icon?.Dispose();
        trayIcon.Dispose();
        _baseTrayBitmap.Dispose();
        Application.Exit();
    }
}