namespace GamingMonitor.Infrastructure;

using Microsoft.Toolkit.Uwp.Notifications;
using Serilog;
using System.Runtime.InteropServices;
using Windows.UI.Notifications;

public static partial class NotificationAumidHelper
{
    // Declare Win32 function to set AUMID
    [LibraryImport("shell32.dll", SetLastError = true)]
    public static partial void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string AppID);
}

public static class NotificationHelper
{
    private const string ShutdownTag = "gm-shutdown";
    private const string CountdownBinding = "countdown";

    // Set while the countdown toast is up and can be updated in place.
    // Only touched under the engine's shutdown lock.
    private static bool _countdownShown;

    private static ToastContentBuilder AppendStatus(this ToastContentBuilder builder, bool isDisplayControlled, bool isSleepControlled)
    {
        string displayString = isDisplayControlled ? "always on" : "can turn off";
        string sleepString = isSleepControlled ? "blocked" : "allowed";
        return builder
            .AddText($"Display: {displayString}")
            .AddText($"Sleep: {sleepString}");
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
                .AddButton(new ToastButton()
                    .SetContent("Skip this version")
                    .AddArgument("action", "skipUpdate")
                    .AddArgument("tag", tag))
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
    /// Shows (or refreshes) the shutdown countdown toast. Bypasses
    /// the state notification toggle: this one is always important.
    /// </summary>
    public static void ShowShutdownCountdownNotification(int secondsRemaining)
    {
        string status = $"Shutdown in {secondsRemaining} seconds. Click Cancel to stop it.";
        try
        {
            // Refresh the live toast in place: re-showing it every cycle
            // popped it up (with sound) again each time.
            if (_countdownShown && TryUpdateCountdown(status))
            {
                return;
            }

            // First show, or the user dismissed it: show it again,
            // so the countdown is never silent.
            HideShutdownNotification();
            new ToastContentBuilder()
                .AddText("Download finished. Shutting down.")
                .AddVisualChild(new AdaptiveText { Text = new BindableString(CountdownBinding) })
                .AddButton(new ToastButton()
                    .SetContent("Cancel")
                    .AddArgument("action", "cancelShutdown"))
                .SetToastDuration(ToastDuration.Long)
                .Show(toast =>
                {
                    toast.Tag = ShutdownTag;
                    toast.Data = new NotificationData { SequenceNumber = 0 };
                    toast.Data.Values[CountdownBinding] = status;
                });
            _countdownShown = true;
        }
        catch (Exception ex)
        {
            Log.Error($"[Notification ERROR] Failed to show toast: {ex.Message}");
        }
    }

    /// <summary>
    /// Replaces the countdown toast with a cancellation confirmation, so it
    /// never just vanishes. Same tag, so it morphs the existing toast instead
    /// of spawning a second one. Bypasses the state notification toggle.
    /// </summary>
    public static void ShowShutdownCanceledNotification()
    {
        try
        {
            HideShutdownNotification();
            new ToastContentBuilder()
                .AddText("Shutdown canceled")
                .AddText("Activity detected - your PC will stay on.")
                .SetToastDuration(ToastDuration.Short)
                .Show(toast => toast.Tag = ShutdownTag);
        }
        catch (Exception ex)
        {
            Log.Error($"[Notification ERROR] Failed to show toast: {ex.Message}");
        }
    }

    private static bool TryUpdateCountdown(string status)
    {
        // SequenceNumber 0 always applies, regardless of earlier updates.
        var data = new NotificationData { SequenceNumber = 0 };
        data.Values[CountdownBinding] = status;
        return ToastNotificationManagerCompat.CreateToastNotifier().Update(data, ShutdownTag)
            == NotificationUpdateResult.Succeeded;
    }

    public static void HideShutdownNotification()
    {
        _countdownShown = false;
        try
        {
            ToastNotificationManagerCompat.History.Remove(ShutdownTag);
        }
        catch { }
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
        // Update and up-to-date toasts always show, state toasts respect the toggle.
        if (!AppSettings.ShowStateNotifications)
        {
            return;
        }

        try
        {
            // Toasts allow 3 text lines max and no colored text,
            // so the reason shares the plain title line.
            // A fixed tag replaces the previous state toast instead of piling up.
            try { ToastNotificationManagerCompat.History.Remove("gm-state"); }
            catch { }
            new ToastContentBuilder()
                .AddText($"{newState} ({reason})")
                .AppendStatus(isDisplayControlled, isSleepControlled)
                .SetToastDuration(ToastDuration.Short)
                .Show(toast => toast.Tag = "gm-state");
        }
        catch (Exception ex)
        {
            Log.Error($"[Notification ERROR] Failed to show toast: {ex.Message}");
        }
    }
}
