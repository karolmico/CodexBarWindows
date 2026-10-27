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

Packaging must build on a local disk or redirect the intermediate output:

```powershell
$local = "$env:LOCALAPPDATA\CodexBar\publish\obj\"
dotnet publish src/CodexBar.Windows -c Release -p:UseAppHost=true `
  -p:BaseIntermediateOutputPath=$local -o "$env:LOCALAPPDATA\CodexBar\publish\app"
```

`bin/`, `obj/`, and publish output are git-ignored.

## Layout

| Path | Purpose |
| --- | --- |
| `Windows/src/CodexBar.Core` | Platform-neutral domain: provider keys, rate windows, snapshots, fetch contracts, refresh policy, formatting, tray menu model |
| `Windows/src/CodexBar.Windows` | WPF composition root: tray host, flyout, settings, notifications |
| `Windows/tests/CodexBar.Core.Tests` | xUnit tests for the core seams |
| `docs/windows/SPEC.md` | Product and technical specification |
| `docs/windows/GLOSSARY.md` | Domain vocabulary |
| `docs/windows/adr` | Architecture decision records |
| `.scratch/windows-port/issues` | Tracer-bullet tickets and blocking edges |

## Conventions

- Tests assert observable behaviour at the agreed seams; no test reads real credentials,
  hits the network, or opens a browser/keychain prompt.
- Domain code takes time and paths as parameters instead of reading ambient state, so the
  refresh matrix and file contracts are testable.
- `dotnet build` treats warnings as errors (`Windows/Directory.Build.props`).
