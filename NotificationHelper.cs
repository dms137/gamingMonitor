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

    public static void ShowWelcomeNotification()
    {
        try
        {
            new ToastContentBuilder()
                .AddText("Game monitor is up & running")
                .SetToastDuration(ToastDuration.Short)
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
    public static void ShowStateChangeNotification(string newState, bool isDisplayControlled, bool isSleepControlled)
    {
        try
        {
            new ToastContentBuilder()
                .AddText($"{newState}")
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