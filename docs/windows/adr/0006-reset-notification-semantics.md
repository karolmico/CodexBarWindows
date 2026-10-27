# ADR 0006 - A reset needs an elapsed window, not a moved timestamp

Date: 2026-10-27
Status: accepted

## Context

The notification decider treated any forward move of a window's reset instant as a reset. That held for providers that report a fixed cycle boundary, but not for ones that recompute the boundary on every read.

Antigravity's per-model lanes do exactly that: each `agy` report returns the Argon weekly reset as roughly "now + 7 days". With a near-exhausted account the refresh floor is one minute, so the instant moved about a minute per poll, crossed the one-minute tolerance, and raised a `reset` toast every minute ("Antigravity reset - Gemini 4 Argon weekly reset: 0%"). The user saw dozens of identical notifications.

## Decision

A reset is raised only when both are true:

1. the previously observed reset instant has passed (`previous <= now`), and
2. the new instant is in the future and later than the previous one by more than the one-minute tolerance.

A window that stops reporting an instant also clears the remembered value, so a later reappearance is not compared against a stale boundary. Threshold re-arming keeps its existing rule: crossing back below the lowest threshold resets the state regardless of the reset signal.

## Consequences

- Sliding estimates and stale past instants no longer produce toasts.
- A genuine reset is still reported once, including one that happened while the app was closed: the next refresh sees an elapsed previous boundary and a later new one.
- If a provider resets a window early, before the reported instant, the usage drop still re-arms thresholds but no `reset` toast is shown. That trade keeps false alarms at zero, and the threshold path still covers the next climb.
- Quiet hours and the disabled reset category suppress delivery without replay, as before.
