# 07: Copilot usage end to end

**What to build:** A user with a signed-in GitHub Copilot account sees premium-request or seat usage in the tray. The provider finds the existing GitHub token from the CLI configuration or the signed-in `gh` state, and reports missing entitlement separately from missing credentials.

**Blocked by:** 03, 04

**Status:** done

- [x] The GitHub token is discovered from existing CLI state without prompting for a password or opening a browser
- [x] A recorded entitlement payload maps quota and reset information to the snapshot
- [x] A seat without metered credits shows no metered window instead of 0 % used
- [x] A missing or expired token yields authentication-required with the command that fixes it
- [x] Fixture tests cover metered, unmetered, unauthorised, and malformed payloads

## Further notes

- Token resolution order: `GITHUB_TOKEN`, `GH_TOKEN`, then `gh auth token`. The token only ever appears in an Authorization header.
- Unlimited seats and `token_based_billing` responses produce a snapshot with plan identity but no percentage lane, so the tray says "no quota reported" rather than showing a fake 0 %.
- Enterprise hosts are not configurable yet; the public `api.github.com` host is used.
