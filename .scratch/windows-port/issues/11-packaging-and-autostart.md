# 11: Packaging, autostart, and single instance

**What to build:** The app installs per user without elevation, starts with Windows when the user opts in, keeps one instance, and uninstalls cleanly. A published build runs on a machine with no .NET SDK and no administrator rights.

**Blocked by:** 10

**Status:** done

- [x] `dotnet publish` produces a self-contained Windows x64 build that launches from a clean folder
- [x] The per-user installer creates Start Menu and uninstall entries without an elevation prompt
- [x] Autostart writes and removes a per-user Run entry only, matching the settings toggle
- [x] A second launch activates the existing instance instead of starting another
- [x] Uninstall removes autostart and leaves user settings unless the user opts to delete them
- [x] The app starts minimised to tray when launched by autostart
- [x] Published binaries are excluded from the repository

## Verification

- Published from a local scratch copy (`%LOCALAPPDATA%\CodexBar\build\src`) with
  `-c Release -r win-x64 --self-contained true -p:UseAppHost=true`; the resulting
  `CodexBar.Windows.exe` launched and stayed alive from a clean output folder.
- `packaging/install.ps1 -Source <publish>` created `%LOCALAPPDATA%\Programs\CodexBar`,
  the Start Menu shortcut, and the `HKCU\...\Uninstall\CodexBar` entry with an
  `UninstallString`; no elevation prompt.
- Autostart: with `startWithWindows: true` the app wrote
  `"…\CodexBar.Windows.exe" --autostart` to `HKCU\...\Run\CodexBar`; flipping the setting off
  removed it. `--autostart` launches stay invisible and defer the first refresh by four seconds.
- `uninstall.ps1` removed the install directory, shortcut, uninstall key, and Run value while
  keeping `%APPDATA%\CodexBar`; `-RemoveSettings` also deleted the settings directory.
- Single instance is covered by ticket 02 and uses the same code in the published build.

## Further notes

- A single redirected `BaseIntermediateOutputPath` cannot be used from the `G:` checkout: it is
  shared by `CodexBar.Core` and produced `NETSDK1005`. Packaging therefore copies the sources to
  local disk first (documented in `docs/windows/DEVELOPMENT.md`).
- The installer deliberately does not touch autostart; the app's Settings toggle is the single
  source of truth, so uninstall and reinstall cannot leave a stale entry behind.
