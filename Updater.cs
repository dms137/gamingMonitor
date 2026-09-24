using System.Diagnostics;

/// <summary>
/// Self-update helper. Runs as a short-lived copy of the app executable
/// launched with --apply-update, so the installed files are not locked.
/// </summary>
public static class Updater
{
    private const string SettingsFileName = "settings.json";

    /// <summary>
    /// Waits for the main instance to exit, copies new files over the
    /// installation (preserving settings.json), restarts the app.
    /// </summary>
    public static void Apply(string sourceDir, int waitPid, string installDir)
    {
        try
        {
            try
            {
                Process.GetProcessById(waitPid).WaitForExit(20000);
            }
            catch { }

            Thread.Sleep(1000);

            string? currentExe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(currentExe))
            {
                string freshExe = Path.Combine(sourceDir, Path.GetFileName(currentExe));
                if (File.Exists(freshExe))
                {
                    File.Copy(freshExe, Path.Combine(installDir, Path.GetFileName(currentExe)), true);
                }
            }

            string assetsSource = Path.Combine(sourceDir, "assets");
            if (Directory.Exists(assetsSource))
            {
                Directory.CreateDirectory(Path.Combine(installDir, "assets"));
                foreach (string file in Directory.GetFiles(assetsSource))
                {
                    if (string.Equals(Path.GetFileName(file), SettingsFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    File.Copy(file, Path.Combine(installDir, "assets", Path.GetFileName(file)), true);
                }
            }

            Process.Start(new ProcessStartInfo(Path.Combine(installDir, "GamingMonitor.exe"))
            {
                UseShellExecute = true,
                WorkingDirectory = installDir
            });

            // Self-cleanup: a running exe cannot delete its own folder,
            // so schedule deletion after this process exits.
            try
            {
                string? tempRoot = Path.GetDirectoryName(sourceDir);
                if (!string.IsNullOrEmpty(tempRoot))
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 3 /nobreak >nul & rmdir /s /q \"{tempRoot}\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
            }
            catch { }
        }
        catch { }
    }
}
