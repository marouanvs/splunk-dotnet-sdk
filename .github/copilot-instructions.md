# Repository Instructions For GitHub Copilot

This repository contains a .NET 10 Splunk SDK. Keep generated code production-oriented, token-safe, and grounded in official Splunk REST/SPL behavior.

## Required Context

- Treat `skills/splunk-interactions/SKILL.md` as the canonical Splunk workflow.
- Consult `skills/splunk-interactions/references/splunk-rest-and-spl.md` before changing endpoint behavior, authentication behavior, SPL generation, result parsing, or examples.
- Follow `AGENTS.md` for build/test commands and repository-wide engineering rules.

## SDK Rules

- Never log or persist Splunk tokens, session keys, private hostnames, or real customer search results.
- Preserve the `ISplunkTokenProvider` abstraction for secret stores and token rotation.
- Use `Authorization: Bearer <token>` by default. Use `Authorization: Splunk <token>` only when an app endpoint requires it.
- Prefer semantic v2 search endpoints:
  - `/services/search/v2/jobs/export`
  - `/services/search/v2/jobs`
  - `/services/search/v2/jobs/{sid}/results`
- Keep v1 compatibility only where the SDK already exposes it.
- Use generated SPL helpers for user-provided index names, field names, values, and time spans.
- Reject wildcard index patterns and unsafe unquoted aggregate identifiers in generated SPL.
- Treat raw SPL as trusted application-owned input only.
- Always scope generated searches to a team-owned index and a bounded time range.
- Prefer aggregate searches such as `stats count`, `stats avg(field)`, and `timechart span=... avg(field)` over raw event export.
- Keep observability sanitized: no tokens, raw SPL, full URLs, private hostnames, search IDs, or event payloads in tags/attributes.
- Keep resilience ownership explicit. The core SDK has built-in retries for idempotent GET and DELETE requests but no Polly dependency; host-owned retry policies must set `SplunkClientOptions.Retry.MaxRetries = 0`.
- Keep BenchmarkDotNet references isolated to `benchmarks/SplunkSdk.Benchmarks`.
- Keep Microsoft.Extensions dependencies isolated to `src/SplunkSdk.DependencyInjection`.
- Keep integration tests opt-in and require `SPLUNKSDK_INTEGRATION_MUTATE=1` for saved-search or alert writes.

## Verification

- Build with `dotnet build SplunkSdk.slnx`.
- Run tests with `dotnet test tests/SplunkSdk.Tests/SplunkSdk.Tests.csproj`.
- Run integration tests with `dotnet test tests/SplunkSdk.IntegrationTests/SplunkSdk.IntegrationTests.csproj`; they skip unless env vars are set.
- Smoke-check benchmarks with `dotnet run -c Release --project benchmarks/SplunkSdk.Benchmarks -- --filter "*QueryBuilderBenchmarks*" --job Dry`.
- Validate the Codex skill with `python3 eng/validate-skill.py skills/splunk-interactions` when editing files under `skills/splunk-interactions`.
