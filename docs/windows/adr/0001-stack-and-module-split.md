# ADR 0001 — .NET 9 + WPF tray shell over a platform-neutral core

Date: 2026-10-27
Status: accepted

## Context

CodexBar is a macOS menu bar app with a Swift core and a Qt/Linux shell. A Windows port must deliver the same ambient behaviour (tray icon, flyout, toasts) while reusing the provider semantics already proven on macOS and Linux. The Windows tray surface is small: an icon, a context menu, a borderless flyout, and notifications.

Realistic options:

1. **WinUI 3 (Windows App SDK)** — modern Fluent visuals, native toasts, but heavier deployment (Windows App SDK runtime, MSIX or unpackaged bootstrapper), slower cold start, and a less mature story for notification-area integration.
2. **WPF on .NET 9** — mature, self-contained single-file deployment, excellent tray and toast libraries, `net9.0-windows` compatible; visual polish requires more manual work.
3. **Avalonia** — cross-platform, but the value is cross-platform UI, which is not a requirement, and it would duplicate the existing Qt Linux shell.

## Decision

Build the Windows app as WPF on .NET 9, with `H.NotifyIcon.Wpf` for shell integration, `CommunityToolkit.Mvvm` for view models, and a strict split between `CodexBar.Core` (platform-neutral domain, no WPF) and `CodexBar.Windows` (composition root and all OS integration).

The domain stays in Core so providers, refresh policy, formatting, and the tray menu model are testable without a UI, and so a future CLI shares the exact same behaviour.

## Consequences

- Provider work is testable headlessly; UI work is thin and verified by running the app.
- We accept more manual styling to reach Windows 11 Fluent quality.
- Packaging is a plain `dotnet publish` output plus a per-user installer; no Windows App SDK dependency.
- If Fluent fidelity later becomes a hard requirement, the WPF shell can be replaced without touching Core.
