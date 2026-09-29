# Development

Stack: .NET 10, Windows Forms (tray only, no main window), HidSharp,
Serilog, `System.Diagnostics.PerformanceCounter`, UWP toast toolkit.

## Build

```powershell
# Run (development)
dotnet run --project src/GamingMonitor.csproj

# Publish a self-contained single-file build
dotnet publish src/GamingMonitor.csproj -p:PublishProfile=FolderProfile
```

Output lands in
`src\bin\Release\net10.0-windows10.0.19041.0\publish\win-x64\`.
Exit the running tray instance before rebuilding — it locks the exe.

Zero warnings is the bar (`dotnet build` should report `0 Warning(s)`).

## Releases

Binaries are built by GitHub Actions (`.github/workflows/release.yml`),
triggered **only by `v*` tags** — plain pushes to `main` build nothing:

```powershell
git tag v0.4.2
git push origin v0.4.2
```

The workflow stamps the assembly version from the tag, publishes
self-contained single-file win-x64, zips `GamingMonitor.exe + assets/`,
and creates the Release with notes generated from the commits between
tags. The app's updater picks up exactly that `GamingMonitor-*.zip`.

Bump `<Version>` in `src/GamingMonitor.csproj` to match the tag.

## Troubleshooting (for developers)

See [Troubleshooting](troubleshooting.md) — SmartScreen/SAC,
log locations, icon cache, `update.log`.
