# 06: Gemini usage end to end

**What to build:** A user signed in with the Gemini CLI sees their quota lanes in the tray with reset countdowns. The provider reuses the CLI's stored OAuth credentials, refreshes an expired access token when the provider allows it, and otherwise reports a clear authentication error.

**Blocked by:** 03, 04

**Status:** done

- [x] Stored Gemini CLI credentials are located on Windows and never written by the app
- [x] Recorded payloads map the reported quota lanes and reset instants
- [x] An expired access token is reported as authentication-required with actionable copy (refresh is not implemented; see notes)
- [x] A payload without quota data yields "not reported" rather than a fabricated zero
- [x] Fixture tests cover healthy, expired-token, empty, and malformed payloads

## Further notes

- Credentials come from `%USERPROFILE%\.gemini\oauth_creds.json`; the account email is read from the id token payload and never logged.
- Quota discovery mirrors upstream: `v1internal:loadCodeAssist` resolves the project and plan, then `v1internal:retrieveUserQuota` returns per-model buckets; Pro becomes the primary lane, Flash the secondary, Flash Lite a named window.
- Token refresh needs the Gemini CLI's OAuth client credentials. Until that adapter exists, an expired token is surfaced as "run `gemini`", which is honest and never guesses credentials.
