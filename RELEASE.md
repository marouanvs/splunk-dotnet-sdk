# Release Process

This repository publishes two NuGet packages:

- `Marouanvs.SplunkSdk`
- `Marouanvs.SplunkSdk.DependencyInjection`

The package IDs are owner-prefixed to avoid NuGet.org conflicts with the
legacy `SplunkSDK` package ID, which is already owned by Splunk.

## Version Policy

- `0.x` releases are preview releases. Breaking changes are allowed when they are documented in `CHANGELOG.md`.
- `1.0.0` should wait until the SDK has live validation against Splunk Enterprise and Splunk Cloud.
- After `1.0.0`, use Semantic Versioning:
  - Patch: compatible bug fixes.
  - Minor: compatible public API additions.
  - Major: public API breaks, behavior breaks, or changes that require caller migration.

## Release Checklist

1. Confirm `RepositoryUrl` and `PackageProjectUrl` point to the public repository. GitHub Actions fills these from `GITHUB_SERVER_URL` and `GITHUB_REPOSITORY`.
2. Update `CHANGELOG.md`.
3. Run:

   ```bash
   dotnet format SplunkSdk.slnx --verify-no-changes
   dotnet test tests/SplunkSdk.Tests/SplunkSdk.Tests.csproj
   dotnet test tests/SplunkSdk.IntegrationTests/SplunkSdk.IntegrationTests.csproj
   dotnet pack src/SplunkSdk/SplunkSdk.csproj -c Release -o artifacts/packages
   dotnet pack src/SplunkSdk.DependencyInjection/SplunkSdk.DependencyInjection.csproj -c Release -o artifacts/packages
   ```

4. Create and push a tag in the form `vX.Y.Z`.

## NuGet Publishing

The `release.yml` workflow packs both packages and uploads `.nupkg` and `.snupkg`
files as workflow artifacts. On `v*.*.*` tags, it also publishes to NuGet.org
using the `NUGET_API_KEY` repository secret.

## API Compatibility

Packable projects have `EnablePackageValidation=true`. This validates package
layout and public API consistency during `dotnet pack`.

After the first public release, compare against the latest published baseline by
passing `PackageValidationBaselineVersion`:

```bash
dotnet pack src/SplunkSdk/SplunkSdk.csproj -c Release -p:PackageValidationBaselineVersion=0.1.0
dotnet pack src/SplunkSdk.DependencyInjection/SplunkSdk.DependencyInjection.csproj -c Release -p:PackageValidationBaselineVersion=0.1.0
```
