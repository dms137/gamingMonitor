namespace GamingMonitor.Monitors;

using GamingMonitor.Infrastructure;
using Serilog;

/// <summary>
/// Polls XInput controllers (Xbox and compatible gamepads).
/// Any button, trigger pull or stick deflection counts as activity.
/// </summary>
public class XInputMonitor : IActivityMonitor
{
    private const int MaxControllers = 4;
    private const byte TriggerThreshold = 30;
    private const short StickDeadzone = 8000;
    private static bool _xinputMissing;

    public AppState ActiveState => AppState.Gaming;

    public bool CheckActive()
    {
        if (_xinputMissing)
        {
            return false;
        }

        for (uint index = 0; index < MaxControllers; index++)
        {
            if (IsControllerActive(index))
            {
                return true;
            }
        }

        return false;
    }

    public string DescribeActive() => "Gamepad input";

    private static bool IsControllerActive(uint index)
    {
        try
        {
            var state = new NativeMethods.XINPUT_STATE();
            if (NativeMethods.XInputGetState(index, ref state) != 0)
            {
                return false;
            }

            var pad = state.Gamepad;
            return pad.Buttons != 0 ||
                pad.LeftTrigger > TriggerThreshold ||
                pad.RightTrigger > TriggerThreshold ||
                Math.Abs(pad.ThumbLX) > StickDeadzone ||
                Math.Abs(pad.ThumbLY) > StickDeadzone ||
                Math.Abs(pad.ThumbRX) > StickDeadzone ||
                Math.Abs(pad.ThumbRY) > StickDeadzone;
        }
        catch (DllNotFoundException)
        {
            _xinputMissing = true;
            Log.Debug("[XInput] xinput1_4.dll not found.");
            return false;
        }
        catch (Exception ex)
        {
            Log.Debug($"[XInput] Poll failed: {ex.Message}");
            return false;
        }
    }
}
