# GamingMonitor

> [!WARNING]
> This project was vibe-coded from start to finish. It works on the author's
> machine, but expect quirks, hardcoded thresholds, and minimal error handling.
> Review the code before trusting it with your power settings.

A lightweight Windows system-tray utility that keeps your PC awake while you are gaming,
watching rendering workloads, or downloading games — and lets it sleep otherwise.

## How it works

Every 10 seconds the monitor checks three activity sources and picks a power state.
Short pauses are smoothed out with hysteresis counters, so a loading screen or a
brief network dip does not immediately drop the system back to idle.

| State | Trigger | Display sleep | System sleep |
|---|---|---|---|
| `Gaming` | Gamepad input (DualSense via HID) or GPU load > 25% | Blocked | Blocked |
| `Downloading` | Steam / Xbox download rate > ~10 Mbps | Allowed | Blocked |
| `Idle` | None of the above | Allowed | Allowed |

Power requests are applied through `SetThreadExecutionState` and only when the
resulting state actually changes, so the Windows API is not spammed.

## Monitors

- **DualSenseMonitor** — reads raw HID input reports (sticks with dead zone,
  button bitmasks) over Bluetooth/USB via HidSharp. Ignores triggers, motion
  sensors and touchpad to avoid false positives.
- **GpuMonitor** — sums `GPU Engine/*/Utilization Percentage` performance
  counters across all engines. Threshold is 25% to stay clear of desktop
  compositing and browser noise. Works with any WDDM GPU (NVIDIA / AMD / Intel).
- **NetworkMonitor / ProcessMonitor** — watches per-process `IO Data Bytes/sec`
  for `steam` and `gamingservicesnet` (Xbox). Instance list is fetched once per
  cycle and performance counters are cached to avoid handle leaks.

The tray icon shows the current state, sends a toast notification on every
state change, and logs to `logs/monitor_log.txt` (Serilog, daily rolling).

## Requirements

- Windows 10 version 1709+ / Windows 11 (WDDM driver for GPU monitoring)
- No .NET runtime needed for release builds — they are self-contained single-file

## Installation

1. Download `GamingMonitor-vX.Y.Z-win-x64.zip` from
   [Releases](https://github.com/dms137/gamingMonitor/releases).
2. Extract the archive preserving the folder structure
   (for example into `C:\Tools\GamingMonitor\`), so `assets\GM.ico`
   stays next to `GamingMonitor.exe`.
3. Run `GamingMonitor.exe`. For autostart, tick `Start with Windows`
   in the settings flyout (left-click the tray icon).

## Build from source

```powershell
# Run (development)
dotnet run

# Publish a self-contained single-file build
dotnet publish GamingMonitor.csproj -p:PublishProfile=FolderProfile
```

Output lands in `bin\Release\net8.0-windows10.0.19041.0\publish\win-x64\`.

## Tech stack

- .NET 8, Windows Forms (system tray, no main window)
- HidSharp (DualSense HID), Serilog (file logging)
- `System.Diagnostics.PerformanceCounter` (GPU + download monitoring)
- Windows Toast notifications (`Microsoft.Toolkit.Uwp.Notifications`)
