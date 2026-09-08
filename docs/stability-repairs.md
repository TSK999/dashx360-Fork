# Dashboard stability repairs

This branch addresses the twelve findings from the September 2026 code review. The Windows interface stays in the existing WPF project; reusable persistence, validation, layout, scanning and worker lifecycle rules now live in `DashX360.Core`.

| Review finding | Changed behavior |
| --- | --- |
| R1: unrelated processes could be selected for closing | Capture processes present before launch; require a new process with the expected executable or Steam installation directory; bind tracking and force confirmation to PID and start time. Query the native process image during startup. Browser processes remain outside force-close tracking. |
| R2: partial backups could erase missing sections | Preserve omitted sections and fields, distinguish empty arrays from null, and reject unsupported versions or malformed entries before publishing changes. |
| R3: minimize removed bindings and left image caches stale | Suspend image values without replacing their bindings, refresh bindings on restore, invalidate remembered background paths, and recreate previews. Remove forced garbage collection and working-set trimming. |
| R4: custom app tiles overlapped | Give every tile a distinct cell, extend the canvas for large libraries, and scroll focused tiles into view. |
| R5: unsafe theme paths and payloads | Reject traversal, rooted paths, reserved Windows names and linked destination folders. Validate supported image formats and dimensions. Create themes in generated folders and reject duplicate display names. |
| R6: persistence could be partially overwritten | Publish JSON with a flushed temporary file and atomic rename; serialize writes per data root. Stage imports and preserve originals in a recovery journal, with a separate portable safety backup. Recover interrupted imports before loading the dashboard. |
| R7: installed applications wrote beside the executable | Default to a per-user data directory; put logs and mutable assets there. Keep explicit portable and custom-directory options, migrate existing data, and prevent multiple instances from sharing the same data directory. |
| R8: corrupt Steam caches aborted requests | Treat invalid or unreadable cache files as misses; publish cache updates atomically and preserve cancellation. |
| R9: Steam data mixed accounts and truncated friends | Scope personal caches by SteamID64, use only the matching local Steam account for playtime, request friend summaries in batches of 100, and deduplicate by source and stable ID. Protect API keys with Windows DPAPI and migrate legacy plaintext configuration. |
| R10: scans blocked the interface and rebuilt collections repeatedly | Run discovery off the UI thread, support progress and cancellation, skip inaccessible/reparse directories, scan Steam into a snapshot, merge results on the UI thread, and refresh derived lists after each batch. Bound artwork requests to four concurrent downloads per game. |
| R11: replaced settings were no longer observed | Rebind the window's subscription when a new settings object is loaded and detach the previous object. |
| R12: audio workers could overlap after stop/restart | Each capture generation owns its cancellation token; a replacement waits for its predecessor to finish cleanup. Dispose dashboard timers and media resources, and allow in-flight guide operations to release their gates safely. |

Additional changes include 16 ms controller polling, lazy DirectInput initialization, serialized input disposal, observed command/save errors, and version 2 backups containing dashboard volume, local friends and game artwork. Version 1 backups remain accepted. Existing disabled online integrations remain disabled.

## Build and verification

Install the .NET SDK selected by `global.json`. On Windows:

```powershell
dotnet build XboxMetroLauncher.csproj -c Release
dotnet run --project tests/DashX360.CoreTests -c Release
dotnet run --project tests/DashX360.WindowsTests -c Release
dotnet publish XboxMetroLauncher.csproj -c Release --no-build -o Build/publish
```

The core harness also runs on Linux. Both harnesses exit nonzero when a check fails and print the individual results. They deliberately use no test-framework package dependencies. GitHub Actions runs the core checks on Ubuntu, builds and checks WPF on Windows, verifies publishing, and keeps the Windows publish output as an artifact for seven days.

The regression checks cover atomic writes, concurrent readers, cancellation, rollback with injected failure, persisted-journal recovery, path containment, partial/malformed backups, settings replacement, real WPF bindings, portable artwork, account-scoped Steam caching, encrypted credentials, complete friend batching, process rejection and force confirmation, directory ownership, large app layouts, recursive scanning and audio restart serialization. Steam tests use a fake HTTP transport. Process tests only launch and terminate their own child test executable.

## Data and compatibility

The default data folder is `%LOCALAPPDATA%\DashX360\UserData`. Logs are in its `Logs` subfolder. Mutable themes, music folders and managed artwork are under its `Assets` subfolder. Bundled assets are still read from the application directory.

`--portable` selects the application's `UserData` directory. `--userdata PATH`, `--user-data PATH`, and `DASHX360_USER_DATA_FOLDER` retain explicit overrides; the environment variable takes precedence. These locations must be writable. Migration preserves the original files. Keep the whole application folder if using portable mode.

Backups have a 256 MB limit. Embedded images must be supported raster formats, no larger than 18 MB each, at most 8192 pixels on either edge and at most 32 million pixels per frame. Export validates these limits so its output can be imported. Credentials are intentionally excluded from portable backups. Safety backups are retained in `Backups` after successful imports and their path is returned after a failed commit where recovery may be needed.

Unverified game processes are left running. Games whose launchers run outside their recorded installation directory may require selecting the correct executable or installation path. Scan traversal skips linked directories to avoid cycles and unsafe destinations.

## Manual acceptance checks

Before merging, exercise the published build on a Windows desktop:

1. Start from a protected installation folder and verify migration, ordinary launch, portable mode and custom data-folder overrides.
2. Repeatedly minimize/restore on 16:9 and ultrawide displays with game details and custom themes visible.
3. Navigate more than ten custom apps using keyboard, mouse, XInput and DirectInput devices.
4. Launch real direct-executable and Steam games, including launchers and elevated games; verify graceful close and explicit force confirmation.
5. Start/cancel large library scans and rapidly open/close the guide and music visualizer while audio devices change.

Automated checks do not measure frame times, GPU behavior, live Steam privacy settings, controller hardware, or native audio-device behavior. These checks are listed as manual validation, not claimed as completed.
