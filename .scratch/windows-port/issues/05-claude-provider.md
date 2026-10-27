# 05: Claude usage end to end

**What to build:** A user signed in through the Claude CLI sees session, weekly, and scoped limits in the tray, with model-specific lanes where the provider reports them, plus reset countdowns and plan identity. Auth and expired-token states explain the CLI command that fixes them.

**Blocked by:** 03, 04

**Status:** done

- [x] Credentials are read from the per-user Claude configuration without modifying it
- [x] Recorded payloads map session, weekly, and extra named windows including their titles
- [x] A scoped or model-specific limit appears as a named window instead of overwriting the weekly window
- [x] An expired token yields an authentication error and does not clear the last good snapshot
- [ ] Identity (plan and account label) comes only from the Claude payload — the usage payload carries no plan, and the profile enrichment endpoint is not implemented yet
- [x] Fixture tests cover healthy, exhausted, missing-lane, malformed, and auth-failure payloads

## Further notes

- Credential resolution honours `CLAUDE_CONFIG_DIR` and falls back to `%USERPROFILE%\.claude\.credentials.json`.
- An expired `claudeAiOauth.expiresAt` is reported as authentication-required; the app never refreshes or writes credentials.
- Scoped windows without `utilization` (reset metadata only) are omitted rather than shown as 0% used.
- Plan/account enrichment via `/api/oauth/profile` is a follow-up; it needs its own fixture and must never mix identity across providers.
