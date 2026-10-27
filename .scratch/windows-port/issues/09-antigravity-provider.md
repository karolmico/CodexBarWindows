# 09: Antigravity usage end to end

**What to build:** A user running the Antigravity client sees its model quota in the flyout. Because the data comes from a local language-server probe rather than a documented endpoint, the provider must isolate the probe, time out cleanly, and degrade to "not running" when the client is closed.

**Blocked by:** 03, 04

**Status:** ready-for-agent

- [ ] The local Antigravity process or its probe socket is discovered without hardcoded user paths
- [ ] A recorded probe response maps model quota lanes into the snapshot
- [ ] A closed client yields "not running" and retries on the normal policy, without log spam
- [ ] A probe that exceeds its timeout is cancelled and reported as a network/timeout error
- [ ] Fixture tests cover healthy, not-running, timeout, and malformed responses
