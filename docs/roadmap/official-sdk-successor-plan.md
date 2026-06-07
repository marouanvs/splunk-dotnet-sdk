# Official C# SDK Successor Plan

## Recommended Positioning

Use this positioning:

> A modern unofficial .NET successor for teams migrating away from Splunk's deprecated C# SDK.

Avoid claiming to be the official replacement. The goal is to become the practical migration target for .NET teams that previously used Splunk's deprecated C# SDK packages, while remaining clear that this project is unofficial and not affiliated with Splunk Inc.

Relevant upstream status:

- Splunk states that the Splunk Enterprise SDK for C# v2.x is deprecated and no longer receives feature enhancements, bug fixes, or support: <https://dev.splunk.com/enterprise/docs/devtools/csharp/Csharpdeprecation>
- NuGet marks `Splunk.Client` as legacy and no longer maintained: <https://www.nuget.org/packages/Splunk.Client>
- The `splunk/splunk-sdk-csharp` repository is archived and read-only: <https://github.com/splunk/splunk-sdk-csharp>

## Phase 1: Replacement Scope

Define what replacement means before expanding APIs.

Create a compatibility matrix that compares the official SDK and this SDK across:

| Area | Official SDK | Current SDK | Target |
| --- | --- | --- | --- |
| Auth/session | Username/password and sessions | Token-first | Token-first plus migration support for session-based deployments |
| Search export | Supported | Supported | Keep as a strength |
| Search jobs/results | Supported | Supported basic lifecycle | Full job lifecycle and pagination |
| Saved searches | Supported | Good partial support | Broader field coverage and migration docs |
| Alerts | Saved-search based | Good partial support | Broader action and trigger support |
| Apps/indexes/inputs | Broader coverage | Mostly missing | Add high-value read endpoints first |
| Modular inputs | Supported historically | Missing | Consider a separate package |
| Event submission/HEC | Supported historically | Missing | Consider a separate ingestion package |
| ASP.NET Core DI/options | Not modern ASP.NET Core-native | Supported | Treat as a major advantage |
| Observability | Older SDK style | `ActivitySource` and `Meter` | Keep sanitized OpenTelemetry-friendly diagnostics |

Deliverable:

- `docs/migration/official-csharp-sdk-migration.md`

## Phase 2: Migration-Friendly API

Add conveniences that make migration straightforward without copying the old SDK API surface.

Work items:

- Add a `SplunkSessionTokenProvider` for existing Splunk session keys.
- Consider a `SplunkLoginClient` or `SplunkLoginTokenProvider` for username/password login, documented as migration-only.
- Add namespace helpers such as `SplunkNamespace.System` and `SplunkNamespace.SearchApp`.
- Add `SearchOneShotAsync` as a convenience over export for users coming from official SDK examples.
- Add full job lifecycle operations:
  - create/start job
  - get job metadata
  - list jobs
  - cancel/finalize job
  - retrieve paged results
- Add result pagination helpers.

Avoid:

- Reusing official package names.
- Reusing official namespaces.
- Implying Splunk endorsement.

## Phase 3: Endpoint Coverage

Prioritize endpoints that old SDK users are most likely to depend on.

Tier 1, before `1.0`:

- Search export.
- Search jobs lifecycle.
- Saved searches CRUD and dispatch.
- Alert create, update, suppress, acknowledge.
- Apps list/get.
- Indexes list/get.
- Server info.
- Capabilities and roles basic read endpoints.

Tier 2, after `1.0`:

- Data inputs.
- Index management writes.
- Configuration endpoints.
- User and role management.
- Storage passwords.
- HTTP Event Collector/event ingestion as a separate package.

Tier 3:

- Modular inputs, likely as `Marouanvs.SplunkSdk.ModularInputs`.

## Phase 4: Saved Search And Alert Depth

Saved searches and alerts are key to replacement credibility because many operational Splunk integrations manage knowledge objects and alerts.

Add typed support for:

- More `saved/searches` fields.
- Email alert action settings.
- Webhook alert action settings.
- Summary index action settings.
- Custom alert action names and parameters.
- Realtime and scheduled alert modes.
- Trigger settings.
- Throttling and suppression fields.
- Severity.
- Dispatch TTL.
- Lookups.
- Max count.
- Time format.
- Reduce frequency.
- Update semantics for clearing values.

Keep an `AdditionalParameters` escape hatch with reserved-field validation so advanced/custom fields remain possible without waiting on SDK releases.

Definition of done:

