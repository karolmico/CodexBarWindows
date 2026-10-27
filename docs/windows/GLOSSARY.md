# Glossary — CodexBar Windows

Terms below are the vocabulary used in specs, tickets, code, and tests. Prefer them over synonyms.

## Provider

A metered AI service the app reports on (Codex, Claude, Cursor, Gemini, Copilot, Antigravity). Identified by a `ProviderKey` such as `codex`.

## ProviderKey

Canonical lowercase identifier of a provider (`[a-z0-9][a-z0-9-]*`). Matches upstream provider IDs so configuration and CLI output stay compatible.

## RateWindow

One quota lane reported by a provider. Carries the provider's raw `usedPercent`, an optional duration, and an absolute reset instant. Not normalized: over-quota values are preserved.

## NamedRateWindow

A `RateWindow` with a stable `id` and human `title`, used for model-specific or extra lanes that do not fit session/weekly slots.

## Session window

The short rolling window (typically 5 hours). Not necessarily present; providers may report only a weekly lane.

## Weekly window

The long rolling window (typically 7 days).

## Synthetic placeholder

A window the provider did not actually report but which is needed to keep a layout stable. It has no measured value; `IsMeasured` is false and it must not be rendered as real usage or drive exhaustion.

## UsageSnapshot

The complete report for one provider at one instant: windows, identity, fetch time. Immutable.

## Measured window

A window whose percentage reflects real provider data (not a synthetic placeholder).

## Exhaustion

`usedPercent >= 100` on a measured window. Display projections clamp to 100 %; exhaustion is derived from the raw value.

## Provider identity

Account-level metadata attached to a snapshot: plan name, account label, organization. Never mix identity across providers.

## Stale snapshot

A previously successful snapshot kept for display after a failed refresh. Its error is attached; its age is shown.

## UsageFetchResult

Discriminated result of one provider fetch: `Success(snapshot)` or `Failure(error)`. Providers never throw for expected failures.

## Provider error kind

One of `NotConfigured`, `AuthenticationRequired`, `RateLimited`, `Network`, `Parse`, `Unknown`. Drives user-facing copy and retry behaviour.

## Refresh policy

Pure function deciding the delay until the next fetch from the current state (snapshot age, failure count, highest usage).

## Backoff

Exponential delay after consecutive failures: 30 s, 1 min, 2 min, 4 min, 8 min, capped at 15 min. Reset by a success.

## Flyout

The borderless window opened from the tray icon. Contains provider cards and the footer.

## Tray host

The Windows shell-integration layer that owns the notification-area icon, its menu, and the flyout lifecycle.

## Provider card

The flyout block for one provider: identity, windows, reset countdown, error state.

## Last good snapshot

The most recent successful snapshot for a provider, kept in the cache and used while a refresh is in flight or failing.

## Diagnostics bundle

A redacted text export (versions, enabled provider keys, error kinds, timings) safe to attach to a bug report.
