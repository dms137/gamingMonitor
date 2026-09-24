using Microsoft.Toolkit.Uwp.Notifications;
using System.Diagnostics;

public static partial class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Self-update helper mode: --apply-update <sourceDir> <waitPid> <installDir>
        if (args.Length == 4 && args[0] == "--apply-update" && int.TryParse(args[2], out int waitPid))
        {
            Updater.Apply(args[1].Trim('"'), waitPid, args[3].Trim('"'));
            return;
        }

        string? justUpdatedTo = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--updated")
            {
                justUpdatedTo = args[i + 1].Trim('"');
                break;
            }
        }

        // Mandatory initialization of AUMID and Toast Manager
        const string MyAumid = "MyCompany.GamingMonitor";
        NotificationAumidHelper.SetCurrentProcessExplicitAppUserModelID(MyAumid);

        // Enable modern control styles for settings UI
        ApplicationConfiguration.Initialize();

        var context = new TrayApplicationContext(justUpdatedTo);

        // Toast clicks (fire on a background thread)
        ToastNotificationManagerCompat.OnActivated += toastArgs =>
        {
            ToastArguments parsed = ToastArguments.Parse(toastArgs.Argument);
            string action = parsed.Contains("action") ? parsed["action"] : string.Empty;
            if (action == "openRelease" && parsed.Contains("url"))
            {
                Process.Start(new ProcessStartInfo(parsed["url"]) { UseShellExecute = true });
            }
            else if (action == "downloadUpdate" && parsed.Contains("tag") && parsed.Contains("zip"))
            {
                context.DownloadAndApplyUpdate(parsed["tag"], parsed["zip"]);
            }
            else
            {
                context.ShowSettings();
            }
        };

        // Run application using tray context
        Application.Run(context);
    }
}