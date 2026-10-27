# 12: Headless CLI over the same core

**What to build:** `codexbar usage --format json` prints the same usage data the tray shows, with stable field names compatible with the macOS CLI, so scripts and Windows automation can consume it. Text output mirrors the upstream CLI's summary lines.

**Blocked by:** 04

**Status:** done

- [x] JSON output includes provider id, windows, percentages, reset instants, identity, and error kind, with stable camelCase names
- [x] Text output mirrors upstream's summary layout closely enough for existing scripts
- [x] `--provider` selects one provider; the default is all enabled providers
- [x] A failing provider appears as an error entry without failing the whole command unless requested
- [x] Exit codes distinguish success, partial failure, and total failure
- [x] Golden-file tests cover JSON and text output for two providers and one error

## What shipped

- New `CodexBar.Cli` console project (`codexbar.exe`) whose shaping lives in
  `CodexBar.Core.Cli`, so everything below is unit-tested without a real provider.
- `usage` is the default command. Options: `--format text|json`, `--json`, `--pretty`,
  `-p/--provider` (repeatable or comma-separated), `--require-all`, `-h`, `-V`.
- JSON is a top-level array of provider payloads, matching upstream's shape:
  `provider`, `account`, `version`, `source`, `usage` (`primary`, `secondary`,
  `extraRateWindows`, `updatedAt`, `identity`), `rateWindowLabels`,
  `error` (`code`, `message`, `kind`, `providerErrorKind`). Missing fields are omitted,
  not faked; timestamps are UTC ISO-8601 with whole seconds.
- `identity` maps this port's plan/account/organization onto `plan`, `account`, and
  `organization`. `version` stays null because provider CLI versions are not probed here.
- Text mirrors upstream: `== Codex (codex-cli) ==`, `Session: 58% left [======------]`,
  `Resets in 3h`, `Weekly: 39% left [====--------]`, then `Account`, `Plan`, and
  `Organization`. Remaining quota is printed (as upstream does), with `<1% left` below one
  percent. Synthetic placeholders are skipped; unknown named lanes print `Unavailable`.
- Exit codes: `0` all requested providers succeeded, `1` partial failure, `2` total failure
  or no provider selected, `64` bad arguments. `--require-all` promotes any failure to `2`.
  A provider that throws becomes an `Unknown` error entry; the run continues.
- Provider wiring was extracted into `CodexBar.Providers.Windows` so the tray and CLI share
  one catalog. Both publish into the same folder (`CodexBar.Windows.exe` + `codexbar.exe`).
- Golden fixtures: `Fixtures/Cli/usage.json` and `usage.txt` cover Codex, Claude, and a
  Gemini `notConfigured` error. Verified against the published `codexbar.exe`.

## Verification

- `dotnet test CodexBar.sln`: 313 passing (313 total).
- Published `codexbar.exe` from the local-disk build: `--provider codex --format json`
  returned one error entry and exit `2`; `--version` printed `codexbar 0.1.0`.
- Live credentials are absent on this machine, so the success path is covered by golden
  fixtures and provider unit tests, not by a live account.
