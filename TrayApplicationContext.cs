using Serilog;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private AppState _currentState = AppState.Idle;

    private DualSenseMonitor _dualSenseMonitor = new DualSenseMonitor();
    private static volatile bool _isShuttingDown = false;

    private const int INACTIVITY_THRESHOLD_CYCLES = 30;
    private static int _gamepadInactivityCounter = INACTIVITY_THRESHOLD_CYCLES;

    private const int DOWNLOAD_INACTIVITY_THRESHOLD_CYCLES = 10;
    private static int _downloadInactivityCounter = DOWNLOAD_INACTIVITY_THRESHOLD_CYCLES;

    private const int GPU_INACTIVITY_THRESHOLD_CYCLES = 10;
    private static int _gpuInactivityCounter = GPU_INACTIVITY_THRESHOLD_CYCLES;

    private const int CHECK_INTERVAL_MS = 10000;

    public TrayApplicationContext()
    {
        LoggingConfig.ConfigureLogger();

        Log.Information("Application started and configured.");

        trayIcon = new NotifyIcon()
        {
            Icon = new Icon("GM.ico"),
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true,
            Text = "Gaming Monitor - Idle"
        };

        ToolStripMenuItem stateItem = new ToolStripMenuItem($"Current state: {_currentState}");

        trayIcon.ContextMenuStrip.Items.Add(stateItem);
        trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit", null, Exit_Click)
        {
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        trayIcon.ContextMenuStrip.Items.Add(exitItem);

        Thread mainThread = new Thread(MainCheckLoop);
        mainThread.IsBackground = true;
        mainThread.Start();
    }

    private void MainCheckLoop()
    {
        NotificationHelper.ShowWelcomeNotification();
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
        trayIcon.Text = $"Gaming Monitor - {_currentState}";
        trayIcon.ContextMenuStrip.Items[0].Text = $"State: {_currentState}";

        NotificationHelper.ShowStateChangeNotification(_currentState.ToString(), isDisplayControlled, isSleepControlled);
    }

    private void ShowStatus_Click(object sender, EventArgs e)
    {
        trayIcon.ContextMenuStrip.Items[0].Text = $"State: {_currentState}";
    }

    private void Exit_Click(object sender, EventArgs e)
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