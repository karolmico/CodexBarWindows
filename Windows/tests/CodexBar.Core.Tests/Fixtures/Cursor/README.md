Payloads are hand-built from the upstream models in
`Sources/CodexBarCore/Providers/Cursor/CursorStatusProbe.swift` and the field mapping in
`CursorStatusProbe+UsageSummary.swift`:

- `usage-summary.json` — `GET https://cursor.com/api/usage-summary` for a token-based plan
  (`individualUsage.plan.totalPercentUsed`, `autoPercentUsed`, `apiPercentUsed`, billing cycle).
- `usage-summary-legacy.json` — the same endpoint for a legacy request plan with no `plan` block.
- `usage-legacy.json` — `GET /api/usage?user=<id>` (`gpt-4.maxRequestUsage`, `numRequestsTotal`).
- `auth-me.json` — `GET /api/auth/me` identity payload.

The session database fixture is created at test time as a real SQLite file, so no binary
fixtures are committed. No real tokens are present.
