using HidSharp;
using Serilog;

public class DualSenseMonitor : IDisposable
{
    private const int DualSenseVID = 0x054C;
    private const int DualSensePID = 0x0CE6;

    private const int STICK_DEADZONE_INT = 30; // ~12% of 255
    private const int BT_REPORT_LENGTH = 547;

    private HidDevice _dualSenseDevice;
    private HidStream _dualSenseStream;
    private byte[] _inputReportBuffer = new byte[BT_REPORT_LENGTH];

    public DualSenseMonitor()
    {
        InitializeDualSense();
    }

    /// <summary>
    /// Finds DualSense by VID/PID and opens read stream.
    /// </summary>
    private void InitializeDualSense()
    {
        const DeviceTypes searchTypes = DeviceTypes.Hid | DeviceTypes.Ble;

        _dualSenseDevice = DeviceList.Local.GetDevices(searchTypes)
            .OfType<HidDevice>()
            .FirstOrDefault(d => d.VendorID == DualSenseVID && d.ProductID == DualSensePID);

        if (_dualSenseDevice != null)
        {
            try
            {
                _dualSenseStream = _dualSenseDevice.Open();
                _dualSenseStream.ReadTimeout = 50;
                Log.Information($"[DualSense HID]: Controller '{_dualSenseDevice.GetProductName()}' found and stream opened.");
            }
            catch (Exception ex)
            {
                Log.Error($"[DualSense HID ERROR]: Failed to open stream: {ex.Message}");
                Dispose();
            }
        }
        else
        {
            // Console.WriteLine("[DualSense HID]: Controller not found.");
        }
    }

    /// <summary>
    /// Reads raw Bluetooth report and checks stick/button activity.
    /// </summary>
    /// <returns>True if movement or press is detected.</returns>
    public bool IsGamepadActive()
    {
        if (_dualSenseStream == null)
        {
            InitializeDualSense();
            if (_dualSenseStream == null) return false;
        }

        try
        {
            int bytesRead = _dualSenseStream.Read(_inputReportBuffer, 0, _inputReportBuffer.Length);

            if (bytesRead == 0)
            {
                return false;
            }

            // 1. Sticks (range 0-255, center 128)
            byte leftX = _inputReportBuffer[3];
            byte leftY = _inputReportBuffer[4];

            if (Math.Abs(leftX - 128) > STICK_DEADZONE_INT || Math.Abs(leftY - 128) > STICK_DEADZONE_INT)
            {
                return true;
            }

            // 2. Buttons (bitmask in bytes 10 and 11)
            byte buttons1 = _inputReportBuffer[10];
            byte buttons2 = _inputReportBuffer[11];

            if (buttons1 > 0 || buttons2 > 0)
            {
                return true;
            }

            // Triggers, motion sensors, and touchpad are ignored to avoid false positives.
        }
        catch (TimeoutException)
        {
            // Timeout (no new data) is normal
        }
        catch (Exception ex)
        {
            Log.Error($"[DualSense HID CRITICAL]: Error during read. Disposing. {ex.Message}");
            Dispose();
        }

        return false;
    }

    /// <summary>
    /// Disposes the stream on application exit.
    /// </summary>
    public void Dispose()
    {
        if (_dualSenseStream != null)
        {
            try
            {
                _dualSenseStream.Dispose();
            }
            catch { }
        }
        _dualSenseStream = null;
        _dualSenseDevice = null;
    }
}