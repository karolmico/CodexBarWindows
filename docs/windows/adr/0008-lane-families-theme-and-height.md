# ADR 0008 - Lane families, a theme switch, and a self-sizing flyout

Date: 2026-10-27
Status: accepted

## Context

Three follow-ups landed together after the flyout redesign:

1. Antigravity reports its lanes in families ("Gemini Models", "Claude and GPT models", "Gemini 4 Argon"), but the flyout flattened them and labelled the top two generically ("Session", "Weekly"), so a Claude/GPT weekly read as if it were a Gemini figure.
2. The app was hard-coded light while the user runs Windows in dark mode.
3. The detail pane had a fixed `MaxHeight="430"`, so even a short account showed a scrollbar well before the screen ran out of room.

## Decision

- `RateWindow` carries `Group` and `Title`; the Antigravity parsers stamp each lane with its family and a within-family label ("weekly", "5-hour"). `FlyoutBuilder` orders lanes by family, puts a header on the first lane of each family, and leaves group-less providers untouched. `GroupHeader` is derived, not stored, so the model stays a projection.
- Theme is an `AppTheme` enum (System/Light/Dark) on `AppSettings`. `ThemeManager` resolves System from `AppsUseLightTheme`, swaps one of two `ResourceDictionary` palettes into the application resources, and the flyout references every colour through `DynamicResource`, so a change repaints live. The Settings window gains the picker; the two dialogs keep WPF's default chrome for now.
- The flyout returns to `SizeToContent="Height"` with the detail row as a `*` row, and `MaxHeight` is set from `WorkArea.Height` when it opens. The window grows to its content and only scrolls once the content exceeds the work area.

## Consequences

- Antigravity lanes now read exactly like the native window: a family header, then its weekly and five-hour lanes, including a disabled lane with the provider's explanation.
- A first launch follows the Windows theme without any action; the choice is remembered and the flyout repaints immediately.
- Short accounts show no scrollbar; long ones scroll only inside the detail pane, keeping the menus reachable.
- Provider accents and status-dot colours are shared across themes and stay readable on both palettes.
