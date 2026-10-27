# CodexBar for Windows — Product & Technical Spec

Status: draft for implementation
Scope owner: Dev
Source of truth for upstream behaviour: `Sources/CodexBarCore` (Swift), `Integrations/Linux/DesktopController.cpp`

## Problem Statement

CodexBar shows AI subscription usage (session and weekly windows, credits, reset times) in the macOS menu bar. Windows users who pay for the same plans have no equivalent: they must open each provider's web dashboard, remember different reset rules, and cannot see remaining quota at a glance. A naive port (a window you must keep open) breaks the core value of the product, which is ambient awareness with near-zero interaction cost.

The Linux integration already proves the shape of a non-AppKit port: a small native shell that owns the tray presence, notifications, and settings, delegating provider logic to a shared core. Windows needs the same split in .NET, plus the Windows-specific surfaces (tray, toast, autostart, single instance).

## Solution

A Windows tray application, `CodexBar.Windows`, built on .NET 9 and WPF, that:

- lives in the notification area and never opens a window unless the user clicks the tray icon;
- shows per-provider usage with a compact icon/metric and a flyout with the same information hierarchy as the macOS menu;
- reads credentials from the same local files the CLI tools already write (`~/.codex/auth.json`, `~/.claude/.credentials.json`, …), so no separate sign-in is required when the user is already logged in;
- refreshes adaptively (faster near exhaustion, exponential backoff on failure) and survives sleep/resume, offline, and provider outages without blocking the tray;
- raises Windows toast notifications on configurable thresholds and reset events;
- starts with Windows (opt-in) and enforces a single instance;
- exposes the same data through `codexbar usage --format json` for scripting.

Provider coverage is phased. The first release matches the six providers the Linux build exposes: `codex`, `claude`, `cursor`, `gemini`, `copilot`, `antigravity`. `opencode` was added afterwards from the OpenCode Go usage API (ADR 0004). The architecture is provider-agnostic so further providers are additive.

## User Stories

1. As a Windows user with a ChatGPT/Codex subscription, I want to see my remaining session and weekly quota in the tray, so that I know whether I can start a long task now.
2. As a user, I want the tray tooltip to summarize my active providers without opening a window, so that reading usage costs one hover.
3. As a user, I want to open a flyout by clicking the tray icon, so that I can see per-provider detail on demand.
4. As a user, I want the flyout to stay open while I read it and close when I click elsewhere, so that it behaves like a native Windows flyout.
5. As a user, I want a visible "Refreshing…" state and a last-updated timestamp, so that I can trust the numbers are current.
6. As a user, I want to refresh all providers manually from the flyout or the tray menu, so that I can force fresh data after switching accounts.
7. As a user, I want to refresh a single provider, so that a slow provider does not delay the others.
8. As a user, I want a provider that fails to load to show a clear error with a retry action while other providers keep working, so that one outage does not blind me.
9. As a user, I want an explicit "not signed in" state that points at the CLI command to run, so that I can fix authentication without guessing.
10. As a user, I want usage numbers to distinguish session, weekly, and model-specific windows, so that I do not confuse a 5-hour limit with a weekly one.
11. As a user, I want reset times shown as relative countdowns with the absolute time on hover, so that planning is quick.
12. As a user, I want a warning when a window is close to exhaustion, so that I can slow down before hitting a hard limit.
13. As a user, I want a toast notification when a window resets, so that I know capacity is available again.
14. As a user, I want to configure notification thresholds and turn them off, so that the app does not become noise.
15. As a user, I want to choose which providers are shown, in which order, so that the tray reflects what I pay for.
16. As a user, I want my settings to persist across restarts, so that I configure the app once.
17. As a user, I want the app to start with Windows only if I opt in, so that it is not installed behind my back.
18. As a user, I want a single running instance, so that two icons or two refresh loops never appear.
19. As a user, I want the app to recover gracefully from sleep or lost network, so that a stale snapshot is marked stale instead of silently wrong.
20. As a user, I want the app to remember the last good snapshot across restarts, so that I see data immediately while the first refresh runs.
21. As a user, I want my credentials to stay where the CLIs put them, so that I never paste a token into a third-party app.
22. As a user, I want the app to never log tokens, cookies, or emails it does not need, so that local logs are safe to share in a bug report.
23. As a user, I want the tray icon to be legible on light and dark taskbars, so that it fits Windows 11.
24. As a user, I want keyboard access to the flyout (open, navigate, close, refresh), so that I can use the app without a mouse.
25. As a user, I want a "copy diagnostics" action, so that I can report a broken provider with the information a maintainer needs.
26. As a user, I want the app to respect Windows display scaling, so that the flyout and icon are not blurry on high-DPI screens.
27. As a user, I want provider dashboards one click away, so that I can act on a limit.
28. As a developer, I want the same usage semantics as the macOS and Linux builds, so that support answers apply across platforms.
29. As a developer, I want a headless CLI over the same core, so that behaviour can be verified without UI automation.
30. As a developer, I want provider logic to be pure and testable against recorded fixtures, so that a provider schema change fails a unit test rather than an ad-hoc manual check.
31. As a user, I want the app to be installable without an admin prompt, so that I can use it on a locked-down machine.
32. As a user, I want an uninstall that removes autostart and settings, so that nothing is left behind.

