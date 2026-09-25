namespace GamingMonitor.Ui;

using GamingMonitor.Infrastructure;
using Microsoft.Win32;
using Serilog;
using System.Reflection;
using System.Runtime.InteropServices;

public partial class SettingsForm : Form
{
    private static readonly Color BgColor = Color.FromArgb(32, 32, 32);
    private static readonly Color PanelColor = Color.FromArgb(45, 45, 45);
    private static readonly Color TextColor = Color.FromArgb(243, 243, 243);
    private static readonly Color SecondaryColor = Color.FromArgb(160, 160, 160);
    private static readonly Color AccentColor = Color.FromArgb(76, 194, 255);
    private static readonly Color GamingColor = Color.FromArgb(108, 203, 95);

    private readonly Func<StateSnapshot> _getState;
    private readonly TrackBar _thresholdSlider;
    private readonly Label _valueLabel;
    private readonly Label _stateDot;
    private readonly Label _stateLabel;
    private readonly Label _stateDetail;
    private readonly Label _gpuLoadValue;
    private readonly ToolTip _toolTip;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private NativeMethods.LowLevelMouseProc? _mouseHookProc;
    private IntPtr _mouseHook = IntPtr.Zero;

    public SettingsForm(Func<StateSnapshot> getState)
    {
        _getState = getState;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(360, 270);
        BackColor = BgColor;
        ForeColor = TextColor;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;

        // Header (draggable)
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 84,
            BackColor = BgColor
        };
        header.MouseDown += Header_MouseDown;

        _stateDot = new Label
        {
            Text = "●",
            Location = new Point(20, 14),
            AutoSize = true,
            Font = new Font("Segoe UI", 15, FontStyle.Regular),
            ForeColor = SecondaryColor,
            BackColor = BgColor
        };
        _stateDot.MouseDown += Header_MouseDown;

        var titleLabel = new Label
        {
            Text = "Gaming Monitor",
            Location = new Point(44, 12),
            AutoSize = true,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = TextColor,
            BackColor = BgColor
        };
        titleLabel.MouseDown += Header_MouseDown;

        _stateLabel = new Label
        {
            Location = new Point(20, 52),
            AutoSize = true,
            Font = new Font("Segoe UI", 11, FontStyle.Regular),
            ForeColor = TextColor,
            BackColor = BgColor
        };
        _stateLabel.MouseDown += Header_MouseDown;

        _stateDetail = new Label
        {
            // No Anchor.Right here: the header panel has no final width yet
            // when children are added, so anchoring throws the label off-screen.
            // The form is not resizable, fixed coordinates are safe.
            Location = new Point(170, 52),
            Size = new Size(170, 22),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = SecondaryColor,
            BackColor = BgColor
        };
        _stateDetail.MouseDown += Header_MouseDown;

        header.Controls.Add(titleLabel);
        header.Controls.Add(_stateDot);
        header.Controls.Add(_stateLabel);
        header.Controls.Add(_stateDetail);

        // Current GPU load row
        var gpuCaption = new Label
        {
            Text = "GPU load",
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            ForeColor = SecondaryColor,
            BackColor = BgColor,
            Margin = Padding.Empty
        };

