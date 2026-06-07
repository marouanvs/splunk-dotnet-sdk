using SplunkSdk;
using SplunkSdk.Authentication;
using SplunkSdk.Configuration;
using SplunkSdk.Mapping;
using SplunkSdk.Models;
using SplunkSdk.Search;
using Xunit;

namespace SplunkSdk.IntegrationTests;

// These tests exercise the SDK against a real Splunk management endpoint when
// SPLUNKSDK_INTEGRATION_* environment variables are present. The default local
// and CI behavior is to skip them so unit test runs never require live Splunk.
public sealed class SplunkIntegrationTests
{
    // Read-only smoke test for the streaming export path. It runs one bounded
    // generated SPL query against the configured index and validates the row shape.
    [SplunkIntegrationFact]
    public async Task ExportCountFromConfiguredIndex()
    {
        var settings = IntegrationSettings.LoadRequired();
        using var client = CreateClient(settings);
        var request = CountRequest(settings);

        var rows = await client.Search.ExportAsync(request).ToListAsync();

        Assert.Single(rows);
        Assert.True(rows[0].GetInt64("result_count") is >= 0, "Expected a non-negative result_count.");
    }

    // Read-only smoke test for the job lifecycle path: dispatch a blocking job,
    // then retrieve a bounded page of results through jobs/{sid}/results.
    [SplunkIntegrationFact]
    public async Task BlockingJobLifecycleReturnsResults()
    {
        var settings = IntegrationSettings.LoadRequired();
        using var client = CreateClient(settings);
        var job = await client.Search.StartAsync(CountRequest(settings), SplunkExecutionMode.Blocking);
        var rows = await client.Search.GetResultsAsync(job.Sid, new SplunkResultRequest { Count = 1 });

        Assert.Single(rows);
        Assert.True(rows[0].GetInt64("result_count") is >= 0, "Expected a non-negative result_count.");
    }

    // Verifies that rows returned by live Splunk can flow through the public
    // typed materialization extension, not only through raw field access.
    [SplunkIntegrationFact]
    public async Task TypedMaterializationMapsIntegrationRows()
    {
        var settings = IntegrationSettings.LoadRequired();
        using var client = CreateClient(settings);
        var row = await client.Search.ExportAsync(CountRequest(settings)).ToObjectsAsync<CountRow>().FirstAsync();

        Assert.True(row.ResultCount >= 0, "Expected typed result_count to be non-negative.");
    }

    // Read-only smoke test for a complete trusted SPL string supplied by the
    // user. This proves the raw SplunkSearchRequest path against live Splunk
    // without logging the SPL or returned field values.
    [SplunkCustomSplFact]
    public async Task RawSplRequestFromEnvironmentReturnsRows()
    {
        var settings = IntegrationSettings.LoadConnectionRequired();
        using var client = CreateClient(settings);

        var rows = await client.Search.ExportAsync(new SplunkSearchRequest(settings.CustomSpl!)
        {
            Count = 1
        }).ToListAsync();

        var row = Assert.Single(rows);
        Assert.NotEmpty(row.Fields);
    }

