using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;
using System.Runtime.InteropServices;

public static partial class NotificationAumidHelper
{
    // Declare Win32 function to set AUMID
    [LibraryImport("shell32.dll", SetLastError = true)]
    public static partial void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string AppID);
}

public static class NotificationHelper
{
    private static ToastContentBuilder AppendStatus(this ToastContentBuilder builder, bool isDisplayControlled, bool isSleepControlled)
    {
        string displayString = isDisplayControlled ? "not " : "";
        string sleepString = isSleepControlled ? "not " : "";
        return builder
            .AddText($"Display shutdown: {displayString}allowed")
            .AddText($"Sleep: {sleepString}allowed");
    }

    /// <summary>
    /// Shows a toast about an available update with a Download button.
    /// Clicking it fires OnActivated with action=downloadUpdate.
    /// </summary>
    public static void ShowUpdateAvailableNotification(string version, string tag, string zipUrl)
    {
        try
        {
            new ToastContentBuilder()
                .AddText($"Update available: {version}")
                .AddText("Click Download to install it automatically.")
                .AddButton(new ToastButton()
                    .SetContent("Download and install")
                    .AddArgument("action", "downloadUpdate")
                    .AddArgument("tag", tag)
                    .AddArgument("zip", zipUrl))
                .SetToastDuration(ToastDuration.Long)
                .Show();
        }
        catch (Exception ex)
        {
            Log.Error($"[Notification ERROR] Failed to show toast: {ex.Message}");
        }
    }

    /// <summary>
    /// Fallback when the release has no downloadable zip.
    /// Clicking it fires OnActivated with action=openRelease.
    /// </summary>
    public static void ShowReleasePageNotification(string version, string url)
    {
        try
        {
            new ToastContentBuilder()
                .AddText($"Update available: {version}")
                .AddText("Click Open to download it manually.")
                .AddButton(new ToastButton()
                    .SetContent("Open release page")
                    .AddArgument("action", "openRelease")
                    .AddArgument("url", url))
                .SetToastDuration(ToastDuration.Long)
                .Show();
        }
        catch (Exception ex)
        {
            Log.Error($"[Notification ERROR] Failed to show toast: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a toast confirming the app has updated and restarted.
    /// </summary>
    public static void ShowUpdatedNotification(string version)
    {
        try
        {
            new ToastContentBuilder()
                .AddText("Update installed")
                .AddText($"Running version {version}")
                .SetToastDuration(ToastDuration.Long)
                .Show();
        }
        catch (Exception ex)
        {
            Log.Error($"[Notification ERROR] Failed to show toast: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a toast confirming the app is up to date.
    /// </summary>
    public static void ShowUpToDateNotification(string current)
    {
        try
        {
            new ToastContentBuilder()
                .AddText("You are up to date")
                .AddText($"Current version: {current}")
                .SetToastDuration(ToastDuration.Short)
                .Show();
        }
        catch (Exception ex)
        {
            Log.Error($"[Notification ERROR] Failed to show toast: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a toast when the automatic update fails.
    /// </summary>
    public static void ShowUpdateFailedNotification()
    {
        try
        {
            new ToastContentBuilder()
                .AddText("Update failed")
                .AddText("See the log file for details.")
                .SetToastDuration(ToastDuration.Long)
                .Show();
        }
        catch (Exception ex)
        {
            Log.Error($"[Notification ERROR] Failed to show toast: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a Windows toast notification about state change.
    /// </summary>
    /// <param name="newState">New application state (e.g., "Gaming" or "Idle").</param>
    public static void ShowStateChangeNotification(string newState, string reason, bool isDisplayControlled, bool isSleepControlled)
    {
        try
        {
            // Toasts allow 3 text lines max and no colored text,
            // so the reason shares the plain title line.
            new ToastContentBuilder()
                .AddText($"{newState} ({reason})")
                .AppendStatus(isDisplayControlled, isSleepControlled)
                .SetToastDuration(ToastDuration.Short)
                .Show();
        }
        catch (Exception ex)
        {
            Log.Error($"[Notification ERROR] Failed to show toast: {ex.Message}");
        }
    }
}