- Typed models cover common alert fields.
- Advanced/custom fields remain possible.
- Unit tests verify exact Splunk form fields.

## Phase 5: Reliability And Compatibility

A replacement candidate must be boring in production.

Add or harden:

- Retry policy documentation.
- Timeout guidance.
- TLS troubleshooting.
- Splunk Cloud REST enablement guidance.
- Sanitized exceptions.
- No raw SPL, token, host, SID, or event payload leakage in telemetry.
- Streamed export error-frame tests.
- Cancellation tests.
- Large-result parser tests.
- Malformed JSON/XML tests.

Validation matrix:

- Splunk Enterprise 9.x.
- Splunk Enterprise 10.x.
- Splunk Cloud.
- Bearer token auth.
- `Authorization: Splunk` auth.
- Trusted certificates.
- Self-signed local lab certificates.

## Phase 6: Migration Docs

Documentation matters as much as endpoint coverage for a migration target.

Add:

- Migrating from `Splunk.Client`.
- Migrating from `SplunkSDK`.
- Search examples: old SDK mental model to new SDK model.
- Auth migration: username/password/session to token.
- Saved search migration.
- Alert migration.
- Common pitfalls.

Example migration style:

```csharp
await foreach (var row in client.Search.ExportAsync(
    new SplunkSearchRequest("search index=\"main\" error | stats count")))
{
    // Handle result rows.
}
```

## Phase 7: Packaging And Trust

Before using replacement-level positioning publicly, the packages need release maturity.

Required:

- Publish NuGet packages:
  - `Marouanvs.SplunkSdk`
  - `Marouanvs.SplunkSdk.DependencyInjection`
- SourceLink.
- Deterministic builds.
- Symbol packages.
- Package README.
- License metadata.
- Changelog.
- SemVer policy.
- Public API compatibility checks.
- Signed release tags.
- GitHub release notes.
- Security policy.
- Contribution guide.
- Code owners.

## Phase 8: Public Positioning

Near-term README wording:

> This SDK is intended as a modern unofficial migration path for .NET teams currently using Splunk's deprecated C# SDK packages or direct Splunk REST calls.

Package description:

> Modern unofficial .NET SDK for Splunk REST search, analytics, saved searches, and alerts.

Avoid:

- "Official replacement."
- "Supported by Splunk."
- "Drop-in replacement."
- Splunk-owned naming or branding that implies endorsement.

## Phase 9: 1.0 Gate

Do not ship `1.0.0` until:

- Packages are published.
- Live tests pass against at least one real Splunk Enterprise instance.
- Live tests pass against Splunk Cloud, if possible.
- Search, jobs, saved searches, and alerts are stable.
- Migration guide exists.
- Public API compatibility checks are active.
- At least one real user migration has been tested.
- README clearly states unofficial status.

## Recommended Roadmap Order

1. Finish packaging and publish `0.1.0-preview`.
2. Build the official SDK migration matrix.
3. Add full job lifecycle and endpoint coverage gaps.
4. Deepen saved-search and alert fields.
5. Add migration docs.
6. Run live Splunk Enterprise and Splunk Cloud validation.
7. Move to `1.0.0` only when the SDK can confidently claim to be a modern unofficial migration path.

## GitHub Project Dashboard Items

Use these items to seed the GitHub Project dashboard:

| Item | Phase | Priority |
| --- | --- | --- |
| Define official C# SDK compatibility matrix | Phase 1 | High |
| Write migration guide from `Splunk.Client` and `SplunkSDK` | Phase 1 | High |
| Add migration-friendly auth/session helpers | Phase 2 | High |
| Add `SearchOneShotAsync` convenience API | Phase 2 | Medium |
| Complete search job lifecycle API | Phase 2 | High |
| Add apps, indexes, server info, and role read endpoints | Phase 3 | Medium |
| Broaden saved-search field coverage | Phase 4 | High |
| Broaden alert action and trigger settings | Phase 4 | High |
| Harden parser, cancellation, TLS, retry, and telemetry tests | Phase 5 | High |
| Validate against Splunk Enterprise and Splunk Cloud | Phase 5 | High |
| Add official SDK migration docs and examples | Phase 6 | High |
| Publish preview NuGet packages | Phase 7 | High |
| Add SourceLink, signed tags, API compatibility checks, and security policy | Phase 7 | High |
| Update public positioning after validation | Phase 8 | Medium |
| Define and enforce `1.0.0` release gate | Phase 9 | High |
