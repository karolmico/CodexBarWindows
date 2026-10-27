# 14: Antigravity quota through the `agy` CLI

**What to build:** A user who signed in to the Antigravity CLI sees their Gemini and Claude/GPT quota with no Antigravity desktop app running. The desktop probe stays the first choice when the app is open, because it also reports account and plan.

**Blocked by:** 09

**Status:** done

- [x] The CLI source runs `agy -p "/usage" --output-format json --print-timeout 90s`, hidden, with stdin closed
- [x] Both sources map through one lane builder, so lane names and windows match
- [x] App running and healthy wins; closed or failing app falls through to the CLI
- [x] Missing `agy` says so instead of surfacing a probe failure
- [x] CLI older than 1.1.11, signed-out CLI, network failure, timeout and malformed JSON each map to a fixed message
- [x] `--version` runs once per app run; every run is bounded and kills its process tree on timeout
- [x] Fixture tests cover a real report shape, no usable quota, auth failure, network failure, timeout, old version and fallback order
- [x] Live check with `agy` 1.4.8 and the desktop app closed

## Further notes

- `AgyCliUsageClient` is the seam: `IAgyProcessRunner` is faked in tests and implemented on Windows by `WindowsAgyProcessRunner`.
- `AgyBinaryLocator` checks `PATH` first, then `%LOCALAPPDATA%\agy\bin`, `%LOCALAPPDATA%\Programs\agy` and `%USERPROFILE%\.local\bin`.
- The CLI report carries no identity, so the tray shows quota without an account row on this path.
