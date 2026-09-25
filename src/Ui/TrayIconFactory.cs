namespace GamingMonitor.Ui;

using System.Drawing.Drawing2D;

/// <summary>
/// Builds tray icons from the base image with a Teams-like presence dot.
/// </summary>
public static class TrayIconFactory
{
    private static readonly Color GamingDotColor = Color.FromArgb(108, 203, 95);
    private static readonly Color DownloadingDotColor = Color.FromArgb(76, 194, 255);

    public static Icon Create(AppState state, Bitmap baseBitmap)
    {
        Color? dot = state switch
        {
            AppState.Gaming => GamingDotColor,
            AppState.Downloading => DownloadingDotColor,
            _ => null
        };

        using var bitmap = new Bitmap(baseBitmap);
        if (dot != null)
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            const int diameter = 16;
            const int ring = 2;
            int x = bitmap.Width - diameter;
            int y = bitmap.Height - diameter;
            graphics.FillEllipse(Brushes.Black, x, y, diameter, diameter);
            using var brush = new SolidBrush(dot.Value);
            graphics.FillEllipse(brush, x + ring, y + ring, diameter - ring * 2, diameter - ring * 2);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
