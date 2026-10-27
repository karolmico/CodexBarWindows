# 08: Cursor usage end to end

**What to build:** A user signed in to the Cursor desktop app sees plan usage, included-request consumption, and the reset date in the flyout. Because Cursor authenticates through a web session rather than a documented token file, this ticket also covers reading the local session state safely and failing closed when it is absent.

**Blocked by:** 03, 04

**Status:** ready-for-agent

- [ ] The local Cursor session is located without decrypting or copying unrelated browser data
- [ ] A recorded usage-summary payload maps plan, included usage, and reset date
- [ ] An expired session yields authentication-required and does not break other providers
- [ ] A missing Cursor installation shows "not configured" rather than an error
- [ ] Fixture tests cover healthy, expired-session, no-installation, and malformed payloads
