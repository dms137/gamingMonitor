using Serilog;
using System.Runtime.InteropServices;

public partial class SettingsForm : Form
{
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    private static partial IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hmod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll", EntryPoint = "CallNextHookEx")]
    private static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private static readonly Color BgColor = Color.FromArgb(32, 32, 32);
    private static readonly Color PanelColor = Color.FromArgb(45, 45, 45);
    private static readonly Color TextColor = Color.FromArgb(243, 243, 243);
    private static readonly Color SecondaryColor = Color.FromArgb(160, 160, 160);
    private static readonly Color AccentColor = Color.FromArgb(76, 194, 255);
    private static readonly Color GamingColor = Color.FromArgb(108, 203, 95);

    private readonly Func<AppState> _getState;
    private readonly TrackBar _thresholdSlider;
    private readonly Label _valueLabel;
    private readonly Label _stateDot;
    private readonly Label _stateLabel;
    private readonly Label _gpuLoadValue;
    private readonly ToolTip _toolTip;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private LowLevelMouseProc? _mouseHookProc;
    private IntPtr _mouseHook = IntPtr.Zero;

    public SettingsForm(Func<AppState> getState)
    {
        _getState = getState;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(360, 244);
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

        var titleLabel = new Label
        {
            Text = "Gaming Monitor",
            Location = new Point(20, 12),
            AutoSize = true,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = TextColor,
            BackColor = BgColor
        };
        titleLabel.MouseDown += Header_MouseDown;

        _stateDot = new Label
        {
            Text = "●",
            Location = new Point(20, 50),
            AutoSize = true,
            Font = new Font("Segoe UI", 12, FontStyle.Regular),
            ForeColor = SecondaryColor,
            BackColor = BgColor
        };
        _stateDot.MouseDown += Header_MouseDown;

        _stateLabel = new Label
        {
            Location = new Point(42, 52),
            AutoSize = true,
            Font = new Font("Segoe UI", 11, FontStyle.Regular),
            ForeColor = TextColor,
            BackColor = BgColor
        };
        _stateLabel.MouseDown += Header_MouseDown;

        header.Controls.Add(titleLabel);
        header.Controls.Add(_stateDot);
        header.Controls.Add(_stateLabel);

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

        _toolTip = new ToolTip();
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

        // Body grid: captions on the left, values right-aligned in one column
        var grid = new TableLayoutPanel
        {
            Location = new Point(20, 96),
            Size = new Size(320, 132),
            ColumnCount = 2,
            RowCount = 4,
            BackColor = BgColor,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

        grid.Controls.Add(gpuCaption, 0, 0);
        grid.Controls.Add(_gpuLoadValue, 1, 0);
        grid.Controls.Add(captionFlow, 0, 1);
        grid.Controls.Add(_valueLabel, 1, 1);
        grid.Controls.Add(_thresholdSlider, 0, 2);
        grid.SetColumnSpan(_thresholdSlider, 2);
        grid.Controls.Add(closeButton, 1, 3);

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

        IntPtr region = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 12, 12);
        Region?.Dispose();
        Region = Region.FromHrgn(region);
        DeleteObject(region);

        // Dismiss when clicking anywhere outside the flyout
        _mouseHookProc = MouseHookCallback;
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, GetModuleHandle(null), 0);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !IsDisposed &&
            (wParam == (IntPtr)WM_LBUTTONDOWN || wParam == (IntPtr)WM_RBUTTONDOWN || wParam == (IntPtr)WM_MBUTTONDOWN))
        {
            POINT pt = Marshal.PtrToStructure<POINT>(lParam);
            if (!Bounds.Contains(pt.X, pt.Y))
            {
                BeginInvoke(Close);
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
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
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
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
        AppState state = _getState();
        _stateLabel.Text = state.ToString();
        _stateDot.ForeColor = state switch
        {
            AppState.Gaming => GamingColor,
            AppState.DownloadingActive => AccentColor,
            _ => SecondaryColor
        };

        _gpuLoadValue.Text = $"{GpuMonitor.LastUtilization:F1}%";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        AppSettings.Save();
        Log.Information($"[Settings] GPU threshold set to {AppSettings.GpuThresholdPercent:F0}%.");
        base.OnFormClosed(e);
    }

}
