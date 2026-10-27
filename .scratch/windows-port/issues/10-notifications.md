# 10: Threshold and reset notifications

**What to build:** When a measured window crosses a configured usage threshold or resets, Windows shows one toast naming the provider and window. Thresholds and reset alerts are individually configurable, quiet hours are respected, and no notification repeats for the same window until it is re-armed.

**Blocked by:** 03

**Status:** done

- [x] Crossing 80 % and 95 % fires at most one toast per provider and window per cycle
- [x] A reset to below the threshold re-arms the alert
- [x] Reset notifications fire once per window reset, not on every refresh
- [x] Disabled categories produce no toasts and no toast scheduling
- [x] Clicking a toast opens the flyout focused on that provider
- [x] A provider error does not produce usage notifications
- [x] Notification decisions are unit-tested against a clock without a real toast API

## Further notes

- `NotificationDecider` is pure state plus a clock: per provider *and* quota lane (session, weekly,
  each named window) it tracks the highest threshold already announced and the last reset instant.
  A jump past both thresholds emits only the highest; a reset starts a fresh cycle and suppresses a
  stacked threshold toast.
- Quiet hours wrap past midnight (`22:00–07:00`) and are a delivery filter, not a queue: a suppressed
  alert is marked as delivered so it never fires late. Provider errors skip the provider entirely
  without touching its arming state.
- Settings: individual 80/95 checkboxes, a reset toggle, and optional quiet-hour start/end in `HH:mm`.
  Persisted additively in schema 1; older files keep the defaults.
- Delivery uses the tray notification pipeline (`H.NotifyIcon`), capped at three toasts per refresh.
  `TrayBalloonTipClicked` reopens the flyout with the provider card highlighted.
