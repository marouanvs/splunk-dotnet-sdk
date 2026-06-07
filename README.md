# SplunkSdk

[![CI](https://github.com/marouanvs/splunk-dotnet-sdk/actions/workflows/ci.yml/badge.svg)](https://github.com/marouanvs/splunk-dotnet-sdk/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

`SplunkSdk` is an unofficial modern .NET SDK for token-authenticated Splunk REST search, analytics, saved searches, and alerts. It focuses on team-owned index analytics such as error counts, average execution times, time-bucketed metrics, saved searches, and alerts while still allowing advanced teams to run raw SPL they already own.

This is an unofficial, work-in-progress SDK. It is not affiliated with, endorsed by, sponsored by, or supported by Splunk Inc. Splunk is a trademark or registered trademark of Splunk Inc. in the United States and other countries.

The core SDK intentionally has no external NuGet dependencies. It uses `HttpClient`, token providers, and streaming JSON parsing so the runtime package stays small and enterprise-friendly. Optional DI, xUnit test, and benchmark packages live in separate projects.

## Positioning

This project is not a drop-in replacement for Splunk's deprecated official C# SDK packages. It is a focused REST-first client for modern .NET applications that need to query Splunk, run bounded analytics, manage saved searches and saved-search alerts, and integrate cleanly with `HttpClient`, dependency injection, options binding, and host-owned secret stores.

For platform-wide Splunk automation beyond those surfaces, use Splunk's REST API directly or extend this SDK deliberately around the specific REST endpoints your application owns.

## What You Can Build

Use this SDK to build .NET services, jobs, dashboards backends, internal tools, or alert-management workflows that interact with Splunk without every team hand-writing HTTP calls and SPL string assembly.

Typical capabilities:

- Query team-owned Splunk indexes with token authentication.
- Count errors over a time range.
- Calculate average metric values such as execution duration or latency.
- Build time-series metrics with `timechart`.
- Execute full trusted SPL queries through `SplunkSearchRequest`.
- Stream export results without loading the full response upfront.
- Start Splunk search jobs and retrieve paged results later.
- Map Splunk rows into typed C# objects.
- Create, update, list, dispatch, and delete saved searches.
- Create scheduled or disabled alerts through saved-search alert settings.
- Acknowledge and suppress alerts.
- Register the SDK with dependency injection and `IHttpClientFactory`.
- Plug in custom token providers from Vault, Azure Key Vault, AWS Secrets Manager, Kubernetes secrets, or an internal rotation service.
- Add host-owned resilience through Polly, Microsoft.Extensions.Http.Resilience, or platform retry policies.
- Collect sanitized diagnostics through `ActivitySource` and `Meter`.
- Run opt-in live integration tests against a real Splunk instance.
- Benchmark local SDK paths such as SPL generation and JSON export parsing.

## Splunk Documentation Basis

The implementation follows current Splunk documentation for:

- Token authentication with REST calls through the `Authorization` header and the management port: <https://help.splunk.com/en/splunk-cloud-platform/administer/manage-users-and-security/10.2.2510/authenticate-into-the-splunk-platform-with-tokens/use-authentication-tokens>
- Creating and managing Splunk authentication tokens: <https://help.splunk.com/en/splunk-enterprise/administer/manage-users-and-security/10.0/authenticate-into-the-splunk-platform-with-tokens/create-authentication-tokens>
- Search REST endpoint versioning and v2 search endpoints: <https://help.splunk.com/en/splunk-enterprise/leverage-rest-apis/rest-api-reference/9.4/search-endpoints/search-endpoint-descriptions>
- Exporting search data with the Splunk REST API and `output_mode`: <https://help.splunk.com/en/splunk-enterprise/search/search-manual/10.4/export-search-results/export-data-using-the-splunk-rest-api>
- Splunk time modifiers such as `earliest`, `latest`, `-24h`, and `now`: <https://help.splunk.com/en?resourceId=Splunk_Search_Specifytimemodifiersinyoursearch>
- Statistical functions such as `count`, `avg`, and `timechart`: <https://docs.splunk.com/Documentation/SplunkCloud/latest/SearchReference/CommonStatsFunctions>
- Saved search endpoints and alert-related saved search fields: <https://help.splunk.com/en/splunk-cloud-platform/leverage-rest-apis/rest-api-reference/10.3.2512/search-endpoints/search-endpoint-descriptions>

## Project Layout

```text
src/SplunkSdk/                 SDK library
src/SplunkSdk.DependencyInjection/ DI integration package
tests/SplunkSdk.Tests/         xUnit unit tests using fake HTTP
tests/SplunkSdk.IntegrationTests/ opt-in xUnit live Splunk integration tests
benchmarks/SplunkSdk.Benchmarks/ BenchmarkDotNet microbenchmarks
skills/splunk-interactions/    Codex skill for Splunk work
AGENTS.md                      agent guidance for future changes
.github/copilot-instructions.md GitHub Copilot repository instructions
CLAUDE.md                      Claude Code project memory
```

## SDK Class And Method Guide

The SDK is organized around a small set of public clients and request models:

- `SplunkClient`: main entry point. It owns or wraps an `HttpClient` and exposes `Search`, `Analytics`, `SavedSearches`, and `Alerts`.
- `SplunkClientOptions`: endpoint, authentication, namespace, retry, and API-version configuration. Tokens are supplied through `ISplunkTokenProvider`.
- `ISplunkTokenProvider`: abstraction for token retrieval. Use `StaticSplunkTokenProvider` for simple examples and a custom implementation for Vault, cloud secret stores, Kubernetes secrets, or internal rotation services.
- `ISplunkSearchClient`: low-level search API.
  - `ExportAsync`: streams rows from Splunk export endpoints.
  - `StartAsync`: dispatches a search job and returns the Splunk search ID.
  - `GetResultsAsync`: reads rows from an existing search job.
- `ISplunkAnalyticsClient`: high-level helpers for common metrics.
  - `CountErrorsAsync`: builds a safe `stats count` query.
  - `AverageAsync`: builds a safe `stats avg(field)` query.
  - `AverageTimeSeriesAsync`: builds a safe `timechart avg(field)` query.
- `SplunkQueryBuilder`: safe SPL builder for team-owned index queries. It validates literal index names, field names, values, spans, and aggregate aliases.
- `SplunkSearchRequest`: full trusted SPL request. Use it when your application or Splunk team already owns the complete query.
- `SplunkSearchResult`: one result row returned by Splunk. Use `GetString`, `GetInt64`, and `GetDouble` for common scalar access.
- `SplunkFieldAttribute` and mapping extensions: map rows into typed C# objects with `ToObject`, `ToObjects`, and `ToObjectsAsync`.
- `ISplunkSavedSearchClient`: saved search knowledge-object API.
  - `ListAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, and `DispatchAsync`.
- `ISplunkAlertClient`: saved-search alert API.
  - `CreateAsync`: creates a scheduled saved-search alert.
  - `AcknowledgeAsync`: marks a tracked alert as acknowledged without changing the alert definition.
  - `SuppressAsync`: suppresses an alert for a period without deleting or disabling it.
- `SplunkApiException`: thrown for failed Splunk REST responses and streamed export `ERROR` or `FATAL` message frames.
- `SplunkDiagnostics`: exposes sanitized `ActivitySource` and `Meter` instrumentation for OpenTelemetry or custom listeners.

## Quick Start

```csharp
using SplunkSdk;
using SplunkSdk.Configuration;
using SplunkSdk.Models;

using var client = SplunkClient.Create(
    SplunkClientOptions.FromToken(
        new Uri("https://splunk.company.example:8089"),
        Environment.GetEnvironmentVariable("SPLUNK_TOKEN")!));

var errors = await client.Analytics.CountErrorsAsync(new ErrorCountQuery("payments_prod")
{
    Text = "ERROR",
    FieldFilters = new Dictionary<string, string>
    {
        ["service"] = "checkout"
    },
    TimeRange = SplunkTimeRange.Last(TimeSpan.FromHours(1))
});

Console.WriteLine(errors);
```

In this example:

- `payments_prod` is the Splunk index name, not the full query.
- `service` is the field name.
- `checkout` is the field value to filter on.
- `ERROR` is the free-text search term.

The analytics helper generates SPL equivalent to:

```spl
search index="payments_prod" "ERROR" service="checkout" | stats count AS error_count
```

Use `SplunkSearchRequest` instead when you already have a full trusted SPL query to execute.

## Metric Examples

Average execution time for a numeric field:

```csharp
var averageMs = await client.Analytics.AverageAsync(new AverageMetricQuery("payments_prod", "duration_ms")
{
    Text = "completed",
    FieldFilters = new Dictionary<string, string>
    {
        ["operation"] = "AuthorizePayment"
    },
    TimeRange = SplunkTimeRange.Relative("-24h", "now")
});
```

Five-minute average time series:

```csharp
var buckets = await client.Analytics.AverageTimeSeriesAsync(new MetricTimeSeriesQuery("payments_prod", "duration_ms")
{
    Span = "5m",
    TimeRange = SplunkTimeRange.Last(TimeSpan.FromHours(6))
});
```

Raw SPL export for advanced, trusted searches:

```csharp
await foreach (var row in client.Search.ExportAsync(
    new SplunkSearchRequest("search index=\"payments_prod\" \"ERROR\" | stats count AS error_count")
    {
        TimeRange = SplunkTimeRange.Last(TimeSpan.FromHours(1)),
        Count = 1
    }))
{
    Console.WriteLine(row.GetInt64("error_count"));
}
```

Job lifecycle mode for large or explicitly managed searches:

```csharp
var job = await client.Search.StartAsync(
    new SplunkSearchRequest("search index=\"payments_prod\" | stats avg(duration_ms) AS average_value")
    {
        TimeRange = SplunkTimeRange.Last(TimeSpan.FromHours(24))
    },
    SplunkExecutionMode.Blocking);

var rows = await client.Search.GetResultsAsync(job.Sid, new SplunkResultRequest
{
    Count = 1000,
    Offset = 0
});
```

`GetResultsAsync` buffers rows in memory, so its default count is bounded and `Count = 0` is rejected. Page through job results with positive `Count` and `Offset` values. Use `ExportAsync` when you intentionally need a streaming result flow.

## Typed Results

Use `SplunkFieldAttribute` when Splunk field names do not match DTO property names exactly:

```csharp
using SplunkSdk.Mapping;

public sealed class ErrorCountRow
{
    [SplunkField("error_count")]
    public long ErrorCount { get; set; }
}

var row = await client.Search
    .ExportAsync(new SplunkSearchRequest("search index=\"payments_prod\" | stats count AS error_count"))
    .ToObjectsAsync<ErrorCountRow>()
    .FirstAsync();
```

The built-in mapper supports common scalar types such as `string`, numeric types, `bool`, `DateTimeOffset`, `Guid`, enums, nullable variants, and `JsonElement`.

## Saved Searches And Alerts

Create a saved search:

```csharp
var savedSearch = await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest(
    "checkout_errors",
    "search index=\"payments_prod\" \"ERROR\" | stats count AS error_count")
{
    Description = "Checkout error count",
    IsScheduled = true,
    CronSchedule = "*/5 * * * *",
    TimeRange = SplunkTimeRange.Last(TimeSpan.FromMinutes(15)),
    Dispatch = new SplunkSavedSearchDispatchSettings
    {
        Buckets = 10,
        MaxCount = 5000,
        Lookups = true
    }
});
```

Create a disabled scheduled alert:

```csharp
var alert = await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
    "checkout_error_alert",
    "search index=\"payments_prod\" \"ERROR\"",
    "*/5 * * * *")
{
    Disabled = true,
    Alert = new SplunkAlertSettings
    {
        AlertType = SplunkAlertType.NumberOfEvents,
        Comparator = SplunkAlertComparator.GreaterThan,
        Threshold = "0",
        Severity = SplunkAlertSeverity.Error,
        Expires = "24h",
        Track = true,
        DigestMode = true,
        Suppression = new SplunkAlertSuppressionSettings
        {
            Enabled = true,
            Period = "30m",
            Fields = ["service", "host"]
        },
        Email = new SplunkEmailAlertActionSettings
        {
            To = ["checkout-oncall@example.com"],
            Subject = "Checkout errors detected",
            Message = "The checkout error alert fired."
        },
        SummaryIndex = new SplunkSummaryIndexAlertActionSettings
        {
            Name = "summary"
        }
    }
});
```

Creating an alert creates a Splunk saved-search alert. The SDK does not decide who receives notifications. Recipients and delivery behavior are controlled by Splunk alert actions:

- Setting `Email` enables the email action and configures common `action.email.*` fields.
- Setting `SummaryIndex` enables the `summary_index` action and configures `action.summary_index._name`.
- `Actions = ["webhook"]` can enable installed custom actions.
- Custom or app-specific action settings still use `AdditionalParameters["action.<name>.<field>"]`.
- If no recipient/action parameters are supplied, delivery depends on Splunk app-level defaults or the action might not send anything.

Saved search and alert writes modify Splunk knowledge objects. Use app namespaces and least-privilege roles in production.

## Authentication

Splunk tokens are credentials. The SDK never logs tokens and sends them per request through `Authorization: Bearer <token>` by default. Some app-specific endpoints can use `Authorization: Splunk <token>`.

Configure non-default values with an object initializer:

```csharp
var options = new SplunkClientOptions
{
    ManagementUri = new Uri("https://splunk.company.example:8089"),
    TokenProvider = new MySecretStoreTokenProvider(),
    AuthorizationScheme = SplunkSdk.Authentication.SplunkAuthorizationScheme.Bearer
};
```

Implement `ISplunkTokenProvider` when tokens come from Vault, Azure Key Vault, AWS Secrets Manager, Kubernetes secrets, or an internal rotation service.

## Dependency Injection

The `Marouanvs.SplunkSdk.DependencyInjection` package registers `SplunkClient` with `IHttpClientFactory` and exposes the search, analytics, saved search, and alert interfaces. The C# namespace remains `SplunkSdk.DependencyInjection`:

```csharp
using SplunkSdk.DependencyInjection;

