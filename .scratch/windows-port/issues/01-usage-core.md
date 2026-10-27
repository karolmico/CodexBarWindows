# 01: Platform-neutral usage core with tests

**What to build:** A `CodexBar.Core` library that owns the app's vocabulary so every later slice is testable without Windows: provider keys, rate windows, usage snapshots, fetch results and error kinds, provider registry, refresh policy, usage formatting, and the tray menu/tooltip projection. Include the solution skeleton, pinned SDK, and the test project that exercises those seams.

**Blocked by:** None (can start immediately)

**Status:** done

- [x] A provider key rejects empty, uppercase, and path-like values, and round-trips canonical lowercase ids
- [x] A rate window keeps the provider's raw percentage, clamps only its display projections, and reports exhaustion at 100 or above
- [x] A synthetic placeholder never counts as measured and never drives exhaustion
- [x] Remaining percent is clamped to 0..100 for display and preserves over-quota raw values
- [x] A snapshot exposes its measured windows, the highest used percent, and an updated-at instant
- [x] The registry resolves the configured order, skips unknown keys, and de-duplicates without losing the first position
- [x] The refresh policy returns: immediate when no snapshot exists or it is older than 30 minutes, 5 minutes at normal usage, 2 minutes above 80, 1 minute above 95, and a 30 s..15 min exponential backoff on consecutive failures that a success resets
- [x] Formatting renders percentages with a single rounded integer, countdowns as `2d 4h` / `3h 12m` / `12m` / `<1m` / `now`, and reset copy for unknown reset times
- [x] The tray menu model lists providers in configured order with usage or error state, summarises up to three providers in the tooltip, and shows a refreshing state
- [x] `dotnet build` and `dotnet test` pass from a clean checkout on a machine with only the .NET 9 SDK
