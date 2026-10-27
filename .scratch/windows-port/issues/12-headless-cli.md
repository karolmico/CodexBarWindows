# 12: Headless CLI over the same core

**What to build:** `codexbar usage --format json` prints the same usage data the tray shows, with stable field names compatible with the macOS CLI, so scripts and Windows automation can consume it. Text output mirrors the upstream CLI's summary lines.

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] JSON output includes provider id, windows, percentages, reset instants, identity, and error kind, with stable camelCase names
- [ ] Text output mirrors upstream's summary layout closely enough for existing scripts
- [ ] `--provider` selects one provider; the default is all enabled providers
- [ ] A failing provider appears as an error entry without failing the whole command unless requested
- [ ] Exit codes distinguish success, partial failure, and total failure
- [ ] Golden-file tests cover JSON and text output for two providers and one error
