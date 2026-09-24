using Serilog;
using System.Text.Json;

public static class AppSettings
{
    public const float DefaultGpuThresholdPercent = 25.0f;
    public const float MinGpuThresholdPercent = 5.0f;
    public const float MaxGpuThresholdPercent = 90.0f;

    private static readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static float _gpuThresholdPercent = DefaultGpuThresholdPercent;

    public static float GpuThresholdPercent
    {
        get => _gpuThresholdPercent;
        set => _gpuThresholdPercent = Math.Clamp(value, MinGpuThresholdPercent, MaxGpuThresholdPercent);
    }

    /// <summary>
    /// Loads settings from settings.json if it exists, otherwise keeps defaults.
    /// </summary>
    public static void Load()
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
            }

            Log.Information($"[Settings] Loaded. GPU threshold: {GpuThresholdPercent:F0}%.");
        }
        catch (Exception ex)
        {
            Log.Error($"[Settings ERROR] Failed to load settings, using defaults: {ex.Message}");
        }
    }

    /// <summary>
    /// Persists current settings to settings.json.
    /// </summary>
    public static void Save()
    {
        try
        {
            var data = new SettingsData { GpuThresholdPercent = GpuThresholdPercent };
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);

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
    }
}
