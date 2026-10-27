# 17: Retire the Gemini CLI provider

**What to build:** No permanently failing Gemini tile. Google withdrew individual-account support from `@google/gemini-cli`, and Antigravity already reports the Gemini model lanes for the same account.

**Blocked by:** 06, 09

**Status:** done

- [x] `gemini` key, provider, credential reader, usage client, and setup entry removed
- [x] Provider catalog, CLI source labels, and CLI help no longer mention `gemini`
- [x] Tests and fixtures for the retired provider deleted; generic tests use a shipped provider
- [x] CLI golden fixtures still cover one healthy and one failing provider (now Copilot)
- [x] Existing `settings.json` files keep the stale key as an unknown, ignored entry
- [x] Full suite green after the removal

See ADR 0005.