        _gpuLoadValue = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = TextColor,
            BackColor = BgColor,
            Margin = Padding.Empty
        };

        // Threshold row: caption on the left, value and info badge on the right
        var thresholdCaption = new Label
        {
            Text = "GPU activity threshold",
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            ForeColor = SecondaryColor,
            BackColor = BgColor,
            Margin = Padding.Empty
        };

        _valueLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = AccentColor,
            BackColor = BgColor,
            Margin = Padding.Empty
        };

        var infoLabel = new Label
        {
            Text = "🛈",
            AutoSize = true,
            Font = new Font("Segoe UI Symbol", 10, FontStyle.Regular),
            ForeColor = SecondaryColor,
            BackColor = BgColor,
            Cursor = Cursors.Hand,
            Margin = new Padding(6, 0, 0, 0)
        };

        var captionFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = BgColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        captionFlow.Controls.Add(thresholdCaption);
        captionFlow.Controls.Add(infoLabel);

        _toolTip = new ToolTip
        {
            InitialDelay = 200,
            ReshowDelay = 100,
            AutoPopDelay = 10000,
            ShowAlways = true
        };
        _toolTip.SetToolTip(infoLabel, "System is treated as gaming when GPU load exceeds this value.");

        _thresholdSlider = new TrackBar
        {
            Minimum = (int)AppSettings.MinGpuThresholdPercent,
            Maximum = (int)AppSettings.MaxGpuThresholdPercent,
            TickFrequency = 5,
            LargeChange = 10,
            SmallChange = 5,
            Value = (int)AppSettings.GpuThresholdPercent,
            Dock = DockStyle.Fill,
            BackColor = BgColor,
            Margin = new Padding(0, 4, 0, 0)
        };
        _thresholdSlider.Scroll += ThresholdSlider_Scroll;

        // Footer
        var versionLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = SecondaryColor,
            BackColor = BgColor,
            Margin = Padding.Empty
        };
        Version? appVersion = Assembly.GetExecutingAssembly().GetName().Version;
        versionLabel.Text = appVersion != null ? $"v{appVersion.Major}.{appVersion.Minor}.{appVersion.Build}" : "v?";

        var closeButton = new Button
        {
            Text = "Close",
            Size = new Size(76, 30),
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            BackColor = PanelColor,
            ForeColor = TextColor,
            Font = new Font("Segoe UI", 9, FontStyle.Regular)
        };
        closeButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        closeButton.Click += (s, e) => Close();

        var autoStartCheck = new CheckBox
        {
            Text = "Start with Windows",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = TextColor,
            BackColor = BgColor,
            Margin = Padding.Empty,
            Checked = AutoStart.IsEnabled()
        };
        autoStartCheck.CheckedChanged += (s, e) => AutoStart.SetEnabled(autoStartCheck.Checked);

        // Body grid: captions on the left, values right-aligned in one column
        var grid = new TableLayoutPanel
        {
            Location = new Point(20, 96),
            Size = new Size(320, 158),
            ColumnCount = 2,
            RowCount = 5,
            BackColor = BgColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

        grid.Controls.Add(gpuCaption, 0, 0);
        grid.Controls.Add(_gpuLoadValue, 1, 0);
        grid.Controls.Add(captionFlow, 0, 1);
        grid.Controls.Add(_valueLabel, 1, 1);
        grid.Controls.Add(_thresholdSlider, 0, 2);
        grid.SetColumnSpan(_thresholdSlider, 2);
        grid.Controls.Add(autoStartCheck, 0, 3);
        grid.SetColumnSpan(autoStartCheck, 2);
        grid.Controls.Add(versionLabel, 0, 4);
        grid.Controls.Add(closeButton, 1, 4);

        Controls.Add(header);
        Controls.Add(grid);

        UpdateValueLabel();
        UpdateStatus();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += RefreshTimer_Tick;
        _refreshTimer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_DROPSHADOW = 0x00020000;
            CreateParams cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Position near the tray, above the taskbar
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);

        ApplyRoundedCorners();

        // Dismiss when clicking anywhere outside the flyout
        _mouseHookProc = MouseHookCallback;
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseHookProc, NativeMethods.GetModuleHandle(null), 0);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyRoundedCorners();
    }

    private void ApplyRoundedCorners()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        IntPtr region = NativeMethods.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 12, 12);
        Region?.Dispose();
        Region = Region.FromHrgn(region);
        NativeMethods.DeleteObject(region);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !IsDisposed &&
            (wParam == (IntPtr)NativeMethods.WM_LBUTTONDOWN || wParam == (IntPtr)NativeMethods.WM_RBUTTONDOWN || wParam == (IntPtr)NativeMethods.WM_MBUTTONDOWN))
        {
            NativeMethods.POINT pt = Marshal.PtrToStructure<NativeMethods.POINT>(lParam);
            if (!Bounds.Contains(pt.X, pt.Y))
            {
                BeginInvoke(Close);
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        // Flyout behavior: close when focus moves elsewhere
        Close();
        base.OnDeactivate(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
        base.OnKeyDown(e);
    }

    private void Header_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, NativeMethods.HTCAPTION, 0);
        }
    }

    private void ThresholdSlider_Scroll(object? sender, EventArgs e)
    {
        // Snap to 5% steps, since mouse drag can stop on any value
        int snapped = (int)Math.Round(_thresholdSlider.Value / 5.0) * 5;
        snapped = Math.Clamp(snapped, _thresholdSlider.Minimum, _thresholdSlider.Maximum);
        if (snapped != _thresholdSlider.Value)
        {
            _thresholdSlider.Value = snapped;
        }

        AppSettings.GpuThresholdPercent = snapped;
        UpdateValueLabel();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        UpdateStatus();
    }

    private void UpdateValueLabel()
    {
        _valueLabel.Text = $"{AppSettings.GpuThresholdPercent:F0}%";
    }

    private void UpdateStatus()
    {
        StateSnapshot snapshot = _getState();
        _stateLabel.Text = snapshot.State.ToString();
        _stateDot.ForeColor = snapshot.State switch
        {
            AppState.Gaming => GamingColor,
            AppState.Downloading => AccentColor,
            _ => SecondaryColor
        };
        _stateDetail.Text = $"{snapshot.Reason} · {snapshot.Since:HH:mm}";

        _gpuLoadValue.Text = $"{snapshot.GpuUtilization:F1}%";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        AppSettings.Save();
        Log.Information($"[Settings] GPU threshold set to {AppSettings.GpuThresholdPercent:F0}%.");
        base.OnFormClosed(e);
    }

    /// <summary>
    /// Windows autostart via HKCU Run key.
    /// </summary>
    private static class AutoStart
    {
        private const string ValueName = "GamingMonitor";
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                string? value = key?.GetValue(ValueName) as string;
                string? exe = Environment.ProcessPath;
                return !string.IsNullOrEmpty(value) &&
                    !string.IsNullOrEmpty(exe) &&
                    value.Trim('"').Equals(exe, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null)
                {
                    return;
                }

                if (enabled)
                {
                    string? exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe))
                    {
                        key.SetValue(ValueName, $"\"{exe}\"");
                        Log.Information("[Settings] Autostart enabled.");
                    }
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                    Log.Information("[Settings] Autostart disabled.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Settings ERROR] Failed to update autostart: {ex.Message}");
            }
        }
    }

}
