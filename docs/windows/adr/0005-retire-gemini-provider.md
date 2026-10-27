# ADR 0005 - Retire the Gemini CLI provider

Date: 2026-10-27
Status: accepted

## Context

The `gemini` provider read `%USERPROFILE%\.gemini\oauth_creds.json` and asked Google's Code Assist endpoints for quota. Google has since withdrawn individual-account support from `@google/gemini-cli`: the CLI now answers that this client is no longer supported and points users to Antigravity.

That leaves the provider able to return only an authentication error for the accounts it was built for, while Antigravity already reports the Gemini model lanes (5-hour and weekly) from the same Google account. Keeping a permanently broken tile adds a failed fetch every refresh and makes the tray look broken.

## Decision

Remove the `gemini` provider from this build: key, provider implementation, credential reader, usage client, setup entry, CLI source label, tests, and fixtures. Settings files that still list `gemini` keep the entry as an unknown key (it is preserved, ignored by the registry, and never shown), so downgrading does not silently reorder anything.

## Consequences

- Gemini quota remains available through the Antigravity provider, including its per-model weekly lanes.
- A user who relied on Gemini CLI alone sees five providers instead of six and a settings window with no dead entry.
- The CLI help and provider setup no longer mention `gemini`; `codexbar usage --provider gemini` fails as an unknown provider.
- No migration writes to the user's settings file; the stale preference disappears the next time settings are saved from the app.
