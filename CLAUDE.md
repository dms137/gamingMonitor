# GamingMonitor — working notes

Windows tray app (.NET 10, WinForms, no main window) that holds
`SetThreadExecutionState` while the user games or downloads games.
~3.4k lines of C#, no tests. User-facing docs live in `docs/` —
keep them in sync when behavior changes.

## Build / run

```powershell
dotnet build GamingMonitor.sln          # bar: 0 warnings
dotnet run --project src/GamingMonitor.csproj -- --debug-logs
dotnet publish src/GamingMonitor.csproj -p:PublishProfile=FolderProfile
```

- Exit the running tray instance before rebuilding (it locks the exe;
  single-instance mutex `Local\GamingMonitor_SingleInstance` makes a
  second launch exit silently — "nothing happens" usually means this).
- Debug logs: `src\bin\Debug\net10.0-windows10.0.19041.0\logs\monitor_log<date>.txt`.
  Updater trace: `%TEMP%\GamingMonitor\update.log`.
- CI = only `release.yml`, triggered by a `v*` tag (stamps the version from
  the tag, audits packages, publishes the zip). Nothing builds on push, and
  there is no winget publishing — the in-app auto-update is the channel.
  Also bump `<Version>` in `src/GamingMonitor.csproj`.
- `TODO.md` is local-only (untracked) — the user's own notes; it can be stale.

## Map

| File | Role |
|---|---|
| `src/Program.cs` | entry: `--apply-update` helper mode, mutex, `--updated`, `--debug-logs`, toast activation routing (`action=` args) |
| `src/Monitors/ActivityEngine.cs` | worker thread loop, state resolution, AFK timeout, shutdown-after-download state machine |
| `src/Monitors/MonitoredSource.cs` | exit hysteresis per source (counter 0 = just active; active while `counter <= threshold`; starts inactive) |
| `src/Monitors/IActivityMonitor.cs` | `ActiveState`, `CheckActive()`, `DescribeActive()`, optional `IsDefinitivelyInactive()` |
| `src/Monitors/GpuMonitor.cs` | sums **all** `GPU Engine` counters; top-PID → friendly name (`FileDescription → ProductName → window title → exe`) |
| `src/Monitors/NetworkMonitor.cs` + `ProcessMonitor.cs` | "downloads" = per-process `IO Data Bytes/sec` (disk+network!) for `DownloadProcesses`, >1.25 MB/s |
| `src/Monitors/DualSenseMonitor.cs` | HidSharp, reads ONE raw report per cycle, fixed byte offsets |
| `src/Monitors/XInputMonitor.cs` | `XInputGetState` snapshot per slot |
| `src/Monitors/GamepadMonitor.cs` | `Windows.Gaming.Input.RawGameController` snapshot |
| `src/Infrastructure/AppSettings.cs` | static settings, `assets\settings.json` next to exe, `Reload()` every cycle unless `_dirty` |
| `src/Infrastructure/PowerManagement.cs` | `SetThreadExecutionState` — per-thread API, called from the engine thread |
| `src/Infrastructure/NotificationHelper.cs` | all toasts; tags `gm-state`, `gm-shutdown` |
| `src/Ui/TrayApplicationContext.cs` | tray icon/menu, marshals `StateChanged` to UI via `SynchronizationContext`, download-update orchestration, `Shutdown()` |
| `src/Ui/SettingsForm.cs` | borderless flyout, LL mouse hook closes on outside click, autostart via HKCU Run, saves settings on close |
| `src/Updating/UpdateChecker.cs` / `Updater.cs` | GitHub `releases/latest`, zip `GamingMonitor-*.zip`; updater = copy of exe in `%TEMP%`, keeps `settings.json` |

## Engine cycle (every `CheckIntervalMs`, default 10s, ≈12s effective)

1. `AppSettings.Reload()`, push thresholds into sources.
2. All monitors run in parallel (`Task.Run`, `WaitAll` 30s timeout).
3. Source priority: DualSense, XInput, RawGamepad, GPU, Download →
   first `IsTrulyActive` wins. Gamepad/GPU → `Gaming`, download → `Downloading`.
4. Gamepad-driven Gaming shows the GPU top process name if GPU is active.
5. AFK (`GetLastInputInfo` + last gamepad activity) forces Gaming → Idle.
6. Power + `StateChanged` only on transitions (and on the first cycle).
   Power requests are released by the worker itself when it stops.
7. Shutdown: `Downloading → Idle` arms grace; a state change, or keyboard/mouse
   input in 2 separate cycles, aborts it (each hit is logged); then a 60s
   countdown toast updated in place (only Cancel or new activity aborts) →
   `shutdown /s /t 5`. Shutdown state is guarded by `_shutdownLock`
   (toast Cancel arrives on another thread).

Timers use `Environment.TickCount64` (monotonic). `AppSettings` is guarded by
a static lock; `Save()` writes `settings.json.tmp` and swaps it in.

User decisions: leave the download/disk monitors alone (they work in
practice); `GamepadMonitor` "connected = active" gap is deferred.

## Conventions

- Logging: Serilog static `Log`, message prefix tags like `[State]`, `[GPU Monitor]`, `[Shutdown]`.
- Allman braces, `_camelCase` fields, file-scoped namespace before usings.
- Fail-open everywhere: a broken API must never force sleep/shutdown; catch-and-log, never crash the worker.
- New monitor = class implementing `IActivityMonitor` + one `MonitoredSource` in `ActivityEngine`.
