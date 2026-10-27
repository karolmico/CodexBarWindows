# 07: Copilot usage end to end

**What to build:** A user with a signed-in GitHub Copilot account sees premium-request or seat usage in the flyout. The provider finds the existing GitHub token from the CLI configuration or the signed-in `gh` state, and reports missing entitlement separately from missing credentials.

**Blocked by:** 03, 04

**Status:** ready-for-agent

- [ ] The GitHub token is discovered from existing CLI state without prompting for a password or opening a browser
- [ ] A recorded entitlement payload maps quota and reset information to the snapshot
- [ ] A seat without metered credits shows a clear "no metered quota" state rather than 0 % used
- [ ] A missing or expired token yields authentication-required with the command that fixes it
- [ ] Fixture tests cover metered, unmetered, unauthorised, and malformed payloads