## Implementation Decisions

### Stack and process

- .NET 9 SDK pinned through `global.json`; WPF on `net9.0-windows`; no WinUI 3 (packaging and self-contained deployment cost outweighs the design benefit for a tray-only surface).
- Tray integration via `H.NotifyIcon.Wpf` (Win32 `Shell_NotifyIcon` underneath), not a custom P/Invoke layer.
- MVVM with `CommunityToolkit.Mvvm`; views stay thin, all decisions live in view models or the core.
- One solution at `Windows/CodexBar.sln`, with projects under `Windows/src/` and `Windows/tests/`; the existing Swift tree is untouched.

### Module boundaries

- `CodexBar.Core` — platform-neutral domain: provider keys, rate windows, snapshots, fetch contracts, refresh policy, formatting, persistence contracts, tray menu model, CLI shaping, diagnostics. No WPF, no Win32, no ambient clock or filesystem.
- `CodexBar.Core.Providers.*` — one namespace per provider, each an `IUsageProvider` implementation composed of a credential locator, an HTTP/CLI probe, and a response mapper. Providers never touch the UI.
- `CodexBar.Providers.Windows` — the concrete provider catalog shared by the tray and the CLI, plus the WMI/TCP-table Antigravity process source.
- `CodexBar.Windows` — composition root: tray host, flyout window, settings window, toasts, autostart, single-instance, clipboard, logging. Depends on Core and the Windows provider catalog.
- `CodexBar.Cli` — headless `codexbar.exe` over the same core: `usage --format text|json`, `--provider`, and `0/1/2/64` exit codes.

### Domain semantics (mirrors upstream, deliberately)

- A `RateWindow` carries `usedPercent` (provider value, not normalized), an optional measured title, a window duration, an absolute reset instant, and a synthetic-placeholder flag. Display projections clamp; the stored value does not.
- A `UsageSnapshot` carries up to two named windows (session, weekly) plus extra named windows, provider identity, and the fetch instant. Exhaustion is `usedPercent >= 100` on a measured window.
- Errors are values, not exceptions: `NotConfigured`, `AuthenticationRequired`, `RateLimited` (with retry-after), `Network`, `Parse`, `Unknown`. A failed provider must not clear the last good snapshot; it marks it stale with the error attached.
- Refresh policy is a pure function of (now, snapshot age, consecutive failures, highest used percent): base 5 minutes, 2 minutes above 80 %, 1 minute above 95 %, failure backoff 30 s doubling to 15 minutes, immediate refresh when there is no snapshot or the snapshot is older than 30 minutes (sleep/resume).

