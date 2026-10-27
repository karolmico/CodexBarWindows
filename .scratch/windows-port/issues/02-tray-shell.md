# 02: Tray shell that runs without a window

**What to build:** A WPF tray application that starts, shows a CodexBar icon in the notification area, opens a context menu, and exits cleanly. No provider data yet: the menu must honestly show that nothing is configured, and the shell must survive repeated open/close cycles and a second launch.

**Blocked by:** 01

**Status:** done

- [x] Launching the app shows one tray icon and no main window (process stays alive without a window; icon visually identified in the hidden-icons flyout)
- [x] The context menu reflects the tray menu model from the core, including the empty state and a disabled refresh while empty
- [x] The tooltip shows the core-generated summary text
- [x] Exit removes the icon and ends the process; no orphan process remains
- [x] The icon renders acceptably on both light and dark taskbars (9 frames, 16–256 px; pixel analysis shows both dark and light strokes)
- [x] A second launch does not create a second icon
- [x] The built executable runs from a clean `dotnet publish` output (verified in ticket 11)

## Further notes

- Left-click, double-click-free tray activation and the provider rows open the flyout; right-click keeps the
  core-rendered context menu. The flyout is a borderless, topmost window placed above the taskbar near
  the pointer, laid out at zero opacity first so its natural height is known before placement.
- `SingleInstance` owns `Local\CodexBar.Windows.SingleInstance` and listens on
  `Local\CodexBar.Windows.Activate`. A second launch signals that event, opens the flyout in the running
  instance, and exits 0 without touching tray state. Verified by launching the built app twice: second
  process exited, first survived, flyout window appeared.
- Closing uses a single-entry guard: `Deactivated` also fires during teardown, and calling `Close()`
  again threw "cannot set Visibility while a window is closing" during the first smoke test.
- The flyout shows per-provider cards (name, plan/account, session/weekly/named bars, reset copy,
  error status, highlight for notification clicks) and a footer with the refresh state.
