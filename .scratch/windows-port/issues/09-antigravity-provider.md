# 09: Antigravity usage end to end

**What to build:** A user running the Antigravity client sees its model quota in the flyout. Because the data comes from a local language-server probe rather than a documented endpoint, the provider must isolate the probe, time out cleanly, and degrade to "not running" when the client is closed.

**Blocked by:** 03, 04

**Status:** done

- [x] The local Antigravity process or its probe socket is discovered without hardcoded user paths
- [x] A recorded probe response maps model quota lanes into the snapshot
- [x] A closed client yields "not running" and retries on the normal policy, without log spam
- [x] A probe that exceeds its timeout is cancelled and reported as a network/timeout error
- [x] Fixture tests cover healthy, not-running, timeout, and malformed responses

## Further notes

- Discovery mirrors upstream: WMI (`Win32_Process.CommandLine`) finds a `language_server` whose
  command line names Antigravity (app or IDE) or the `agy`/`antigravity-cli` CLI. The CSRF token and
  `--extension_server_port` come from that command line; loopback listeners come from the IP helper
  TCP table, filtered to the owning PID. No user path is hardcoded.
- The probe POSTs JSON (Connect protocol) to
  `/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary` on `127.0.0.1`, sending
  `X-Codeium-Csrf-Token` when the server needs one, then reads `GetUserStatus` for identity.
- Lanes map by family and cadence: Gemini (session 5-hour / weekly) becomes the primary lane,
  Claude/GPT the secondary, remaining buckets become named windows. Buckets with no
  `remainingFraction` are `usageKnown: false`, never a fabricated 0 %.
- Every attempt has its own 5 s timeout; a timeout is a network error and the normal backoff applies.
  A closed client (no matching process) is `NotConfigured`, so the tray says "not running".
- Encrypted local app sessions are not decrypted here: the quota comes from the running language
  server, so the app's own storage is never opened.
