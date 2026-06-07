# Splunk REST And SPL Reference

Use this file when changing SDK behavior or creating Splunk examples.

## Official Documentation

- Token usage: https://help.splunk.com/en/splunk-cloud-platform/administer/manage-users-and-security/10.2.2510/authenticate-into-the-splunk-platform-with-tokens/use-authentication-tokens
- Token creation: https://help.splunk.com/en/splunk-enterprise/administer/manage-users-and-security/10.0/authenticate-into-the-splunk-platform-with-tokens/create-authentication-tokens
- Search endpoints: https://help.splunk.com/en/splunk-enterprise/leverage-rest-apis/rest-api-reference/9.4/search-endpoints/search-endpoint-descriptions
- REST export: https://help.splunk.com/en/splunk-enterprise/search/search-manual/10.4/export-search-results/export-data-using-the-splunk-rest-api
- Time modifiers: https://help.splunk.com/en?resourceId=Splunk_Search_Specifytimemodifiersinyoursearch
- Statistical functions: https://docs.splunk.com/Documentation/SplunkCloud/latest/SearchReference/CommonStatsFunctions

## Endpoint Notes

- Splunk token REST calls include the management port, commonly `8089`.
- `Authorization: Bearer <token>` is the normal JWT token header.
- `Authorization: Splunk <token>` exists for some app endpoints.
- Prefer semantic v2 search endpoints:
  - `/services/search/v2/jobs/export`
  - `/services/search/v2/jobs`
  - `/services/search/v2/jobs/{sid}/results`
- Saved search and alert management uses saved search endpoints:
  - `/services/saved/searches`
  - `/services/saved/searches/{name}`
  - `/services/saved/searches/{name}/dispatch`
  - `/services/saved/searches/{name}/acknowledge`
  - `/services/saved/searches/{name}/suppress`
- For app-scoped objects, use `/servicesNS/{owner}/{app}/...`.
- Use `output_mode=json` for SDK parsing.
- Saved-search `alert.severity` uses the savedsearches.conf scale: `1=debug`, `2=info`, `3=warn`, `4=error`, `5=severe`, `6=fatal`.

## Query Notes

- Put user-supplied time ranges in REST parameters `earliest_time` and `latest_time` where possible.
- Scope generated searches to an index.
- Reject wildcard index patterns in generated searches; require trusted raw SPL for intentional patterns.
- Quote field values with double quotes and escape quotes/backslashes.
- Validate field names before inserting them into generated SPL, and keep aggregate identifiers safe for unquoted command expressions.
- Prefer aggregates for SDK metrics:
  - `stats count AS error_count`
  - `stats avg(duration_ms) AS average_value`
  - `timechart span=5m avg(duration_ms) AS average_value`

## Operational Notes

- Treat tokens as credentials.
- Keep searches least-privilege through Splunk roles and index permissions.
- For broad exports, consider smaller time slices, field projection, or job result paging.
- Preserve Splunk error messages because they often include the missing capability or auth reason.
- Use `ActivitySource` and `Meter` for production diagnostics; keep telemetry attributes sanitized and low-cardinality.
- Use a single retry owner. The SDK only retries transient failures on idempotent GET and DELETE requests. If the host application adds Polly, Microsoft.Extensions.Http.Resilience, service mesh retries, or another retry layer, set SDK `MaxRetries` to `0`.
- Use BenchmarkDotNet only for local parser/query-builder microbenchmarks, never for live Splunk query timing.
- Keep integration tests opt-in and require an explicit mutation flag before writing saved searches or alerts.
