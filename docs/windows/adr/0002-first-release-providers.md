# ADR 0002 — First-release provider set is the Linux parity set

Date: 2026-10-27
Status: accepted

## Context

Upstream registers well over a hundred providers. Porting them all before shipping would delay the first usable Windows build and multiply unverifiable surface area. The Linux integration already made this call and exposes: `codex`, `claude`, `cursor`, `gemini`, `copilot`, `antigravity`.

## Decision

The first Windows release implements the same six providers, in this order: `codex`, `claude`, `gemini`, `copilot`, `cursor`, `antigravity`. The registry, settings schema, CLI output, and UI are provider-agnostic so additional providers are additive work, not rework.

Ordering is driven by user value: the two subscription products the maintainers use daily first, then the providers whose data sources are stable file-based credentials. `antigravity` is last because it depends on a local language-server probe rather than a documented endpoint.

## Consequences

- Users of other providers see no Windows tray support in the first release; the settings UI must say so instead of failing silently.
- Verification stays tractable: six fixture suites, six credential paths.
- Adding a provider later means a new `IUsageProvider` plus fixtures, with no changes to refresh or UI code.
