# Troubleshooting

## Windows blocks the app

- **SmartScreen** (“Windows protected your PC”): `More info` →
  `Run anyway`. One-time per file; reputation builds over time.
- **Smart App Control** (silent block, `0x800711C7` / “Application Control
  policy has blocked this file” in the Application event log): no per-app
  bypass exists. Turning it off is a one-way door — it cannot be
  re-enabled without reinstalling Windows.

## Logs

- App log: `logs\monitor_log.txt` next to the exe (daily rolling,
  `Information` and above).
- Updater trace: `%TEMP%\GamingMonitor\update.log` — every self-update
  step, including why a file copy was skipped.

## Common issues

- **Stale tray icon after starting**: Explorer caches exe icons.
  `ie4uinit.exe -show` + F5 in the folder, or wait a bit.
- **Build fails with locked exe**: Exit the tray instance first
  (it locks `bin\...\GamingMonitor.exe` while running).
- **No toast on state change**: check `Show state notifications`
  in the flyout; update toasts always show regardless.
- **Monitor never idles**: GPU threshold too low for the machine —
  watch live load in the flyout and raise it (see
  [Configuration](configuration.md)).
