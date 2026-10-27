# 13: Diagnostics, logging, and copy-diagnostics

**What to build:** When a provider misbehaves, the user can copy a redacted diagnostics bundle and attach it to a bug report. Logs are local, rolling, and free of tokens, cookies, emails, and full file paths.

**Blocked by:** 03

**Status:** done

- [x] Logs roll at a size cap and older files are pruned
- [x] Tokens, cookies, authorisation headers, and account emails are redacted before writing
- [x] The diagnostics bundle contains versions, enabled provider keys, last error kinds, timings, and settings shape only
- [x] Copy diagnostics puts the bundle on the clipboard and confirms visibly
- [x] An unexpected exception in a provider is contained, logged, and surfaced as an unknown error without killing the app
- [x] Redaction is unit-tested with representative secret-shaped strings

## What shipped

- `RollingLogStore` writes `%APPDATA%\CodexBar\logs\codexbar.log` with a 512 KB cap and
  three rotations (`.1`–`.3`); older files are pruned. IO failures are swallowed so logging
  can never take the app down.
- `LogRedactor` removes `Authorization`/`Cookie` headers, bearer/basic tokens, JSON and
  query-string secrets, JWTs, `ghp_`/`sk-`/`xoxb-`/AWS-shaped keys, emails, Windows/UNC
  paths, and Unix home directories. Redaction runs on every log line and again on the final
  diagnostics JSON as a safety net. Tests cover each shape plus idempotence and safe text.
- `DiagnosticsBundle` emits schema 1 with app/runtime/OS versions, settings shape
  (refresh floor, thresholds, reset toggle, quiet hours, autostart, enabled keys), and one
  entry per provider: enabled flag, `ok`/`error`/`noData`, camelCase error kind, last update,
  measured window count, last fetch milliseconds, and fetch instant. It never includes
  account labels, identity, error messages, tokens, or paths.
- `DiagnosticsService` writes `%APPDATA%\CodexBar\diagnostics-latest.json` and retries the
  clipboard a few times. The flyout's "Copy diagnostics" button shows an inline confirmation
  (4 s) with a failure variant; Settings has the same action behind a MessageBox confirmation.
- The tray logs startup, settings warnings, every fetch outcome with its duration, clipboard
  results, unhandled dispatcher exceptions, and exit. `UsageCoordinator` records timings and
  converts provider exceptions into `Unknown` errors; both paths are unit-tested.

## Verification

- `dotnet test CodexBar.sln`: 313 passing, including redaction, rotation, and bundle-shape
  suites.
- End-to-end on the running tray: invoking "Copy diagnostics" through UI Automation produced
  the inline message and wrote `diagnostics-latest.json` (about 2.2 KB) with the expected
  versions, settings shape, provider keys, error kinds, and timings.
- This remote session blocks clipboard access for every process (`Get-Clipboard` fails too),
  so the run exercised the honest fallback message. Clipboard success is the same
  `Clipboard.SetText` path and needs a normal interactive desktop to observe.
