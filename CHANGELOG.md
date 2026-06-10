# Changelog

All notable changes to this project are documented here.

This project follows Semantic Versioning after `1.0.0`. Until `1.0.0`, minor
versions can include breaking API changes when they are called out in this file.

## [Unreleased]

No version has been tagged or published yet. Everything below ships in the
first public release.

### Changed

- Renamed the SDK root identity from `SplunkSdk` to `Marouanvs.Splunk` before
  the first publish. Package ids are now `Marouanvs.Splunk` and
  `Marouanvs.Splunk.DependencyInjection` (previously `Marouanvs.SplunkSdk` and
  `Marouanvs.SplunkSdk.DependencyInjection`), and the shipped assembly names
  and C# root namespaces match the package ids. The `ActivitySource` and
  `Meter` are now named `Marouanvs.Splunk`, metric instrument names use the
  `marouanvs.splunk.` prefix (previously `splunksdk.`), the dependency
  injection HTTP client names are `Marouanvs.Splunk` and
  `Marouanvs.Splunk:{name}`, and the default `User-Agent` product token is
  `Marouanvs.Splunk`. `Splunk*` type names, the `Splunk` configuration section
  name, the `SPLUNKSDK_INTEGRATION_*` environment variables, and the repository
  layout (`SplunkSdk.slnx`, project folder and file names) are unchanged.

### Added

- Token-authenticated Splunk REST client with `Bearer` and `Splunk` authorization schemes.
- Search export, search job dispatch, and paged job result retrieval using semantic v2 search endpoints by default.
- Search job lifecycle APIs: `OneshotSearchAsync`, `GetJobStatusAsync`, `WaitForJobCompletionAsync`, and `DeleteJobAsync`, plus the `SplunkExecutionMode.Oneshot` member.
- Safe SPL query builder for index-scoped searches, field filters, `stats count`, `stats avg`, and `timechart`. `FieldEquals` is literal-only; `FieldMatchesWildcard` provides intentional wildcard matching.
- High-level analytics helpers for error counts, average metrics, and average time series; analytics queries request final (non-preview) results and support a trusted `RawPredicate` on all query models.
- Saved-search and saved-search alert management, including typed dispatch, suppression, email, and summary-index settings. Operational alert suppression follows the Splunk suppress contract (`expiration` in whole seconds) with `SuppressAsync(TimeSpan)`, `UnsuppressAsync`, and `GetSuppressionAsync`, and fired alerts are readable through `ListFiredAlertGroupsAsync`/`ListFiredAlertsAsync`.
- JSON and XML Splunk message parsing with sanitized `SplunkApiException` errors. Exception messages include the structured Splunk `messages` entries; unparseable 2xx responses throw the dedicated `SplunkResponseFormatException`.
- Client hardening options: plain `http://` management URIs require the explicit `AllowInsecureHttp` opt-in, an optional `Timeout` applies to the SDK-owned `HttpClient` created by `SplunkClient.Create`, and that client never follows HTTP redirects.
- Jittered exponential retry backoff for idempotent requests. Server `Retry-After` delays are honored exactly up to `SplunkRetryOptions.MaxServerDelay` (default 30s); larger server delays fail fast instead of retrying. `MaxDelay` must be greater than or equal to `BaseDelay`.
- Typed result materialization through `SplunkFieldAttribute`, with UTC-normalized `DateTime` values, defined-value enum validation, and extended scalar type support.
- Dependency injection package for `IHttpClientFactory`, with named multi-instance registrations resolved as keyed services, fail-loud duplicate registration, settings keys for `AllowInsecureHttp`, `Timeout`, and `Retry:MaxServerDelay`, retry validation delegated to the core `SplunkRetryOptions.Validate()`, and a loopback-only `AllowUntrustedCertificates` lab switch that fails validation for non-loopback hosts.
- Sanitized `ActivitySource` and `Meter` diagnostics.
- Unit tests with fake HTTP and opt-in live Splunk integration tests.
- BenchmarkDotNet smoke benchmarks for local SPL and result-parser paths.
- MIT open-source license and NuGet license expression.
- NuGet package metadata, symbol package generation, package README inclusion, and package validation.
- Release workflow for tag-driven package publishing, gated behind the `release` GitHub environment, with an automatic API-compatibility baseline derived from the previous release tag and a check that the release commit is reachable from `main`.
- Supply-chain hardening: GitHub Actions pinned to commit SHAs, Dependabot updates for actions and NuGet, and locked-mode NuGet restore backed by committed `packages.lock.json` files.
- Warnings-as-errors and code-style enforcement during build.
- Security policy, contributing guide, code owners, and issue templates.
