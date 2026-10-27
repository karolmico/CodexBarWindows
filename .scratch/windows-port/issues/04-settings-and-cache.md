# 04: Settings, last-good snapshot cache, and refresh orchestration

**What to build:** Settings survive restarts and a restart shows the previous numbers immediately while a fresh fetch runs. The user can enable, disable, and reorder providers, set the refresh interval floor, and reset settings to defaults. Invalid or older config files are migrated or replaced with a visible warning, never silently dropped.

**Blocked by:** 03

**Status:** ready-for-agent

- [ ] Settings persist under `%APPDATA%\CodexBar` with a schema version and survive a round trip
- [ ] Provider enablement and order drive registry resolution and menu order
- [ ] The last good snapshot per provider is cached and rendered as stale with its age until a refresh succeeds
- [ ] A corrupt settings file is backed up, defaults are applied, and the user is told
- [ ] Concurrent refresh requests coalesce: one in-flight fetch per provider, manual refresh included
- [ ] Refresh work never blocks the tray thread, and cancellation on exit is clean
- [ ] File-contract tests use a temporary directory and never touch the real profile
