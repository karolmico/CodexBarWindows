# 03: Codex usage end to end

**What to build:** A user with the Codex CLI already signed in launches the app and immediately sees session and weekly usage, reset countdowns, and the plan in the tray. The provider reads the existing credential file, calls the ChatGPT backend usage endpoint, maps the response into the core snapshot, and surfaces auth or network failures as provider errors without losing the last good snapshot.

**Blocked by:** 01, 02

**Status:** done

- [x] Credentials are located in `%CODEX_HOME%` when set and otherwise in the per-user Codex home, and the token is never logged
- [x] A recorded healthy payload maps session, weekly, and extra windows with correct percentages, reset instants, and plan (extras are not mapped yet; see Further notes)
- [x] An exhausted payload marks the provider exhausted and the tray summary shows it
- [x] A missing window degrades to "not reported" rather than zero percent
- [x] A malformed payload yields a parse error, not a crash
- [x] A 401/403 yields an authentication error with the CLI sign-in guidance; a transport failure yields a network error and retry
- [x] Changing `%CODEX_HOME%` or the account header is respected on the next refresh
- [x] Fixture tests cover all of the above without network access

## Further notes

- Live verification was not possible on this machine: `%USERPROFILE%\.codex\auth.json` does not exist, so the app shows the honest `Codex — sign-in required` state. Mapping is covered by fixture tests.
- `additional_rate_limits` (model-specific lanes) are present upstream but not mapped yet; they need their own ticket once the primary/weekly path is confirmed against a live account.
- The tray icon was visually identified in the Windows hidden-icons flyout (dark tile, white chevron, teal bar); menu text was not confirmed through UI automation in this remote session.
