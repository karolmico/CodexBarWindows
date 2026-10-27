Payloads are hand-built from the upstream probe in
`Sources/CodexBarCore/Providers/Antigravity/AntigravityStatusProbe.swift` and its parser in
`AntigravityQuotaSummaryParser.swift`:

- `quota-summary.json` — `POST /exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary`
  with `{"forceRefresh":true}`; groups carry per-model buckets with `remainingFraction`,
  `resetTime`, and a cadence (`window`, `bucketId`, or `displayName`).
- `quota-summary-empty.json` — a valid response with no buckets (must be a parse error, not 0 %).
- `user-status.json` — `POST .../GetUserStatus` identity payload (`userStatus.email`,
  `userStatus.userTier.name`).

No real Antigravity process, token, or account is involved.
