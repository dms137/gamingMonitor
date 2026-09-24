using Microsoft.Toolkit.Uwp.Notifications;

public static partial class Program
{
    [STAThread]
    public static void Main()
    {
        // Mandatory initialization of AUMID and Toast Manager
        const string MyAumid = "MyCompany.GamingMonitor";
        NotificationAumidHelper.SetCurrentProcessExplicitAppUserModelID(MyAumid);

        // Enable modern control styles for settings UI
        ApplicationConfiguration.Initialize();

        var context = new TrayApplicationContext();

        // Clicking a toast opens the settings flyout (fires on a background thread)
        ToastNotificationManagerCompat.OnActivated += toastArgs => context.ShowSettings();

        // Run application using tray context
        Application.Run(context);
    }
}