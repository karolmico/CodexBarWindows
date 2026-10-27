# 05: Claude usage end to end

**What to build:** A user signed in through the Claude CLI sees session, weekly, and scoped limits in the flyout, with model-specific lanes where the provider reports them, plus reset countdowns and plan identity. Auth and expired-token states explain the CLI command that fixes them.

**Blocked by:** 03, 04

**Status:** ready-for-agent

- [ ] Credentials are read from the per-user Claude configuration without modifying it
- [ ] Recorded payloads map session, weekly, and extra named windows including their titles
- [ ] A scoped or model-specific limit appears as a named window instead of overwriting the weekly window
- [ ] An expired token yields an authentication error and does not clear the last good snapshot
- [ ] Identity (plan and account label) comes only from the Claude payload
- [ ] Fixture tests cover healthy, exhausted, missing-lane, malformed, and auth-failure payloads
