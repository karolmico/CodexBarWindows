# 18: Flyout redesign with a provider switcher

**What to build:** A flyout that matches the macOS menu: provider tabs on top, one provider's quota below, modern capsule bars, and a compact action menu.

**Blocked by:** 12

**Status:** done

- [x] Horizontal provider switcher with a mark, name, and health dot; the selected tab is an accent pill
- [x] Clicking a tab switches the detail pane; Left/Right arrows move between tabs
- [x] Selection survives a background refresh and falls back when the open provider is disabled
- [x] Header with the provider name, freshness line, account, and plan badge
- [x] Flat 6 px capsule progress bars in the provider accent, usage and reset on one row
- [x] Pace line when the window can be paced, silent for sliding estimates
- [x] Quick actions: configure, usage dashboard, status page (only when the provider has them)
- [x] App menu: Settings, About CodexBar, Quit, plus copy diagnostics and refresh in the footer
- [x] Rounded 16 px surface, 1 px border, drop shadow, Segoe UI Variable with Segoe UI fallback
- [x] Empty state when no provider is enabled
- [x] Full suite green

## Not built

Cost and token rows. No provider client in this port collects cost data, so the section is omitted rather than filled with a placeholder (ADR 0007).

## Review aid

`C:\Users\Karol\AppData\Local\Temp\opencode\flyoutshot` renders the window to PNG with representative data, so the layout can be reviewed without a live account.
