# Changelog

All notable changes to this project are documented here.

This project follows Semantic Versioning after `1.0.0`. Until `1.0.0`, minor
versions can include breaking API changes when they are called out in this file.

## [Unreleased]

### Added

- MIT open-source license and NuGet license expression.
- NuGet package metadata, symbol package generation, package README inclusion, and package validation.
- Release workflow for tag-driven package publishing.

## [0.1.0] - 2026-06-07

### Added

- Token-authenticated Splunk REST client with `Bearer` and `Splunk` authorization schemes.
- Search export, search job dispatch, and paged job result retrieval using semantic v2 search endpoints by default.
- Safe SPL query builder for index-scoped searches, field filters, `stats count`, `stats avg`, and `timechart`.
- High-level analytics helpers for error counts, average metrics, and average time series.
- Saved-search and saved-search alert management, including typed dispatch, suppression, email, and summary-index settings.
- JSON and XML Splunk message parsing with sanitized `SplunkApiException` errors.
- Typed result materialization through `SplunkFieldAttribute`.
- Dependency injection package for `IHttpClientFactory`.
- Sanitized `ActivitySource` and `Meter` diagnostics.
- Unit tests with fake HTTP and opt-in live Splunk integration tests.
- BenchmarkDotNet smoke benchmarks for local SPL and result-parser paths.
