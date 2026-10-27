# 08: Cursor usage end to end

**What to build:** A user signed in to the Cursor desktop app sees plan usage, included-request consumption, and the reset date in the flyout. Because Cursor authenticates through a web session rather than a documented token file, this ticket also covers reading the local session state safely and failing closed when it is absent.

**Blocked by:** 03, 04

**Status:** done

- [x] The local Cursor session is located without decrypting or copying unrelated browser data
- [x] A recorded usage-summary payload maps plan, included usage, and reset date
- [x] An expired session yields authentication-required and does not break other providers
- [x] A missing Cursor installation shows "not configured" rather than an error
- [x] Fixture tests cover healthy, expired-session, no-installation, and malformed payloads

## Further notes

- The only local read is the `cursorAuth/accessToken` row of Cursor's VS Code state database
  (`%APPDATA%\Cursor\User\globalStorage\state.vscdb`), opened read-only through `Microsoft.Data.Sqlite`.
  No browser cookie store is touched.
- Fallback: the `cursor-agent` CLI auth file (`%USERPROFILE%\.cursor\auth.json`, then
  `%APPDATA%\cursor\auth.json`).
- The access token is a JWT; its `sub`/`exp` decide usability and its payload supplies the account
  email when `/api/auth/me` is unavailable. Tokens are never logged, cached, or written.
- A `cursor-safe-storage:` token is encrypted with OS protection the app does not own, so it fails
  closed and points at `cursor-agent login` instead of guessing a decryption.
- Lanes: plan total percentage (or `used / limit`), Auto as secondary, API as an extra window.
  Legacy request plans use `/api/usage?user=<id>` and report `used / limit requests`.
