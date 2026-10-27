# 10: Threshold and reset notifications

**What to build:** When a measured window crosses a configured usage threshold or resets, Windows shows one toast naming the provider and window. Thresholds and reset alerts are individually configurable, quiet hours are respected, and no notification repeats for the same window until it is re-armed.

**Blocked by:** 03

**Status:** ready-for-agent

- [ ] Crossing 80 % and 95 % fires at most one toast per provider and window per cycle
- [ ] A reset to below the threshold re-arms the alert
- [ ] Reset notifications fire once per window reset, not on every refresh
- [ ] Disabled categories produce no toasts and no toast scheduling
- [ ] Clicking a toast opens the flyout focused on that provider
- [ ] A provider error does not produce usage notifications
- [ ] Notification decisions are unit-tested against a clock without a real toast API
