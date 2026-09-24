using Microsoft.Toolkit.Uwp.Notifications;

public static partial class Program
{
    [STAThread]
    public static void Main()
    {
        // Mandatory initialization of AUMID and Toast Manager
        const string MyAumid = "MyCompany.GamingMonitor";
        NotificationAumidHelper.SetCurrentProcessExplicitAppUserModelID(MyAumid);
        ToastNotificationManagerCompat.OnActivated += toastArgs => { };

        // Run application using tray context
        Application.Run(new TrayApplicationContext());
    }
}