builder.Services.AddSplunkClient(builder.Configuration);
```

This binds the default `Splunk` configuration section to `SplunkClientSettings`, validates it through the ASP.NET Core options pattern, and maps it to the core `SplunkClientOptions`. To bind an explicit section:

```csharp
builder.Services.AddSplunkClient(
    builder.Configuration.GetSection(SplunkClientSettings.SectionName));
```

Use the direct options overload when the host builds options itself:

```csharp
using SplunkSdk.DependencyInjection;

services.AddSplunkClient(new SplunkClientOptions
{
    ManagementUri = new Uri("https://splunk.company.example:8089"),
    TokenProvider = new StaticSplunkTokenProvider(token)
});
```

The returned `IHttpClientBuilder` can be used to add handlers, proxies, or resilience policies owned by the host application. If the host owns retries through Polly, Microsoft.Extensions.Http.Resilience, a service mesh, or another platform policy, disable SDK retries so there is only one retry owner:

```csharp
services
    .AddSplunkClient(new SplunkClientOptions
    {
        ManagementUri = new Uri("https://splunk.company.example:8089"),
        TokenProvider = new StaticSplunkTokenProvider(token),
        Retry = new SplunkRetryOptions { MaxRetries = 0 }
    });
```

## Endpoint Strategy

By default the SDK uses semantic v2 search endpoints:

- Streaming export: `/services/search/v2/jobs/export`
- Job dispatch: `/services/search/v2/jobs`
- Job results: `/services/search/v2/jobs/{sid}/results`

Set `SearchApiVersion = SplunkSearchApiVersion.V1` only when targeting an older deployment that does not support v2 search endpoints.

For Splunk Cloud, REST API access might need to be enabled for the deployment, and free trial Cloud accounts cannot access the REST API according to Splunk's export documentation.

## SPL Safety

The SDK gives two paths:

- Safe generated SPL through `SplunkQueryBuilder` and analytics helpers. Index names must be single literal index names, field identifiers must be safe unquoted SPL identifiers, field values are quoted, and time ranges are sent as REST parameters.
- Raw SPL through `SplunkSearchRequest` and `RawPredicate`. Use this only for trusted searches owned by your application or Splunk team.

Recommended query practices:

- Always scope searches to a team-owned index.
- Use raw SPL only when you intentionally need index wildcards or field names that require Splunk quoting.
- Always pass a time range unless the use case explicitly requires all time.
- Prefer `stats`, `timechart`, and field projection over exporting raw events.
- Avoid risky or mutating commands in SDK-driven searches.
- Keep app permissions and Splunk roles least-privilege.

## Result Parsing

Splunk JSON search output can be streamed as one JSON object per row, with the row under `result`, or returned as a single object containing a `results` array depending on endpoint and mode. The SDK handles both shapes and clones `JsonElement` values so rows remain usable after the response stream is disposed.

Use convenience accessors for common scalar values:

```csharp
var text = row.GetString("service");
var count = row.GetInt64("error_count");
var average = row.GetDouble("average_value");
```

## Resilience

The SDK retries transient failures on idempotent `GET` and `DELETE` requests:

- HTTP `429`
- HTTP `500`
- HTTP `502`
- HTTP `503`
- HTTP `504`
- `HttpRequestException`
- client-side timeouts where the caller cancellation token was not cancelled

Non-idempotent `POST` operations such as job creation, saved-search dispatch, alert acknowledge, and alert suppression are not retried by the SDK because the first request may already have been accepted by Splunk.

Configure retry behavior:

```csharp
var options = new SplunkClientOptions
{
    ManagementUri = uri,
    TokenProvider = new StaticSplunkTokenProvider(token),
    Retry = new SplunkRetryOptions
    {
        MaxRetries = 3,
        BaseDelay = TimeSpan.FromMilliseconds(250),
        MaxDelay = TimeSpan.FromSeconds(5)
    }
};
```

Keep one component responsible for retries. The core SDK intentionally does not depend on Polly; host applications can attach Polly or Microsoft.Extensions.Http.Resilience handlers through `SplunkSdk.DependencyInjection` and set `Retry.MaxRetries = 0` to avoid stacked retry amplification.

When `MaxRetries` is greater than zero, both `BaseDelay` and `MaxDelay` must be positive.

## Observability

The production SDK does not take a logging dependency and does not emit raw SPL, tokens, full URLs, or private hostnames. It exposes sanitized instrumentation through:

- `ActivitySource` name: `SplunkSdk`
- `Meter` name: `SplunkSdk`

Activities:

- `Splunk REST request`: request method, sanitized Splunk endpoint kind, search API version, status code, retry count.
- `Splunk search export`: operation name, completion flag, row count.
- `Splunk search start`: operation name, execution mode, completion flag.
- `Splunk search results`: operation name, completion flag, row count.

Metrics:

- `splunksdk.rest.client.request.duration`: REST request duration in milliseconds until response headers are received.
- `splunksdk.rest.client.retries`: retry attempts by endpoint and reason.
- `splunksdk.rest.client.errors`: unsuccessful Splunk REST responses surfaced by the SDK.
- `splunksdk.search.operation.duration`: search operation duration in milliseconds.
- `splunksdk.search.rows`: result rows read by operation.

Consumers can subscribe with OpenTelemetry, `ActivityListener`, or `MeterListener` from the host application. Keep high-cardinality or sensitive values such as raw SPL, Splunk hostnames, search IDs, and index contents out of telemetry attributes.

## TLS

Production deployments should use valid TLS certificates on the Splunk management endpoint. Prefer installing your internal CA certificate into the operating system or container trust store so normal platform validation succeeds.

If callers see an error such as `The SSL connection could not be established` or `remote certificate is invalid`, the request did not reach Splunk because .NET rejected the TLS certificate. Common causes are a default self-signed Splunk Enterprise certificate, a private CA that is not trusted by the host, an expired certificate, a proxy replacing certificates, or a `ManagementUri` host that does not match the certificate subject or SAN. Fix the Splunk management certificate, use the certificate's DNS name instead of an IP address when required, or install the issuing CA into the host/container trust store.

If the live integration test `ExportCountFromConfiguredIndex` fails normally but passes with `SPLUNKSDK_INTEGRATION_ALLOW_UNTRUSTED_CERTS=1`, the SDK request path, token, REST API access, and generated SPL are working. Treat that result as confirmation that the host does not trust the Splunk management certificate or that the `ManagementUri` host does not match the certificate.

The SDK intentionally does not expose an `AllowUntrustedCertificates` switch on `SplunkClientOptions`. Certificate bypasses should be explicit at the host `HttpClient` boundary and limited to local development or disposable Splunk labs.

Direct `HttpClient` example for a local lab:

```csharp
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};

