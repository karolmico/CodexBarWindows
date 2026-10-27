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

## Amendment - percent basis and disabled lanes

Two problems surfaced once the flyout shipped.

**The same number, two languages.** Antigravity's own window reports *remaining* ("Weekly Limit Remaining 0%"), while CodexBar reported *used* ("100% used"). Both were correct, and the user read the pair as a bug. Rather than a per-provider display setting, every lane now states both: `100% used · 0% left`. The macOS `PercentStyle` choice exists because its layout has room for one figure; here the pair costs a few pixels and removes the ambiguity for every provider at once. The CLI already spoke in `left` terms, so the two surfaces now agree.

**A lane that vanished.** Antigravity marks the five-hour bucket `disabled: true` when the weekly limit is exhausted: it is reported, has no usage value, and the old projection dropped it. The payload also carries the provider's own sentence ("You have hit your weekly limit, the 5-hour limit does not currently apply."), so `RateWindow` gained `IsDisabled` and `DisabledReason`, a disabled lane is never measured (it cannot move a threshold, a pace line, or an exhaustion badge), and the flyout shows it with a muted `disabled` chip, the word `Disabled` instead of a bar, and that sentence underneath. The CLI prints `Disabled` plus the same sentence.
