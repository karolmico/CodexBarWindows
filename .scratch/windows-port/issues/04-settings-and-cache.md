# 04: Settings, last-good snapshot cache, and refresh orchestration

**What to build:** Settings survive restarts and a restart shows the previous numbers immediately while a fresh fetch runs. The user can enable, disable, and reorder providers, set the refresh interval floor, and reset settings to defaults. Invalid or older config files are migrated or replaced with a visible warning, never silently dropped.

**Blocked by:** 03

**Status:** done

- [x] Settings persist under `%APPDATA%\CodexBar` with a schema version and survive a round trip
- [x] Provider enablement and order drive registry resolution and menu order
- [x] The last good snapshot per provider is cached and rendered as stale with its age until a refresh succeeds
- [x] A corrupt settings file is backed up, defaults are applied, and the user is told (tray balloon)
- [x] Concurrent refresh requests coalesce: one in-flight fetch per provider, manual refresh included
- [x] Refresh work never blocks the tray thread, and cancellation on exit is clean
- [x] File-contract tests use a temporary directory and never touch the real profile

## Further notes

- The settings dialog (provider checkboxes, move up/down, refresh floor) is implemented but was not driven through UI automation in this remote session; the persistence and ordering contracts behind it are covered by tests.
- A settings file from a newer schema is deliberately left untouched so an older build cannot destroy it.
- Cache writes are best-effort: failing to persist costs only a slower first paint on the next launch.
