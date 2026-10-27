# 13: Diagnostics, logging, and copy-diagnostics

**What to build:** When a provider misbehaves, the user can copy a redacted diagnostics bundle and attach it to a bug report. Logs are local, rolling, and free of tokens, cookies, emails, and full file paths.

**Blocked by:** 03

**Status:** ready-for-agent

- [ ] Logs roll at a size cap and older files are pruned
- [ ] Tokens, cookies, authorisation headers, and account emails are redacted before writing
- [ ] The diagnostics bundle contains versions, enabled provider keys, last error kinds, timings, and settings shape only
- [ ] Copy diagnostics puts the bundle on the clipboard and confirms visibly
- [ ] An unexpected exception in a provider is contained, logged, and surfaced as an unknown error without killing the app
- [ ] Redaction is unit-tested with representative secret-shaped strings
