using Serilog;
using System.Text;
using Windows.Gaming.Input;

public static class GamePadMonitor
{
    private const double DEADZONE = 0.25;

    public static bool IsGamepadActive()
    {
        var rawControllers = RawGameController.RawGameControllers;

        if (rawControllers.Count == 0)
        {
            Log.Information($"[Gamepad]: No active gamepad found.");
            return false;
        }

        foreach (var controller in rawControllers)
        {
            bool[] buttonArray = new bool[controller.ButtonCount];
            double[] axisArray = new double[controller.AxisCount];
            GameControllerSwitchPosition[] switchArray = new GameControllerSwitchPosition[controller.SwitchCount];

            controller.GetCurrentReading(buttonArray, switchArray, axisArray);

            LogRawData(controller.ButtonCount, buttonArray, controller.AxisCount, axisArray, switchArray);

            // 1. Check button presses
            if (buttonArray.Any(b => b))
            {
                Log.Debug($"[Gamepad]: Button is pressed.");
                return true;
            }

            for (int i = 0; i < controller.AxisCount; i++)
            {
                if (Math.Abs(axisArray[i]) > DEADZONE)
                {
                    Log.Debug($"[Gamepad]: Active on Axis {i}: {axisArray[i]}");
                    return true;
                }
                else
                {
                    Log.Debug($"[Gamepad]: Axis {i} in dead zone: {axisArray[i]}");
                }
            }

            // 3. Check switches (DPAD)
            if (switchArray.Any(s => s != GameControllerSwitchPosition.Center))
            {
                Log.Debug($"[Gamepad]: Switch is pressed.");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Logs all non-zero buttons, axes (outside DEADZONE), and switches.
    /// </summary>
    private static void LogRawData(int buttonCount, bool[] buttonArray, int axisCount, double[] axisArray, GameControllerSwitchPosition[] switchArray)
    {
        var sb = new StringBuilder();

        // Log buttons
        for (int i = 0; i < buttonCount; i++)
        {
            if (buttonArray[i])
            {
                sb.Append($" B{i}:True;");
            }
        }

        // Log axes
        for (int i = 0; i < axisCount; i++)
        {
            if (Math.Abs(axisArray[i]) > DEADZONE)
            {
                sb.Append($" A{i}:{axisArray[i]:F2};");
            }
        }

        // Log switches
        for (int i = 0; i < switchArray.Length; i++)
        {
            if (switchArray[i] != GameControllerSwitchPosition.Center)
            {
                sb.Append($" S{i}:{switchArray[i]};");
            }
        }

        if (sb.Length > 0)
        {
            Log.Debug($"[Gamepad RAW] {sb.ToString()}");
        }
    }
}