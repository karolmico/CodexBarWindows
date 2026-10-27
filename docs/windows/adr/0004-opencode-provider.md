# ADR 0004 - One `opencode` provider fed by the Go usage API

Date: 2026-10-27
Status: accepted

## Context

Upstream ships two providers: `opencode` (web console, browser cookies, local history) and `opencodego` (Go subscription meters, API key, Cookie accounts). Both read the same product from different angles, and the base provider only reports Go meters when the workspace has a Go subscription.

The Windows build has no browser-cookie importer, and the six shipped providers already prove the value of keeping each provider to the smallest source that can answer. On this machine OpenCode is signed in through its CLI, which stores an OAuth credential in `opencode.db`; that token authenticates the public Go usage endpoint and returns all three meters.

## Decision

Ship one new provider, key `opencode`, display "OpenCode". It reports the OpenCode Go subscription meters from `GET https://opencode.ai/zen/go/v1/usage`:

- primary: rolling 5-hour percentage,
- secondary: weekly percentage,
- extra lane: monthly percentage.

Credential order is: the key saved in CodexBar (DPAPI), then `OPENCODE_API_KEY`, then the credential the OpenCode CLI already saved (`opencode.db` `credential` rows for `opencode-go`/`opencode`, or the legacy `auth.json` entry). Every source is read-only; a key and the CLI's OAuth access token both work as a bearer token.

`opencodego` is deliberately not registered as a second provider: on a Go account it would duplicate the same three meters under a different name.

## Consequences

- A user who already ran `opencode auth login` gets data with no extra setup; a pasted key is only needed for API-key-only accounts.
- Browser-cookie import, workspace selection, Zen balance, and the local cost-history chart stay out of scope. The tray reports quota, not spend.
- Local SQLite history is not converted into percentages. Its monthly window is an estimate anchored at the first local row, and showing that as quota would be a fabricated limit.
- CLI output labels the source `opencode-api`; the JSON shape matches the other providers.
- Settings files written before this provider existed get it appended with its default enablement, so the provider is not invisible after upgrading.