    // Mutation test for saved-search CRUD and dispatch. It creates a unique,
    // unscheduled knowledge object and always attempts cleanup in finally.
    [SplunkMutationFact]
    public async Task SavedSearchMutationLifecycle()
    {
        var settings = IntegrationSettings.LoadRequired();
        using var client = CreateClient(settings);
        var name = "splunksdk_integration_" + Guid.NewGuid().ToString("N");

        try
        {
            var created = await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest(name, CountRequest(settings).Search)
            {
                Namespace = settings.Namespace,
                Description = "SplunkSdk integration test saved search",
                IsScheduled = false,
                TimeRange = SplunkTimeRange.Last(TimeSpan.FromMinutes(15))
            });
            var fetched = await client.SavedSearches.GetAsync(name, settings.Namespace);
            var job = await client.SavedSearches.DispatchAsync(name, new SplunkDispatchSavedSearchRequest
            {
                Namespace = settings.Namespace
            });

            Assert.Equal(name, created.Name);
            Assert.Equal(name, fetched?.Name);
            Assert.False(string.IsNullOrWhiteSpace(job.Sid), "Expected dispatch to return a sid.");
        }
        finally
        {
            await IgnoreNotFoundAsync(() => client.SavedSearches.DeleteAsync(name, settings.Namespace));
        }
    }

    // Mutation test for alert creation. The alert is created disabled so a test
    // run does not schedule notifications or alert actions in the target stack.
    [SplunkMutationFact]
    public async Task DisabledAlertMutationLifecycle()
    {
        var settings = IntegrationSettings.LoadRequired();
        using var client = CreateClient(settings);
        var name = "splunksdk_alert_integration_" + Guid.NewGuid().ToString("N");

        try
        {
            var created = await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(name, CountRequest(settings).Search, "*/15 * * * *")
            {
                Namespace = settings.Namespace,
                Description = "SplunkSdk integration test alert",
                Disabled = true,
                TimeRange = SplunkTimeRange.Last(TimeSpan.FromMinutes(15)),
                Alert = new SplunkAlertSettings
                {
                    AlertType = SplunkAlertType.NumberOfEvents,
                    Comparator = SplunkAlertComparator.GreaterThan,
                    Threshold = "0",
                    Severity = SplunkAlertSeverity.Info,
                    Track = true,
                    DigestMode = true
                }
            });

            Assert.Equal(name, created.Name);
            Assert.True(created.IsScheduled);
            Assert.True(created.Disabled);
        }
        finally
        {
            await IgnoreNotFoundAsync(() => client.SavedSearches.DeleteAsync(name, settings.Namespace));
        }
    }

    private static SplunkSearchRequest CountRequest(IntegrationSettings settings) =>
        new(
            SplunkQueryBuilder.FromIndex(settings.Index)
                .StatsCount("result_count")
                .Build())
        {
            TimeRange = SplunkTimeRange.Last(TimeSpan.FromMinutes(15)),
            Count = 1
        };

    // Centralizes client construction so every live test uses the same auth
    // scheme, optional namespace, and explicit local-lab TLS behavior. If a
    // read-only test passes only with AllowUntrustedCertificates enabled, the
    // remaining issue is certificate trust or hostname validation.
    private static SplunkClient CreateClient(IntegrationSettings settings)
    {
        var options = new SplunkClientOptions
        {
            ManagementUri = settings.ManagementUri,
            TokenProvider = new StaticSplunkTokenProvider(settings.Token),
            AuthorizationScheme = settings.AuthorizationScheme,
            DefaultNamespace = settings.Namespace
        };

        if (!settings.AllowUntrustedCertificates)
        {
            return SplunkClient.Create(options);
        }

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        return new SplunkClient(new HttpClient(handler), options);
    }

    // Cleanup helpers tolerate a missing object because failed creates, manual
    // cleanup, or Splunk-side race conditions should not mask the original result.
    private static async Task IgnoreNotFoundAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (SplunkApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
        }
    }
}

// Marks read-only live tests. They run only when endpoint, token, and index are
// configured, which keeps normal local and CI test runs hermetic.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SplunkIntegrationFactAttribute : FactAttribute
{
    public SplunkIntegrationFactAttribute()
    {
        if (!IntegrationSettings.HasRequiredEnvironment())
        {
            Skip = "Set SPLUNKSDK_INTEGRATION_URI, SPLUNKSDK_INTEGRATION_TOKEN, and SPLUNKSDK_INTEGRATION_INDEX to run live Splunk integration tests.";
        }
    }
}

// Marks tests that create or delete Splunk knowledge objects. These require the
// normal live settings plus SPLUNKSDK_INTEGRATION_MUTATE=1.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SplunkMutationFactAttribute : FactAttribute
{
    public SplunkMutationFactAttribute()
    {
        if (!IntegrationSettings.HasRequiredEnvironment())
        {
            Skip = "Set SPLUNKSDK_INTEGRATION_URI, SPLUNKSDK_INTEGRATION_TOKEN, and SPLUNKSDK_INTEGRATION_INDEX to run live Splunk integration tests.";
            return;
        }

        if (!IntegrationSettings.IsMutationEnabled())
        {
            Skip = "Set SPLUNKSDK_INTEGRATION_MUTATE=1 to create/delete saved searches and alerts.";
        }
    }
}

