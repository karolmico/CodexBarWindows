# 15: OpenCode quota end to end

**What to build:** A user signed in to OpenCode (CLI or API key) sees the OpenCode Go 5-hour, weekly and monthly meters in the flyout, the tray menu, and the CLI, without opening a browser.

**Blocked by:** 01, 04

**Status:** done

- [x] `opencode` key is registered in the catalog, settings defaults, CLI, and provider setup
- [x] Usage comes from `GET https://opencode.ai/zen/go/v1/usage` with a bearer token
- [x] Credential order: saved DPAPI key, `OPENCODE_API_KEY`, then the OpenCode CLI store
- [x] CLI store reads `credential` rows from `opencode.db` read-only, with `auth.json` only when that table is absent
- [x] Expired OAuth, duplicate active rows, unreadable payloads, 401/403, 429, 5xx, timeouts and bad JSON each map to fixed copy
- [x] Setup entry offers an API-key field, the `opencode auth login` command, the store path, and a help page
- [x] Fixture tests cover the live API shape, the legacy field names, status errors, and clamping
- [x] Real SQLite tests cover key rows, OAuth rows, expiry, inactive rows, duplicates, and the legacy file
- [x] Live check with this machine's OpenCode CLI login and no browser session

## Further notes

- The live endpoint returns `percent` plus an ISO `resetsAt`; the parser also accepts the older
  `usagePercent`/`resetInSec` fields, so a rolling deploy or an older server still parses.
- Weekly and monthly windows are optional; a malformed optional window is skipped rather than hiding
  the rolling meter.
- Local cost history is not turned into quota percentages (see ADR 0004). It is only read for the
  CLI credential.
- The setup window's "Check connection" runs the same fetch, so it proves both the credential and
  the API path.