using var httpClient = new HttpClient(handler);
using var client = new SplunkClient(httpClient, options);
```

Dependency-injection example for a local lab:

```csharp
services
    .AddSplunkClient(options)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
```

Do not use `HttpClientHandler.DangerousAcceptAnyServerCertificateValidator` against Splunk Cloud or production Splunk Enterprise. If custom validation is unavoidable, validate the certificate chain and hostname or pin a known certificate/public key with a documented rotation plan.

When using `AddSplunkClient(builder.Configuration)`, setting `Splunk:AllowUntrustedCertificates` to `true` applies the same bypass for local labs only.

## Build And Test

```bash
dotnet build SplunkSdk.slnx
dotnet test tests/SplunkSdk.Tests/SplunkSdk.Tests.csproj
dotnet test tests/SplunkSdk.IntegrationTests/SplunkSdk.IntegrationTests.csproj
dotnet pack src/SplunkSdk/SplunkSdk.csproj -c Release -o artifacts/packages
dotnet pack src/SplunkSdk.DependencyInjection/SplunkSdk.DependencyInjection.csproj -c Release -o artifacts/packages
python3 eng/validate-skill.py skills/splunk-interactions
```

The xUnit test project verifies auth headers, v2 endpoint paths, form parameters, generated SPL, streaming JSON rows, `results` array parsing, bounded buffered reads, retry behavior, Splunk API error message parsing, sanitized diagnostics and exceptions, typed materialization, DI registration, saved searches, and alert request generation.

## Packaging And Release

Package metadata is centralized in `Directory.Build.props` and `Directory.Build.targets`. Only `src/SplunkSdk` and `src/SplunkSdk.DependencyInjection` are packable; tests and benchmarks are explicitly left out of package output.

Published package IDs:

- `Marouanvs.SplunkSdk`
- `Marouanvs.SplunkSdk.DependencyInjection`

Packable projects produce:

- `.nupkg` packages.
- `.snupkg` symbol packages.
- XML documentation files.
- The repository `README.md` as the NuGet package readme.
- Package validation during `dotnet pack`.

Release state is tracked in `CHANGELOG.md`. Release procedure and versioning policy live in `RELEASE.md`.

Before publishing to NuGet.org, confirm repository metadata points at the public repository. GitHub Actions fills repository URLs automatically when the workflow runs in GitHub.

Full `appsettings.json` example for application projects:

```json
{
  "Splunk": {
    "ManagementUri": "https://splunk.company.example:8089",
    "Token": "",
    "TokenEnvironmentVariable": "SPLUNK_TOKEN",
    "AuthorizationScheme": "Bearer",
    "SearchApiVersion": "V2",
    "DefaultNamespace": {
      "Owner": "nobody",
      "App": "search"
    },
    "Retry": {
      "MaxRetries": 3,
      "BaseDelay": "00:00:00.250",
      "MaxDelay": "00:00:05"
    },
    "UserAgent": "MyService/1.0",
    "AllowUntrustedCertificates": false
  }
}
```

Parameter notes:

- `ManagementUri`: Splunk management REST endpoint, usually `https://host:8089`. This maps to `SplunkClientOptions.ManagementUri`.
- `Token`: optional host-application setting for local development or user secrets. Do not commit real tokens to source control. The configuration overload requires either `Token` or `TokenEnvironmentVariable`.
- `TokenEnvironmentVariable`: optional host-application setting naming the environment variable that contains the token. The configuration overload reads the environment variable during startup. The SDK itself only requires an `ISplunkTokenProvider`.
- `AuthorizationScheme`: `Bearer` for Splunk JWT tokens, or `Splunk` for endpoints that require `Authorization: Splunk <token>`.
- `SearchApiVersion`: `V2` by default. Use `V1` only for older Splunk deployments that do not support semantic v2 search endpoints.
- `DefaultNamespace.Owner` and `DefaultNamespace.App`: optional `/servicesNS/{owner}/{app}` namespace for knowledge objects and searches.
- `Retry.MaxRetries`: SDK-owned retries for idempotent `GET` and `DELETE` calls. Set to `0` when host-level resilience owns retries.
- `Retry.BaseDelay` and `Retry.MaxDelay`: retry backoff bounds used when `MaxRetries` is greater than `0`.
- `UserAgent`: optional request user-agent override. If omitted, the SDK derives one from the package assembly version.
- `AllowUntrustedCertificates`: host-application setting only. It is not part of `SplunkClientOptions`; the DI configuration overload uses it to configure a certificate bypass for disposable local labs.