// Marks a read-only live test that executes a complete trusted SPL request
// supplied by the caller. It needs only endpoint, token, and SPL because the SPL
// itself may use generating commands or fully-owned index predicates.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SplunkCustomSplFactAttribute : FactAttribute
{
    public SplunkCustomSplFactAttribute()
    {
        if (!IntegrationSettings.HasConnectionEnvironment())
        {
            Skip = "Set SPLUNKSDK_INTEGRATION_URI and SPLUNKSDK_INTEGRATION_TOKEN to run custom SPL integration tests.";
            return;
        }

        if (!IntegrationSettings.HasCustomSpl())
        {
            Skip = "Set SPLUNKSDK_INTEGRATION_SPL to run the custom raw SPL integration test.";
        }
    }
}

// Environment-backed live Splunk settings. Tokens stay in environment variables
// or GitHub environment secrets and are never written to test output.
internal sealed record IntegrationSettings(
    Uri ManagementUri,
    string Token,
    string Index,
    string? CustomSpl,
    SplunkAuthorizationScheme AuthorizationScheme,
    SplunkNamespace? Namespace,
    bool AllowUntrustedCertificates,
    bool EnableMutations)
{
    public static bool HasConnectionEnvironment() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_URI")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_TOKEN"));

    public static bool HasRequiredEnvironment() =>
        HasConnectionEnvironment() &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_INDEX"));

    public static bool HasCustomSpl() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_SPL"));

    public static bool IsMutationEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_MUTATE"), "1", StringComparison.Ordinal);

    public static IntegrationSettings LoadRequired() =>
        TryLoad(requireIndex: true) ?? throw new InvalidOperationException(
            "Set SPLUNKSDK_INTEGRATION_URI, SPLUNKSDK_INTEGRATION_TOKEN, and SPLUNKSDK_INTEGRATION_INDEX to run live Splunk integration tests.");

    public static IntegrationSettings LoadConnectionRequired() =>
        TryLoad(requireIndex: false) ?? throw new InvalidOperationException(
            "Set SPLUNKSDK_INTEGRATION_URI and SPLUNKSDK_INTEGRATION_TOKEN to run custom SPL integration tests.");

    public static IntegrationSettings? TryLoad(bool requireIndex)
    {
        var uri = Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_URI");
        var token = Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_TOKEN");
        var index = Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_INDEX");
        var customSpl = Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_SPL");

        if (string.IsNullOrWhiteSpace(uri) ||
            string.IsNullOrWhiteSpace(token) ||
            (requireIndex && string.IsNullOrWhiteSpace(index)))
        {
            return null;
        }

        var scheme = string.Equals(Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_AUTH_SCHEME"), "Splunk", StringComparison.OrdinalIgnoreCase)
            ? SplunkAuthorizationScheme.Splunk
            : SplunkAuthorizationScheme.Bearer;

        var owner = Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_OWNER");
        var app = Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_APP");
        var splunkNamespace = !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(app)
            ? SplunkNamespace.Create(owner, app)
            : null;

        return new IntegrationSettings(
            new Uri(uri, UriKind.Absolute),
            token,
            index ?? string.Empty,
            string.IsNullOrWhiteSpace(customSpl) ? null : customSpl,
            scheme,
            splunkNamespace,
            string.Equals(Environment.GetEnvironmentVariable("SPLUNKSDK_INTEGRATION_ALLOW_UNTRUSTED_CERTS"), "1", StringComparison.Ordinal),
            IsMutationEnabled());
    }
}

internal sealed class CountRow
{
    [SplunkField("result_count")]
    public long ResultCount { get; set; }
}

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> values)
    {
        var results = new List<T>();
        await foreach (var value in values)
        {
            results.Add(value);
        }

        return results;
    }

    public static async Task<T> FirstAsync<T>(this IAsyncEnumerable<T> values)
    {
        await foreach (var value in values)
        {
            return value;
        }

        throw new InvalidOperationException("Sequence contained no elements.");
    }
}
