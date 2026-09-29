# Architecture

```
src/
  Program.cs               entry point, single-instance mutex, toast routing, updater mode
  AppState.cs              AppState enum, StateSnapshot record
  Monitors/                activity sources + state machine
  Ui/                      tray icon, flyout, icon factory, Win32 interop
  Infrastructure/          settings, logging, toasts, power
  Updating/                version check, self-update applier
```

## State machine (`Monitors/ActivityEngine`)

A worker thread runs the check loop:

1. All monitors are polled **concurrently** (`Task.Run` + `WaitAll`) —
   each one blocks on hardware/counter sampling (~1s), so sequential
   polling would add up.
2. Every source has a hysteresis counter (`Monitors/MonitoredSource`):
   any positive sample resets it to 0, otherwise it climbs (clamped).
   A source counts as active until its counter passes its threshold.
3. The first truly-active source in priority order decides the state:
   gamepad and GPU map to `Gaming`, downloads map to `Downloading`,
   otherwise `Idle`.
4. `SetThreadExecutionState` is called **only on transitions**, never
   every cycle. The first cycle always reports, so startup shows the
   same state toast as any other transition.
5. `StateChanged` is raised on the worker thread; the tray marshals
   UI updates to the UI thread via `SynchronizationContext`.

Default timing: 10s base interval (≈12s effective with sampling),
gamepad exits after 30 quiet cycles (~6 min), downloads and GPU
after 10 (~2 min). All tunable, see [Configuration](configuration.md).

Every ~15 min the engine logs a `[Perf]` line (managed heap + working
set, `Debug` level) so memory growth can be eyeballed over long
sessions. Run with `--debug-logs` for per-cycle details.

State toasts carry a fixed tag, so each new one replaces the previous
instead of piling up in the notification center.

Long absence (`AfkTimeoutMinutes`, default 2h) force-releases `Gaming`
back to `Idle`, even if the GPU still renders something. Input means
gamepad activity plus system-wide keyboard/mouse via `GetLastInputInfo`
(fail-open: a broken API never forces sleep). Downloads stay exempt —
an overnight Steam download is never killed by the AFK timer.

Adding a monitor = a class with `ActiveState` + `CheckActive()` +
`DescribeActive()` (`Monitors/IActivityMonitor`) plus one line
in the engine's source list. The loop itself never changes.

## Monitors

- **DualSenseMonitor** — raw HID input reports over Bluetooth/USB
  (HidSharp): sticks with dead zone, button bitmasks. Triggers, motion
  sensors and touchpad are ignored to avoid false positives.
- **XInputMonitor** — polls XInput slots for Xbox-compatible gamepads
  (buttons, triggers, sticks with dead zones). Degrades silently
  without `xinput1_4.dll`.
- **GpuMonitor** — sums `GPU Engine/*/Utilization Percentage` across
  all engines. Works with any WDDM GPU (NVIDIA / AMD / Intel).
- **NetworkMonitor / ProcessMonitor** — per-process `IO Data Bytes/sec`
  for `steam` and `gamingservicesnet`. Counters are cached and cleaned
  up to avoid handle leaks.
- **GamepadMonitor** — generic `Windows.Gaming.Input` polling for any
  HID game controller not covered above (DualShock 4, Switch Pro,
  F710 in DirectInput mode, sticks). F710 in XInput mode is handled
  by `XInputMonitor` instead.

## UI (`Ui/`)

- **TrayApplicationContext** — notify icon (generated per-state icon
  with a Teams-like presence dot via `TrayIconFactory`), context menu,
  update orchestration, shutdown. No monitoring logic.
- **SettingsForm** — borderless dark flyout near the tray: live GPU
  load, state with trigger reason and duration, threshold slider,
  autostart toggle, version. Auto-closes on outside click
  (low-level mouse hook, `Deactivate` was unreliable).
- **NativeMethods** — all Win32 interop in one place (drag, rounded
  corners, mouse hook, icon handles).
- The app is `PerMonitorV2` DPI-aware (`ApplicationHighDpiMode`);
  layout scales via WinForms autoscaling, corner rounding re-applies
  on resize.

## Updating (`Updating/`)

- `UpdateChecker` queries `releases/latest` on startup (10s timeout,
  silent on failure) and compares tags to the stamped assembly version.
- The update toast's **Download and install** button downloads the
  release zip (streamed to `%TEMP%`), copies the running exe aside as
  an updater, and exits.
- `Updater` (`--apply-update` mode) waits for the main process,
  overwrites the exe and `assets/` **except `settings.json`**,
  restarts with `--updated <tag>` (shows an “Update installed” toast),
  and schedules temp cleanup. Every step is traced to
  `%TEMP%\GamingMonitor\update.log`.
- Downloaded zips carry no Mark-of-the-Web (`HttpClient` doesn't set
  it), so self-updated copies skip SmartScreen friction.
- A named mutex (`Local\GamingMonitor_SingleInstance`) prevents
  double launches.
