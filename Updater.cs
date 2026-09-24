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
            Log($"Updater started. source={sourceDir} waitPid={waitPid} install={installDir}");

            try
            {
                Process.GetProcessById(waitPid).WaitForExit(20000);
                Log("Main instance exited.");
            }
            catch (Exception ex)
            {
                Log($"Wait finished: {ex.GetType().Name}");
            }

            Thread.Sleep(1000);

            string freshExe = Path.Combine(sourceDir, "GamingMonitor.exe");
            string targetExe = Path.Combine(installDir, "GamingMonitor.exe");
            if (File.Exists(freshExe))
            {
                File.Copy(freshExe, targetExe, true);
                Log($"Copied exe, new size: {new FileInfo(targetExe).Length} bytes.");
            }
            else
            {
                Log($"ERROR: fresh exe not found: {freshExe}");
            }

            string assetsSource = Path.Combine(sourceDir, "assets");
            if (Directory.Exists(assetsSource))
            {
                Directory.CreateDirectory(Path.Combine(installDir, "assets"));
                int copied = 0;
                foreach (string file in Directory.GetFiles(assetsSource))
                {
                    if (string.Equals(Path.GetFileName(file), SettingsFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    File.Copy(file, Path.Combine(installDir, "assets", Path.GetFileName(file)), true);
                    copied++;
                }

                Log($"Copied {copied} asset(s).");
            }
            else
            {
                Log($"No assets folder: {assetsSource}");
            }

            string tag = Path.GetFileName(Path.GetDirectoryName(sourceDir)) ?? string.Empty;
            string arguments = string.IsNullOrEmpty(tag) ? string.Empty : $"--updated \"{tag}\"";
            Process.Start(new ProcessStartInfo(Path.Combine(installDir, "GamingMonitor.exe"))
            {
                Arguments = arguments,
                UseShellExecute = true,
                WorkingDirectory = installDir
            });
            Log("Restarted new instance.");

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
        catch (Exception ex)
        {
            Log($"FATAL: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        try
        {
            string logPath = Path.Combine(Path.GetTempPath(), "GamingMonitor", "update.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
