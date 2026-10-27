# 03: Codex usage end to end

**What to build:** A user with the Codex CLI already signed in launches the app and immediately sees session and weekly usage, reset countdowns, and the plan in the tray flyout. The provider reads the existing credential file, calls the ChatGPT backend usage endpoint, maps the response into the core snapshot, and surfaces auth or network failures as provider errors without losing the last good snapshot.

**Blocked by:** 01, 02

**Status:** ready-for-agent

- [ ] Credentials are located in `%CODEX_HOME%` when set and otherwise in the per-user Codex home, and the token is never logged
- [ ] A recorded healthy payload maps session, weekly, and extra windows with correct percentages, reset instants, and plan
- [ ] An exhausted payload marks the provider exhausted and the flyout shows it
- [ ] A missing window degrades to "not reported" rather than zero percent
- [ ] A malformed payload yields a parse error, not a crash
- [ ] A 401 yields an authentication error with the CLI sign-in guidance; a transport failure yields a network error and retry
- [ ] Changing `%CODEX_HOME%` or the account header is respected on the next refresh
- [ ] Fixture tests cover all of the above without network access
