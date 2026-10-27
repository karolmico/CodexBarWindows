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

## CLI fallback (ticket 14)

- With the desktop app closed, quota comes from `agy -p "/usage" --output-format json
  --print-timeout 90s` (upstream's print report). `agy` 1.1.11 or newer is required; older builds
  get a fixed "too old" message.
- The CLI runs hidden with stdin closed, `AGY_CLI_DISABLE_AUTO_UPDATE=true`, a 60 s timeout that
  kills the process tree, and `--version` cached once per app run.
- The app probe still wins when it answers, because it also yields account email and plan. A failed
  or untokened app probe now falls through to the CLI instead of failing the whole provider.
- Hand-started `agy` sessions are no longer probed over loopback: every CLI from 1.2.2 rejects
  tokenless requests, so the probe would only burn a timeout.
- Lang mapping is shared with the HTTP path (`AntigravityQuotaSnapshot`), so both sources report
  the same lanes. CLI stderr is classified, never surfaced.
- Verified live on this machine with `agy` 1.4.8 and no Antigravity process running:
  `codexbar.exe usage --provider antigravity` returned Gemini 5-hour 22.05 %, Claude/GPT weekly
  100 %, Gemini weekly 9.06 %.
