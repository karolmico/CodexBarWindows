# 02: Tray shell that runs without a window

**What to build:** A WPF tray application that starts, shows a CodexBar icon in the notification area, opens a context menu, and exits cleanly. No provider data yet: the menu must honestly show that nothing is configured, and the shell must survive repeated open/close cycles and a second launch.

**Blocked by:** 01

**Status:** in-progress

- [x] Launching the app shows one tray icon and no main window (process stays alive without a window; icon visually identified in the hidden-icons flyout)
- [x] The context menu reflects the tray menu model from the core, including the empty state and a disabled refresh while empty
- [x] The tooltip shows the core-generated summary text
- [x] Exit removes the icon and ends the process; no orphan process remains
- [ ] The icon renders acceptably on both light and dark taskbars
- [ ] A second launch does not create a second icon
- [ ] The built executable runs from a clean `dotnet publish` output
