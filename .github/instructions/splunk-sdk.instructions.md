---
applyTo: "src/SplunkSdk/**,tests/SplunkSdk.Tests/**,skills/splunk-interactions/**,README.md,AGENTS.md,CLAUDE.md,.github/copilot-instructions.md"
---

# Splunk SDK Path Instructions

When working on these files, apply the Splunk interaction workflow from `skills/splunk-interactions/SKILL.md`.

- Keep tokens behind `ISplunkTokenProvider`; never log token values.
- Prefer Splunk v2 search endpoints and keep request paths compatible with Splunk REST documentation.
- Validate user-provided SPL fragments where possible; raw SPL must be trusted input.
- Reject wildcard index patterns and aggregate field identifiers that are unsafe unquoted.
- Preserve fake-HTTP test coverage for auth headers, endpoint paths, SPL generation, retry behavior, result parsing, and error parsing.
- Preserve sanitized ActivitySource/Meter diagnostics without adding production logging dependencies.
- Keep host-owned retry policies from stacking with SDK retries by setting `SplunkClientOptions.Retry.MaxRetries = 0`.
- Keep integration tests opt-in and mutation-gated.
- Keep DI dependencies in `src/SplunkSdk.DependencyInjection`, not the core SDK project.
- Keep GitHub Actions workflows aligned with README commands and `SPLUNKSDK_INTEGRATION_*` secret names.
- Update README examples when public SDK APIs change.
- Run the xUnit test project before finishing SDK code changes.