### Data sources (Windows)

- Codex: `%CODEX_HOME%\auth.json`, falling back to `%USERPROFILE%\.codex\auth.json`; `POST/GET` the ChatGPT backend usage endpoint with the stored bearer token and account header.
- Claude: `%USERPROFILE%\.claude\.credentials.json` and the Anthropic OAuth usage endpoint.
- Cursor, Gemini, Copilot, Antigravity: the same credential files/cookies upstream uses, adapted to Windows paths; no browser cookie import in the first release unless a provider has no file-based source. Antigravity also reads quota through the `agy` CLI print report, so it works without the desktop app running (ADR 0003).
- Credentials are read-only from the app's point of view: the app never writes provider credential files.

### Persistence and lifecycle

- Settings and the last snapshot cache live under `%APPDATA%\CodexBar` as JSON with an explicit schema version.
- Single instance via a named mutex; a second launch activates the existing instance's flyout.
- Autostart uses the per-user `Run` key, never a machine-wide service.
- Logging is a rolling local file under `%APPDATA%\CodexBar\logs` with redaction of tokens, cookies, emails, and paths; "copy diagnostics" bundles versions, settings shape, enabled provider ids, error kinds, and timings only.

### UI

- Flyout: per-provider card with provider name, plan/identity, session and weekly bars with percentage, reset countdown, and error/empty states; footer with last-updated, refresh, settings.
- Tray tooltip summarizes up to three providers (`Codex 42% · Claude 87%`) and the refresh state.
- Icon: template icon generated from the upstream brand mark, rendered per Windows light/dark taskbar.
- Toasts: threshold crossings and resets only, rate-limited to one per provider per window.

### Testing seams (agreed before implementation)

- `IUsageProvider.FetchAsync` — the only provider seam. Fixture-driven HTTP tests live behind a stub transport; no test touches a real account.
- `RefreshPolicy.NextDelay` — pure function, the only place refresh timing is decided.
- `TrayMenuBuilder.Build` — pure menu/tooltip projection, so tray behaviour is testable without Win32.
- `UsageFormatter` — pure presentation of percentages and countdowns.
- `SettingsStore` + `SnapshotCache` — file contracts tested against a temporary directory.
- `CliPayloadBuilder` + `CliTextRenderer` — golden files pin the JSON and text contract.
- `LogRedactor`, `RollingLogStore`, `DiagnosticsBundle` — redaction, rotation, and bundle shape tested without a real desktop.
- WPF windows are not unit-tested; they are verified by running the built app and, once providers exist, by a smoke launch that asserts the process stays alive.

## Testing Decisions

- Tests assert external behaviour at the seams above; no test reaches into private helpers or asserts on WPF controls.
- Expected values come from worked examples and recorded provider payloads, never recomputed the way the implementation does.
- Each provider ships with recorded fixtures for: healthy response, exhausted window, missing window, malformed payload, and auth failure.
- Refresh timing is covered by table-driven tests over the policy matrix, including sleep/resume and backoff caps.
- No test may read the developer's real credentials, hit the network, or emit a keychain/browser prompt.

## Out of Scope

- macOS/Linux parity for providers beyond the six listed.
- Browser cookie import and multi-account switching (planned follow-up, mirrors upstream's account switcher).
- Widgets, cost/usage history charts, spend analytics, and the plugin system.
- Code signing, store distribution, and auto-update.
- macOS or Linux packaging changes.

## Further Notes

- Upstream is the behavioural reference; when its semantics and this document disagree, upstream wins and this document is corrected.
- The Linux controller is the closest prior art for tray/menu/notification wiring and should be read before implementing UI behaviour.
- Provider HTTP calls must be cancellable and must never block the UI thread; the tray shows the previous snapshot while a refresh is in flight.
