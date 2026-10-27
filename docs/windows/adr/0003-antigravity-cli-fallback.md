# ADR 0003 - Antigravity falls back to the `agy` CLI print report

Date: 2026-10-27
Status: accepted

## Context

The Antigravity integration reads quota from the app's local language server over loopback, authenticated with the CSRF token from the server's command line. That only works while the desktop app (or the IDE extension) is running and has opened its port. It also stops working once the CLI's own language server is CSRF-gated: upstream notes that every `agy` from 1.2.2 rejects tokenless local requests, so a hand-started CLI session cannot be probed.

Upstream solves this with `agy -p "/usage" --output-format json --print-timeout 90s`, a one-shot report that never opens the desktop app. On this machine `agy` 1.4.8 is installed at `%LOCALAPPDATA%\agy\bin\agy.exe` and returns a complete JSON quota document, so the Windows port can rely on the same contract.

## Decision

`AntigravityProvider` probes the app/IDE language server when one is running with a CSRF token, and falls back to `AgyCliUsageClient` when the app is closed, untokened, or the probe fails. The CLI is only usable from 1.1.11 (upstream's first JSON report); older builds produce a fixed "too old" error instead of a confusing parse failure.

`agy` is run as a hidden child process with closed stdin, `AGY_CLI_DISABLE_AUTO_UPDATE=true`, and a 60 s timeout that kills the whole process tree. Both sources map through one shared lane builder, so Gemini/Claude+GPT lane assignment cannot drift.

Hand-started `agy` processes are no longer probed over loopback: since 1.2.2 that request can only fail, and failing it would spend a timeout per refresh before reaching the report that works.

## Consequences

- The tray shows Antigravity quota with no Antigravity desktop window open, which is the normal state for CLI users.
- A refresh that needs the CLI costs one process start (about 3.5 s here) and a `--version` probe once per app run.
- The CLI report carries no account email or plan, so those rows stay empty on this path; the app probe still supplies them when it runs.
- CLI stderr can contain sign-in URLs and proxy details, so it is classified into fixed copy and never shown or logged.
