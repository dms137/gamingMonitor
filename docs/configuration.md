# Configuration

Everything lives in `assets\settings.json` next to the exe and is
re-read every check cycle, so hand edits apply **without a restart**.
Closing the flyout re-saves the file with the in-memory values,
so prefer editing while the app is stopped.

| Key | Default | Range | Meaning |
|---|---|---|---|
| `GpuThresholdPercent` | 25 | 5–90 | GPU load % treated as gaming (also the flyout slider) |
| `ShowStateNotifications` | true | — | State-change toasts on/off (update toasts always show) |
| `SkippedUpdateVersion` | "" | — | Release tag skipped via the toast button; empty clears the skip |
| `ShowStateNotifications` | true | — | State-change toasts on/off (update toasts always show) |
| `CheckIntervalMs` | 10000 | 2000–60000 | Delay between checks |
| `GamepadInactivityCycles` | 30 | 1–600 | Quiet cycles before the gamepad drops out (~6 min) |
| `DownloadInactivityCycles` | 10 | 1–600 | Same for downloads (~2 min) |
| `GpuInactivityCycles` | 10 | 1–600 | Same for GPU (~2 min) |
| `AfkTimeoutMinutes` | 120 | 0–720, 0 = Never | No keyboard/mouse/gamepad input for this long forces `Gaming` back to `Idle` (downloads stay exempt). Also the flyout slider (1–6h + Never) |
| `DownloadProcesses` | `["steam", "gamingservicesnet"]` | — | Process names watched for download activity |

Missing keys fall back to defaults, so old files keep working.

Tuning advice: desktop compositing and browsers idle around a few
percent of GPU; real games sit at 40–100%. If the monitor never
idles, raise the threshold; if light 2D games don't trigger it,
lower it — the flyout shows live load next to the slider.