Keep real tokens in a secret store, user secrets, or environment variable such as `SPLUNK_TOKEN`, not in a committed `appsettings.json`.

Set `Splunk:Retry:MaxRetries` to `0` when host-level resilience owns retries.

Index-backed integration tests skip unless these environment variables are set:

```bash
export SPLUNKSDK_INTEGRATION_URI="https://splunk.company.example:8089"
export SPLUNKSDK_INTEGRATION_TOKEN="..."
export SPLUNKSDK_INTEGRATION_INDEX="team_index"
```

Optional integration variables:

- `SPLUNKSDK_INTEGRATION_SPL` to run a read-only raw `SplunkSearchRequest` smoke test with trusted SPL supplied by the caller. This custom SPL test requires only `SPLUNKSDK_INTEGRATION_URI`, `SPLUNKSDK_INTEGRATION_TOKEN`, and `SPLUNKSDK_INTEGRATION_SPL`. The SPL must return at least one row; the test requests `count=1` and does not log SPL or result values.
- `SPLUNKSDK_INTEGRATION_AUTH_SCHEME=Splunk` to use `Authorization: Splunk`.
- `SPLUNKSDK_INTEGRATION_OWNER` and `SPLUNKSDK_INTEGRATION_APP` for `/servicesNS/{owner}/{app}`.
- `SPLUNKSDK_INTEGRATION_ALLOW_UNTRUSTED_CERTS=1` for local labs only.
- `SPLUNKSDK_INTEGRATION_MUTATE=1` to create/delete saved searches and disabled alerts.

