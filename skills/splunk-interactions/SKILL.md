---
name: splunk-interactions
description: Work with Splunk Enterprise or Splunk Cloud searches, REST API calls, SPL generation, token-authenticated SDK code, dashboards, alerts, and operational troubleshooting. Use when Codex needs to design safe Splunk queries, modify this .NET Splunk SDK, explain Splunk REST behavior, review SPL for risk, or create examples for querying team-owned indexes and metrics.
---

# Splunk Interactions

## Overview

Use this skill to make Splunk work precise, safe, and grounded in official REST/SPL behavior. Prefer least-privilege token use, index-scoped searches, explicit time ranges, and transforming searches that return aggregated rows instead of raw event dumps.

## Workflow

1. Identify the Splunk surface: Enterprise or Cloud, management URI, search head or search head cluster, app namespace if required, and whether REST API access is enabled.
2. Confirm auth mode: use `Authorization: Bearer <token>` for Splunk JWT tokens unless the target app endpoint explicitly requires `Authorization: Splunk <token>`.
3. Scope the query: always include the team-owned `index`, a bounded time range, and only the needed fields or aggregates.
4. Choose the REST pattern:
   - Use `search/v2/jobs/export` for streaming lightweight analytics and one-shot aggregate queries.
   - Use `search/v2/jobs` with `exec_mode=blocking`, then `jobs/{sid}/results`, when the caller needs job lifecycle control or result paging.
   - Use `saved/searches` for saved search and alert knowledge object management.
5. Use safe SPL generation for user-provided values. Treat raw SPL as trusted application-owned input only.
6. Validate behavior with fake HTTP tests for SDK code or with read-only Splunk searches for live environments.

## SDK Patterns

- Keep tokens behind `ISplunkTokenProvider`; never log token values or include them in exceptions.
- Prefer `SplunkClientOptions.FromToken` for examples and custom token providers for production integrations.
- Prefer `SplunkTimeRange` REST parameters over embedding `earliest` and `latest` in generated SPL.
- Keep endpoint changes compatible with semantic v2 search endpoints by default.
- Add xUnit tests through `tests/SplunkSdk.Tests` using fake HTTP; do not require a live Splunk instance for unit coverage.
- Keep live Splunk integration tests under `tests/SplunkSdk.IntegrationTests` opt-in through environment variables.
- Require `SPLUNKSDK_INTEGRATION_MUTATE=1` before integration tests create/delete saved searches or alerts.
- Use `ActivitySource` and `Meter` for production observability; do not add a mandatory logging dependency.
- Keep resilience ownership explicit. The SDK can retry transient failures on idempotent GET and DELETE requests, but host-owned retry policies should set `SplunkClientOptions.Retry.MaxRetries = 0`.
- Keep BenchmarkDotNet isolated to `benchmarks/SplunkSdk.Benchmarks` for local parser/query-builder microbenchmarks.
- Keep dependency-injection dependencies isolated to `src/SplunkSdk.DependencyInjection`.
- Keep CI secret names aligned with documented `SPLUNKSDK_INTEGRATION_*` environment variables.

## SPL Guidelines

- Start generated searches with `search index="team_index"`.
- Reject index wildcards in generated searches; use trusted raw SPL for intentional index patterns.
- Quote literal values and validate field names before inserting them into SPL.
- Restrict generated aggregate field identifiers to safe unquoted SPL identifiers.
- Prefer `stats count`, `stats avg(field)`, and `timechart span=... avg(field)` for SDK metrics.
- Avoid risky or mutating commands in SDK-generated searches.
- When a user asks for broad event export, suggest aggregation, field projection, paging, or smaller time slices first.

## Troubleshooting

- Authentication failures usually mean wrong token type, expired/disabled token, token from a different instance, missing token auth enablement, or wrong `Authorization` scheme.
- Permission errors often mention the missing Splunk capability; preserve that message in SDK exceptions.
- Cloud REST failures can be caused by REST API access not being enabled for the stack.
- Large searches can hit search head quotas; prefer `exec_mode=blocking` plus paged results or smaller time ranges.
- Self-signed certificate bypasses are acceptable only for local labs, never for production or Splunk Cloud.

## References

Read `references/splunk-rest-and-spl.md` when changing endpoint behavior, auth handling, SPL generation, or SDK examples.
