# 16: Reset notifications require an elapsed window

**What to build:** A user with a near-exhausted account gets at most one reset toast per real cycle, even when the provider recomputes its reset instant on every read.

**Blocked by:** 10

**Status:** done

- [x] A reset needs the previously observed instant to have passed and a later future instant to replace it
- [x] A window whose end slides forward on every poll produces no notification (regression test with Antigravity's Argon lane)
- [x] A window end stuck in the past produces no notification
- [x] A genuine reset, including one that happened while the app was closed, still notifies exactly once
- [x] A window that stops reporting an instant clears its remembered boundary
- [x] Threshold re-arming after a reset is unchanged

## Root cause

`NotificationDecider` treated any forward move larger than one minute as a reset. Antigravity reports per-model weekly lanes as "now + 7 days", and the critical-usage refresh floor is one minute, so the instant moved past the tolerance on almost every poll and raised `Antigravity reset - Gemini 4 Argon weekly reset: 0%` each minute.

See ADR 0006 for the rule and its trade-offs.
