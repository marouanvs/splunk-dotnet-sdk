# Agent Guidance

This repository contains a .NET 10 Splunk SDK. Keep changes focused on production SDK behavior: token-authenticated REST calls, safe SPL generation, search result parsing, and operational documentation.

## Commands

- Build: `dotnet build SplunkSdk.slnx`
- Format check: `dotnet format SplunkSdk.slnx --verify-no-changes` (CI fails on formatting drift)
- Tests: `dotnet test tests/SplunkSdk.Tests/SplunkSdk.Tests.csproj`
- Integration tests: `dotnet test tests/SplunkSdk.IntegrationTests/SplunkSdk.IntegrationTests.csproj` (skips unless env vars are set)
- Pack: `dotnet pack src/SplunkSdk/SplunkSdk.csproj -c Release -o artifacts/packages` and `dotnet pack src/SplunkSdk.DependencyInjection/SplunkSdk.DependencyInjection.csproj -c Release -o artifacts/packages`
- Benchmark smoke: `dotnet run -c Release --project benchmarks/SplunkSdk.Benchmarks -- --filter "*QueryBuilderBenchmarks*" --job Dry`
- Skill validation: `python3 eng/validate-skill.py skills/splunk-interactions`

## Engineering Rules

- Never commit Splunk tokens, session keys, search results containing sensitive data, or real hostnames from a private deployment.
- Do not log token values. Preserve the `ISplunkTokenProvider` boundary for secret-store and rotation integrations.
- Prefer Splunk semantic v2 search endpoints. Keep v1 compatibility only where the code already exposes it.
- Use generated SPL helpers for user-provided index names, field names, and values. Raw SPL is only for trusted application-owned searches.
- Keep public SDK APIs documented with XML comments and covered by xUnit tests.
- Keep production observability sanitized: no tokens, raw SPL, full URLs, private hostnames, search IDs, or event payloads in activity tags or metric attributes.
- Keep resilience ownership explicit. The core SDK has built-in retries for idempotent GET and DELETE requests but no Polly dependency; host-owned retry policies must set `SplunkClientOptions.Retry.MaxRetries = 0`.
- Keep BenchmarkDotNet references isolated to `benchmarks/SplunkSdk.Benchmarks`.
- Keep live Splunk tests opt-in; saved search and alert mutation tests must require `SPLUNKSDK_INTEGRATION_MUTATE=1`.
- Keep Microsoft.Extensions dependencies isolated to `src/SplunkSdk.DependencyInjection`.
- Keep GitHub Actions secret names aligned with the `SPLUNKSDK_INTEGRATION_*` environment variables documented in README.
- Consult official Splunk documentation before changing endpoint behavior, auth behavior, output parsing, or SPL time/stat functions.

## Skill

Use `skills/splunk-interactions` when working on Splunk searches, Splunk REST API behavior, SDK examples, troubleshooting, or operational guidance.

## Other AI Assistants

- GitHub Copilot uses `.github/copilot-instructions.md` and `.github/instructions/splunk-sdk.instructions.md`.
- Claude Code uses `CLAUDE.md`, which imports this file and the Splunk interaction skill.
- Keep Splunk-specific guidance synchronized across `AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, and `skills/splunk-interactions`.
