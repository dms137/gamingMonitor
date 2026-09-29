namespace GamingMonitor.Infrastructure;

using System.Runtime.InteropServices;

/// <summary>
/// Win32 interop shared by tray and flyout UI.
/// </summary>
internal static partial class NativeMethods
{
    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HTCAPTION = 0x2;

    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_MBUTTONDOWN = 0x0207;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    public static partial IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hmod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll", EntryPoint = "CallNextHookEx")]
    public static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandle(string? lpModuleName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr hIcon);

    [LibraryImport("user32.dll", EntryPoint = "GetLastInputInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    public static partial uint XInputGetState(uint dwUserIndex, ref XINPUT_STATE pState);

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint PacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }
}
