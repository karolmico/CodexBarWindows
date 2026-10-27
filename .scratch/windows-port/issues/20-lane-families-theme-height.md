# 20: Lane families, dark/system theme, and dynamic flyout height

**What to build:** Three polish items the user asked for after the flyout redesign.

**Blocked by:** 19

**Status:** done

## Grouping

- [x] `RateWindow.Group` and `RateWindow.Title`; Antigravity lanes carry their family and a within-family label
- [x] The flyout orders lanes by family and renders one header per family ("Gemini Models", "Claude and GPT models", "Gemini 4 Argon"), so a Claude/GPT weekly no longer reads as Gemini
- [x] Providers without groups keep their flat "Session"/"Weekly" layout

## Theme

- [x] `AppTheme` (System/Light/Dark) on `AppSettings`, persisted as `theme`
- [x] `ThemeManager` resolves System from `AppsUseLightTheme` and swaps a light/dark palette
- [x] The flyout references every colour through `DynamicResource`, so a change repaints live
- [x] Theme picker in Settings; a first launch follows Windows
- [ ] Settings/ProviderSetup dialogs keep WPF default chrome (follow-up)

## Dynamic height

- [x] `SizeToContent="Height"` + a `*` detail row; `MaxHeight` derived from `WorkArea.Height`
- [x] No scrollbar on a short account; the detail pane scrolls only when content exceeds the screen

## Not changed

Provider accents and status-dot colours are shared across themes and verified readable on both palettes.
