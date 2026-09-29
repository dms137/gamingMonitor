namespace GamingMonitor.Infrastructure;

using Serilog;
using System.Text.Json;

public static class AppSettings
{
    public const float DefaultGpuThresholdPercent = 25.0f;
    public const float MinGpuThresholdPercent = 5.0f;
    public const float MaxGpuThresholdPercent = 90.0f;

    public const int DefaultCheckIntervalMs = 10000;
    public const int MinCheckIntervalMs = 2000;
    public const int MaxCheckIntervalMs = 60000;

    public const int DefaultGamepadInactivityCycles = 30;
    public const int DefaultDownloadInactivityCycles = 10;
    public const int DefaultGpuInactivityCycles = 10;
    public const int MinInactivityCycles = 1;
    public const int MaxInactivityCycles = 600;

    public const int DefaultAfkTimeoutMinutes = 120;
    public const int MaxAfkTimeoutMinutes = 720;

    private static readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "assets", "settings.json");

    private static float _gpuThresholdPercent = DefaultGpuThresholdPercent;
    private static bool _showStateNotifications = true;
    private static int _checkIntervalMs = DefaultCheckIntervalMs;
    private static int _gamepadInactivityCycles = DefaultGamepadInactivityCycles;
    private static int _downloadInactivityCycles = DefaultDownloadInactivityCycles;
    private static int _gpuInactivityCycles = DefaultGpuInactivityCycles;
    private static int _afkTimeoutMinutes = DefaultAfkTimeoutMinutes;
    private static List<string> _downloadProcesses = new List<string> { "steam", "gamingservicesnet" };
    private static string _skippedUpdateVersion = string.Empty;
    private static bool _dirty;

    private static void SetField<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            _dirty = true;
        }
    }

    public static float GpuThresholdPercent
    {
        get => _gpuThresholdPercent;
        set => SetField(ref _gpuThresholdPercent, Math.Clamp(value, MinGpuThresholdPercent, MaxGpuThresholdPercent));
    }

    public static bool ShowStateNotifications
    {
        get => _showStateNotifications;
        set => SetField(ref _showStateNotifications, value);
    }

    public static int CheckIntervalMs
    {
        get => _checkIntervalMs;
        set => SetField(ref _checkIntervalMs, Math.Clamp(value, MinCheckIntervalMs, MaxCheckIntervalMs));
    }

    public static int GamepadInactivityCycles
    {
        get => _gamepadInactivityCycles;
        set => SetField(ref _gamepadInactivityCycles, Math.Clamp(value, MinInactivityCycles, MaxInactivityCycles));
    }

    public static int DownloadInactivityCycles
    {
        get => _downloadInactivityCycles;
        set => SetField(ref _downloadInactivityCycles, Math.Clamp(value, MinInactivityCycles, MaxInactivityCycles));
    }

    public static int GpuInactivityCycles
    {
        get => _gpuInactivityCycles;
        set => SetField(ref _gpuInactivityCycles, Math.Clamp(value, MinInactivityCycles, MaxInactivityCycles));
    }

    /// <summary>
    /// Minutes without any input (gamepad, keyboard, mouse) before Gaming
    /// is force-released. 0 means Never (disabled).
    /// </summary>
    public static int AfkTimeoutMinutes
    {
        get => _afkTimeoutMinutes;
        set => SetField(ref _afkTimeoutMinutes, Math.Clamp(value, 0, MaxAfkTimeoutMinutes));
    }

    /// <summary>
    /// Release tag the user chose to skip. Empty means none skipped.
    /// </summary>
    public static string SkippedUpdateVersion
    {
        get => _skippedUpdateVersion;
        set => SetField(ref _skippedUpdateVersion, value ?? string.Empty);
    }

    public static List<string> DownloadProcesses
    {
        get => _downloadProcesses;
        set
        {
            var cleaned = value?
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            if (!cleaned.SequenceEqual(_downloadProcesses, StringComparer.OrdinalIgnoreCase))
            {
                _downloadProcesses = cleaned;
                _dirty = true;
            }
        }
    }

    /// <summary>
    /// Loads settings from settings.json if it exists, otherwise keeps defaults.
    /// </summary>
    public static void Load(bool log = true)
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }

            string json = File.ReadAllText(_settingsPath);
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data != null)
            {
                GpuThresholdPercent = data.GpuThresholdPercent;
                ShowStateNotifications = data.ShowStateNotifications;
                CheckIntervalMs = data.CheckIntervalMs;
                GamepadInactivityCycles = data.GamepadInactivityCycles;
                DownloadInactivityCycles = data.DownloadInactivityCycles;
                GpuInactivityCycles = data.GpuInactivityCycles;
                AfkTimeoutMinutes = data.AfkTimeoutMinutes;
                DownloadProcesses = data.DownloadProcesses;
                SkippedUpdateVersion = data.SkippedUpdateVersion;
            }

            if (log)
            {
                Log.Information($"[Settings] Loaded. GPU threshold: {GpuThresholdPercent:F0}%, interval: {CheckIntervalMs}ms.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[Settings ERROR] Failed to load settings, using defaults: {ex.Message}");
        }
    }

    /// <summary>
    /// Reloads settings from disk, logging only when something actually changed.
    /// Picks up hand edits to settings.json.
    /// </summary>
    public static void Reload()
    {
        float oldThreshold = GpuThresholdPercent;
        bool oldNotifications = ShowStateNotifications;
        int oldInterval = CheckIntervalMs;
        int oldGamepad = GamepadInactivityCycles;
        int oldDownload = DownloadInactivityCycles;
        int oldGpu = GpuInactivityCycles;
        int oldAfk = AfkTimeoutMinutes;
        var oldProcesses = DownloadProcesses;
        string oldSkipped = SkippedUpdateVersion;

        Load(log: false);

        if (GpuThresholdPercent != oldThreshold ||
            ShowStateNotifications != oldNotifications ||
            CheckIntervalMs != oldInterval ||
            GamepadInactivityCycles != oldGamepad ||
            DownloadInactivityCycles != oldDownload ||
            GpuInactivityCycles != oldGpu ||
            AfkTimeoutMinutes != oldAfk ||
            !DownloadProcesses.SequenceEqual(oldProcesses, StringComparer.OrdinalIgnoreCase) ||
            SkippedUpdateVersion != oldSkipped)
        {
            Log.Information($"[Settings] Reloaded. GPU threshold: {GpuThresholdPercent:F0}%, interval: {CheckIntervalMs}ms.");
        }
    }

    /// <summary>
    /// Persists current settings to settings.json, but only if something changed.
    /// </summary>
    public static void Save()
    {
        if (!_dirty)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var data = new SettingsData
            {
                GpuThresholdPercent = GpuThresholdPercent,
                ShowStateNotifications = ShowStateNotifications,
                CheckIntervalMs = CheckIntervalMs,
                GamepadInactivityCycles = GamepadInactivityCycles,
                DownloadInactivityCycles = DownloadInactivityCycles,
                GpuInactivityCycles = GpuInactivityCycles,
                AfkTimeoutMinutes = AfkTimeoutMinutes,
                DownloadProcesses = DownloadProcesses,
                SkippedUpdateVersion = SkippedUpdateVersion
            };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
            _dirty = false;

            Log.Information($"[Settings] Saved. GPU threshold: {GpuThresholdPercent:F0}%.");
        }
        catch (Exception ex)
        {
            Log.Error($"[Settings ERROR] Failed to save settings: {ex.Message}");
        }
    }

    private sealed class SettingsData
    {
        public float GpuThresholdPercent { get; set; } = DefaultGpuThresholdPercent;
        public bool ShowStateNotifications { get; set; } = true;
        public int CheckIntervalMs { get; set; } = DefaultCheckIntervalMs;
        public int GamepadInactivityCycles { get; set; } = DefaultGamepadInactivityCycles;
        public int DownloadInactivityCycles { get; set; } = DefaultDownloadInactivityCycles;
        public int GpuInactivityCycles { get; set; } = DefaultGpuInactivityCycles;
        public int AfkTimeoutMinutes { get; set; } = DefaultAfkTimeoutMinutes;
        public List<string> DownloadProcesses { get; set; } = new List<string> { "steam", "gamingservicesnet" };
        public string SkippedUpdateVersion { get; set; } = string.Empty;
    }
}
