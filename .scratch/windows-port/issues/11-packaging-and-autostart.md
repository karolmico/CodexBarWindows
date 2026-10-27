# 11: Packaging, autostart, and single instance

**What to build:** The app installs per user without elevation, starts with Windows when the user opts in, keeps one instance, and uninstalls cleanly. A published build runs on a machine with no .NET SDK and no administrator rights.

**Blocked by:** 10

**Status:** ready-for-agent

- [ ] `dotnet publish` produces a self-contained Windows x64 build that launches from a clean folder
- [ ] The per-user installer creates Start Menu and uninstall entries without an elevation prompt
- [ ] Autostart writes and removes a per-user Run entry only, matching the settings toggle
- [ ] A second launch activates the existing instance instead of starting another
- [ ] Uninstall removes autostart and leaves user settings unless the user opts to delete them
- [ ] The app starts minimised to tray when launched by autostart
- [ ] Published binaries are excluded from the repository
