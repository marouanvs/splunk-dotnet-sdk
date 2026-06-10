# Release Process

This repository publishes two NuGet packages:

- `Marouanvs.Splunk`
- `Marouanvs.Splunk.DependencyInjection`

The package IDs are owner-prefixed to avoid NuGet.org conflicts with the
legacy `SplunkSDK` package ID, which is already owned by Splunk.

## Version Policy

- `0.x` releases are preview releases. Breaking changes are allowed when they are documented in `CHANGELOG.md`.
- `1.0.0` should wait until the SDK has live validation against Splunk Enterprise and Splunk Cloud.
- After `1.0.0`, use Semantic Versioning:
  - Patch: compatible bug fixes.
  - Minor: compatible public API additions.
  - Major: public API breaks, behavior breaks, or changes that require caller migration.

## Canonical Pack Path

The `release.yml` GitHub Actions workflow is the canonical way to produce
publishable packages. Only CI builds receive `RepositoryUrl` and
`PackageProjectUrl` metadata, which GitHub Actions injects from
`GITHUB_SERVER_URL` and `GITHUB_REPOSITORY`.

Local `dotnet pack` runs are for inspection only — locally built packages
intentionally lack repository and project URL metadata so private hostnames
never leak into package files. Never push a locally built package to
NuGet.org.

## Release Environment Gate

The release job runs in the `release` GitHub environment:

- Configure required reviewers for the `release` environment in the
  repository settings (Settings → Environments → release) so every release
  run needs explicit human approval. The workflow itself cannot enforce
  this; it must be configured in repo settings.
- Store the `NUGET_API_KEY` secret in the `release` environment rather than
  as a repository-wide secret.

Before publishing, the workflow also verifies that the tagged commit is
reachable from `origin/main` and refuses to publish otherwise.

## Release Checklist

1. Update `CHANGELOG.md`: move the entries shipping in this release from
   `Unreleased` into a new version section.
2. Verify locally with the same gates CI enforces:

   ```bash
   dotnet format SplunkSdk.slnx --verify-no-changes
   dotnet build SplunkSdk.slnx
   dotnet test tests/SplunkSdk.Tests/SplunkSdk.Tests.csproj
   dotnet test tests/SplunkSdk.IntegrationTests/SplunkSdk.IntegrationTests.csproj
   ```

3. Optional: inspect package contents locally with `dotnet pack ... -o
   artifacts/packages`. Remember these artifacts lack repository metadata
   and must not be published.
4. Merge the release commit to `main`. The workflow refuses to publish a
   commit that is not reachable from `origin/main`.
5. Create and push a tag in the form `vX.Y.Z` on that commit.
6. Approve the `release` environment run when prompted, then confirm the
   workflow published both packages and uploaded the `.nupkg`/`.snupkg`
   artifacts.

A manual `workflow_dispatch` run with `publish` enabled follows the same
gates: environment approval, the main-reachability check, and the API
baseline.

## API Compatibility

Packable projects have `EnablePackageValidation=true`. This validates package
layout and public API consistency during `dotnet pack`.

The release workflow derives `PackageValidationBaselineVersion` automatically
from the most recent stable `v*.*.*` tag older than the version being
released, so breaking-change detection gates every publish once the first
version is tagged. For the first release, when no prior tag exists, packing
proceeds without a baseline. A manual dispatch can override the derived
baseline with the `baseline_version` input.

To reproduce the baseline check locally for inspection:

```bash
dotnet pack src/SplunkSdk/SplunkSdk.csproj -c Release -p:PackageValidationBaselineVersion=<previous-version>
dotnet pack src/SplunkSdk.DependencyInjection/SplunkSdk.DependencyInjection.csproj -c Release -p:PackageValidationBaselineVersion=<previous-version>
```
