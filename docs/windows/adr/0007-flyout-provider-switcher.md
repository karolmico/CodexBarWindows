# ADR 0007 - The flyout is a provider switcher plus one detail pane

Date: 2026-10-27
Status: accepted

## Context

The Windows flyout stacked every enabled provider in one scroll list. With six providers it read as a wall of bars, and it hid which provider a bar belonged to until you read the small heading above it. The macOS build solved this long ago: a row of provider tabs on top, and only the selected provider's quota below. `Sources/CodexBar/Menu/ProviderSwitcherButtons.swift` and `UsageMenuCardHeaderAndUsageSectionView.swift` define that shape, and the user asked this port to match it.

The model had been deliberately one-way: `FlyoutBuilder` projected state into an immutable `FlyoutModel`, and `FlyoutWindow` only rendered it. A tab switcher introduces state that is neither provider data nor settings: which tab the user is reading.

## Decision

- `FlyoutModel` keeps the provider list, and each `FlyoutProviderModel` now also carries what a tab and a header need: a 16x16 path mark, an accent colour, a `FlyoutStatusKind`, a freshness line, and the provider's dashboard/status links.
- The selected tab lives in `FlyoutViewModel` (Core, `INotifyPropertyChanged`), not in the window. `Apply(model)` keeps the selection across refreshes, falls back to the first provider when the selected one was disabled, and honours a toast highlight when one arrives.
- Marks are inline SVG-style path data in `ProviderVisuals`, converted to a `Geometry` in the window. No icon files, no icon font, no dependency on glyph coverage.
- Progress bars are a six-pixel capsule drawn by a `ProgressBar` template, filled with the provider accent, so no default WPF chrome leaks into the design.
- The pace line appears only when a window reports both a duration and a reset instant and between 5% and 95% of that window has elapsed. Outside that band the instant is usually an estimate that slides with every poll, and a pace line there would be noise.
- Dashboard and status destinations mirror the macOS descriptors in `ProviderLinks`.
- There is no cost or token section. This port does not collect cost data for any provider, and an invented figure would be worse than an absent row.

## Consequences

- The flyout is 460 px wide: six tabs with a mark and a name fit at 10 px without truncation.
- A background refresh repaints the same tab; the user's place survives, including while a 1-minute refresh loop runs at high usage.
- Selection is testable without WPF (`FlyoutViewModelTests`), and the projection stays pure (`FlyoutBuilderTests`).
- Providers with a sliding reset estimate show usage and reset copy but no pace verdict.
- Adding a provider now also means giving it a mark, an accent, and its links.
