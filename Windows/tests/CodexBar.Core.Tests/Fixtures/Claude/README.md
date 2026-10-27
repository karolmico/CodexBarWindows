Payloads are hand-built from the upstream schema in
`Sources/CodexBarCore/Providers/Claude/ClaudeOAuth/ClaudeOAuthUsageFetcher.swift` and
`ClaudeUsageFetcher.swift` (`five_hour` / `seven_day` / `seven_day_*` windows with `utilization` and an
ISO-8601 `resets_at`). `auth-*.json` mirrors the `claudeAiOauth` block of `~/.claude/.credentials.json`.
No real credentials are present; tokens are placeholders.
