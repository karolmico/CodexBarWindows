# Windows port — development notes

## Prerequisites

- .NET 9 SDK (`Microsoft.DotNet.SDK.9`; the solution pins `9.0.320` through `global.json`)
- Windows 10 1809 or newer

## Build and test

```powershell
cd Windows
dotnet build CodexBar.sln
dotnet test CodexBar.sln
```

Run the tray app (no window is created, look in the notification area):

```powershell
cd Windows
dotnet run --project src/CodexBar.Windows
```

Run the headless CLI over the same provider catalog:

```powershell
cd Windows
dotnet run --project src/CodexBar.Cli -- usage
dotnet run --project src/CodexBar.Cli -- usage --format json --pretty
dotnet run --project src/CodexBar.Cli -- usage --provider codex --format json
```

`--provider` is repeatable (or comma-separated); the default is every provider enabled in
`settings.json`. A partial failure still prints every provider and exits `1`; total failure exits
`2`; bad arguments exit `64`. Add `--require-all` when any gap must fail the run.

Regenerate the application icon from the upstream brand mark:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Windows\tools\generate-icon.ps1
```

## Repository location caveat

This checkout lives on a virtual drive (`G:\Mój dysk\…`) that cannot create apphost
resources: the .NET SDK's `CreateAppHost` task writes the executable through a
memory-mapped file and fails with `IOException` on that filesystem. The Windows project
therefore sets `UseAppHost=false`, which is transparent for `dotnet build`, `dotnet test`,
and `dotnet run`.

Packaging must build on a local disk. A single redirected `BaseIntermediateOutputPath` is not
enough because it is shared by the referenced `CodexBar.Core` project, so publish from a local
scratch copy instead:

```powershell
$repo = (Get-Location).Path
$work = "$env:LOCALAPPDATA\CodexBar\build\src"
$out  = "$env:LOCALAPPDATA\CodexBar\publish\win-x64"
robocopy $repo $work /E /XD .git bin obj .scratch TestResults /NFL /NDL /NJH /NJS
dotnet publish "$work\Windows\src\CodexBar.Windows\CodexBar.Windows.csproj" `
  -c Release -r win-x64 --self-contained true -p:UseAppHost=true -o $out
dotnet publish "$work\Windows\src\CodexBar.Cli\CodexBar.Cli.csproj" `
  -c Release -r win-x64 --self-contained true -p:UseAppHost=true -o $out
```

Both publishes target the same folder, so the installed tree contains `CodexBar.Windows.exe`
(tray) and `codexbar.exe` (CLI). Each keeps its own `.deps.json` and `.runtimeconfig.json`.

Install and uninstall per user, without elevation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File packaging\install.ps1 -Source $out
powershell -NoProfile -ExecutionPolicy Bypass `
  -File "$env:LOCALAPPDATA\Programs\CodexBar\uninstall.ps1" [-RemoveSettings]
```

The installer writes only to `%LOCALAPPDATA%`, the per-user Start Menu, and `HKCU`. Autostart is
owned by the app's Settings toggle, which writes/removes
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexBar` and passes `--autostart` so the
launch stays invisible. Uninstall keeps `%APPDATA%\CodexBar` unless `-RemoveSettings` is passed.

`bin/`, `obj/`, `publish/`, and `dist/` are git-ignored.

## Diagnostics

The tray writes a redacted rolling log to `%APPDATA%\CodexBar\logs\codexbar.log`
(512 KB cap, three rotated files). Tokens, cookies, `Authorization` headers, emails, and file
paths are removed before a line is written.

"Copy diagnostics" in the flyout (and in Settings) builds
`%APPDATA%\CodexBar\diagnostics-latest.json` and copies it to the clipboard. The bundle holds the
app/runtime/OS versions, settings shape, enabled provider keys, per-provider state and error kind,
and last fetch timings — never accounts, messages, tokens, or paths. The flyout shows an inline
confirmation; if the clipboard is unavailable it says so and points at the saved file.

## Layout

| Path | Purpose |
| --- | --- |
| `Windows/src/CodexBar.Core` | Platform-neutral domain: provider keys, rate windows, snapshots, fetch contracts, refresh policy, formatting, tray menu model, CLI shaping, diagnostics |
| `Windows/src/CodexBar.Providers.Windows` | Concrete Windows provider catalog shared by the tray and the CLI (WMI + TCP table for Antigravity) |
| `Windows/src/CodexBar.Windows` | WPF composition root: tray host, flyout, settings, notifications, clipboard |
| `Windows/src/CodexBar.Cli` | `codexbar.exe` console host over the shared core |
| `Windows/tests/CodexBar.Core.Tests` | xUnit tests for the core seams, including CLI golden files |
| `docs/windows/SPEC.md` | Product and technical specification |
| `docs/windows/GLOSSARY.md` | Domain vocabulary |
| `docs/windows/adr` | Architecture decision records |
| `.scratch/windows-port/issues` | Tracer-bullet tickets and blocking edges |

## Provider dependencies

| Package | Where | Why |
| --- | --- | --- |
| `Microsoft.Data.Sqlite` | `CodexBar.Core` | Read-only access to Cursor's `cursorAuth/accessToken` row |
| `System.Management` | `CodexBar.Providers.Windows` | WMI command lines for Antigravity process discovery |
| `H.NotifyIcon.Wpf` | `CodexBar.Windows` | Tray icon and native menu |

Antigravity loopback ports come from `GetExtendedTcpTable` (IP helper) rather than a subprocess.


## Conventions

- Tests assert observable behaviour at the agreed seams; no test reads real credentials,
  hits the network, or opens a browser/keychain prompt.
- Domain code takes time and paths as parameters instead of reading ambient state, so the
  refresh matrix and file contracts are testable.
- `dotnet build` treats warnings as errors (`Windows/Directory.Build.props`).