Example custom SPL smoke test:

```bash
export SPLUNKSDK_INTEGRATION_SPL='| makeresults | eval sdk_raw_spl=1 | fields sdk_raw_spl'
```

## CI

GitHub Actions workflows live under `.github/workflows`:

- `ci.yml`: runs on push, pull request, and manual dispatch. It restores, verifies formatting, builds, runs the xUnit unit tests, verifies the live integration tests skip safely without secrets, validates the skill, and runs a BenchmarkDotNet dry smoke check.
- `splunk-integration.yml`: manual live Splunk integration workflow. It expects repository or environment secrets:
  - `SPLUNKSDK_INTEGRATION_URI`
  - `SPLUNKSDK_INTEGRATION_TOKEN`
  - `SPLUNKSDK_INTEGRATION_INDEX` for index-backed tests, or `SPLUNKSDK_INTEGRATION_SPL` for the custom raw SPL test

Optional workflow variables:

- `SPLUNKSDK_INTEGRATION_OWNER`
- `SPLUNKSDK_INTEGRATION_APP`

Optional workflow secret:

- `SPLUNKSDK_INTEGRATION_SPL`

The live workflow has explicit inputs for mutation tests, self-signed lab certificates, and auth scheme. Store live Splunk secrets in the `splunk-integration` GitHub environment so mutation runs can be approval-gated.

