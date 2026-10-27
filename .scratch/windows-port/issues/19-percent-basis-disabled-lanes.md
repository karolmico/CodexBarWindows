# 19: Percent basis and disabled lanes

**What to build:** Stop the flyout from looking like it disagrees with Antigravity's own window, and stop a disabled lane from disappearing without a trace.

**Blocked by:** 18

**Status:** done

## Why

The user compared two screens of the same account:

- Antigravity: `Weekly Limit Remaining 0%`, `Five Hour Limit Remaining 100%` (grey, because `disabled: true`).
- CodexBar: `100% used` and no five-hour row at all.

Both showed the same quota. One showed *remaining*, the other *used*, and the disabled lane was dropped by the projection, so the app looked like it was misreading the data.

## What changed

- [x] Every lane states both figures: `100% used · 0% left`
- [x] `RateWindow.IsDisabled` and `RateWindow.DisabledReason`; a disabled lane is never measured, so it cannot move a threshold, pace, or exhaustion badge
- [x] Antigravity CLI and desktop parsers keep the `disabled` flag and the bucket description that explains it
- [x] The flyout shows a disabled lane with a muted `disabled` chip, `Disabled` instead of a bar, and the provider's own sentence
- [x] The CLI prints `Disabled` plus the same sentence instead of `Unavailable`
- [x] Tests: both figures, disabled projection, disabled exclusion from measurement, parser keeps the flag and reason

## Not changed

The bar keeps the provider accent when a lane is exhausted. Only the small `exhausted` chip carries the warning, so a full bar stays informative instead of alarming.
