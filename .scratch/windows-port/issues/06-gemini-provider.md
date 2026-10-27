# 06: Gemini usage end to end

**What to build:** A user signed in with the Gemini CLI sees their quota lanes in the flyout with reset countdowns. The provider reuses the CLI's stored OAuth credentials, refreshes an expired access token when the provider allows it, and otherwise reports a clear authentication error.

**Blocked by:** 03, 04

**Status:** ready-for-agent

- [ ] Stored Gemini CLI credentials are located on Windows and never written by the app
- [ ] Recorded payloads map the reported quota lanes and reset instants
- [ ] An expired access token is reported as authentication-required with actionable copy, or refreshed through the same mechanism the CLI uses
- [ ] A payload without quota data yields "not reported" rather than a fabricated zero
- [ ] Fixture tests cover healthy, expired-token, empty, and malformed payloads