## Benchmarks

BenchmarkDotNet is used only in `benchmarks/SplunkSdk.Benchmarks`; the production SDK project has no BenchmarkDotNet dependency. The benchmarks measure deterministic local code paths:

- `QueryBuilderBenchmarks`: generated SPL creation.
- `SearchExportBenchmarks`: fake-HTTP export parsing over in-memory JSON frames.

Run a quick smoke check:

```bash
dotnet run -c Release --project benchmarks/SplunkSdk.Benchmarks -- --filter "*QueryBuilderBenchmarks*" --job Dry
```

Run the full benchmark suite when investigating allocations or parser/query-builder regressions:

```bash
dotnet run -c Release --project benchmarks/SplunkSdk.Benchmarks
```

Latest local results from BenchmarkDotNet `0.15.8` on .NET SDK `10.0.108`, .NET runtime `10.0.8`, Linux x64:

| Benchmark | Scenario | Mean | Allocated |
| --- | --- | ---: | ---: |
| `QueryBuilderBenchmarks.ErrorCountQuery` | Build `search index="..." "ERROR" service="..." \| stats count` | `576.3 ns` | `1.27 KB` |
| `QueryBuilderBenchmarks.TimechartAverageQuery` | Build `timechart span=5m avg(duration_ms)` SPL | `840.4 ns` | `1.51 KB` |
| `SearchExportBenchmarks.ExportAndReadRows` | Parse 10 fake export rows | `30.99 us` | `43.48 KB` |
| `SearchExportBenchmarks.ExportAndReadRows` | Parse 1,000 fake export rows | `2.115 ms` | `1.49 MB` |

