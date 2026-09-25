namespace GamingMonitor.Infrastructure;

using Serilog;
using System.Runtime.InteropServices;
public static partial class PowerManagement
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    [LibraryImport("Kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint esFlags);

    private static uint _currentExecutionState = ES_CONTINUOUS;

    /// <summary>
    /// Sets or clears the display required requirement, ONLY IF the state changes.
    /// </summary>
    public static void SetDisplayRequired(bool required)
    {
        SetExecutionState(required, ES_DISPLAY_REQUIRED, "Display");
    }

    /// <summary>
    /// Sets or clears the system sleep prevention requirement, ONLY IF the state changes.
    /// </summary>
    public static void SetSystemRequired(bool required)
    {
        SetExecutionState(required, ES_SYSTEM_REQUIRED, "System Sleep");
    }

    private static void SetExecutionState(bool required, uint flagToChange, string logMessage)
    {
        uint newState = _currentExecutionState;

        if (required)
        {
            newState |= flagToChange;
        }
        else
        {
            newState &= ~flagToChange;
        }

        if (newState != _currentExecutionState)
        {
            _currentExecutionState = newState;
            SetThreadExecutionState(_currentExecutionState);
            Log.Information($"[Power] {logMessage} Required: {required}. API call made. State: 0x{_currentExecutionState.ToString("X")}");
        }
        else
        {
            // State unchanged, skipping API call
        }
    }
}