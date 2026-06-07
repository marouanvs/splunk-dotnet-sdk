# Contributing

Thanks for helping improve this unofficial .NET Splunk SDK. This guide keeps
contributions consistent with the engineering rules in `AGENTS.md`.

## Build, Test, And Format

All commands run from the repository root:

```bash
dotnet build SplunkSdk.slnx
dotnet test tests/SplunkSdk.Tests/SplunkSdk.Tests.csproj
dotnet test tests/SplunkSdk.IntegrationTests/SplunkSdk.IntegrationTests.csproj
dotnet format SplunkSdk.slnx --verify-no-changes
dotnet pack src/SplunkSdk/SplunkSdk.csproj -c Release -o artifacts/packages
dotnet pack src/SplunkSdk.DependencyInjection/SplunkSdk.DependencyInjection.csproj -c Release -o artifacts/packages
dotnet run -c Release --project benchmarks/SplunkSdk.Benchmarks -- --filter "*QueryBuilderBenchmarks*" --job Dry
python3 eng/validate-skill.py skills/splunk-interactions
```

CI enforces formatting (`dotnet format --verify-no-changes`), warnings as
errors, locked-mode NuGet restore, unit tests, packaging, skill validation,
and a benchmark dry run. Run the same commands locally before opening a
pull request. NuGet dependencies are pinned by `packages.lock.json` files;
if you intentionally change a dependency, update the lock files with
`dotnet restore SplunkSdk.slnx` and commit them.

## Integration Tests Are Opt-In

`tests/SplunkSdk.IntegrationTests` skips unless environment variables are
set, so running it without configuration is the expected "skips safely"
check:

- `SPLUNKSDK_INTEGRATION_URI` and `SPLUNKSDK_INTEGRATION_TOKEN` are required
  for any live test.
- `SPLUNKSDK_INTEGRATION_INDEX` enables index-backed tests;
  `SPLUNKSDK_INTEGRATION_SPL` enables the trusted raw SPL smoke test.
- `SPLUNKSDK_INTEGRATION_MUTATE=1` is required before any test creates or
  deletes saved searches or alerts. Never enable it against a production
  instance.
- `SPLUNKSDK_INTEGRATION_ALLOW_UNTRUSTED_CERTS=1` is for disposable local
  labs only.

Never commit Splunk tokens, session keys, real hostnames from a private
deployment, or search results containing sensitive data — in code, tests,
fixtures, or issue reports.

## Pull Request Expectations

- Keep changes focused on production SDK behavior: token-authenticated REST
  calls, safe SPL generation, search result parsing, and operational
  documentation.
- Document every new or changed public API with XML comments and cover it
  with xUnit tests in `tests/SplunkSdk.Tests` using fake HTTP; unit tests
  must not require a live Splunk instance.
- Prefer semantic v2 search endpoints; keep v1 compatibility only where the
  code already exposes it.
- Use the generated SPL helpers for user-provided index names, field names,
  and values; raw SPL is trusted application-owned input only.
- Keep observability sanitized: no tokens, raw SPL, full URLs, private
  hostnames, search IDs, or event payloads in exceptions, activity tags, or
  metric attributes.
- Keep dependency isolation intact: the core `src/SplunkSdk` package has no
  external NuGet dependencies, Microsoft.Extensions packages stay in
  `src/SplunkSdk.DependencyInjection`, and BenchmarkDotNet stays in
  `benchmarks/SplunkSdk.Benchmarks`.
- Update `CHANGELOG.md` under `Unreleased` for user-visible changes.
- Consult `skills/splunk-interactions/references/splunk-rest-and-spl.md` and
  official Splunk documentation before changing endpoint, auth, or SPL
  behavior.

## Security Issues

Do not report vulnerabilities through public issues or pull requests; follow
`SECURITY.md`.