These numbers are a baseline for SDK-local work. Query construction is sub-microsecond, and in-memory export parsing is roughly `2.1 us` per row for the 1,000-row case. Export parsing allocations scale with row materialization because the SDK clones JSON values and stores fields in result dictionaries.

Do not benchmark live Splunk queries with BenchmarkDotNet; real query timings belong in production telemetry or integration tests because they depend on data volume, search head load, cluster topology, cache state, and network behavior.

## AI Assistant Support

The Splunk interaction guidance is available to multiple coding assistants:

- Codex: `skills/splunk-interactions/SKILL.md` plus `AGENTS.md`.
- GitHub Copilot: `.github/copilot-instructions.md` and `.github/instructions/splunk-sdk.instructions.md`.
- Claude Code: `CLAUDE.md`, importing `AGENTS.md` and the Splunk interaction skill/reference files.

Keep these adapter files synchronized when changing SDK conventions or Splunk workflow rules.

## Current Scope

Implemented:

- Token-authenticated REST client.
- Splunk Bearer and Splunk auth schemes.
- v2 search endpoints by default with v1 compatibility.
- Streaming export rows.
- Search job dispatch and result retrieval.
- Typed result materialization.
- Safe SPL builder for index scope, literal text, field equality, `stats count`, `stats avg`, and `timechart avg`.
- High-level error count, average metric, and average time series helpers.
- Saved search and saved-search alert management.
- Dependency injection package backed by `IHttpClientFactory`.
- XML/JSON Splunk error message parsing.
- ActivitySource and Meter instrumentation named `SplunkSdk`.
- BenchmarkDotNet microbenchmarks for local parser and query-builder paths.
- Opt-in live Splunk integration harness.

Not implemented yet:

- HEC ingestion.
- Pagination helper abstractions above `SplunkResultRequest`.
- Dashboard management.

## License

This project is open source under the MIT license. See `LICENSE`.
