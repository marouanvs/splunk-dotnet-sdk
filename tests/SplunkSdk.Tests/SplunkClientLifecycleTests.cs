using System.Net;
using System.Reflection;
using Marouanvs.Splunk.Authentication;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;

namespace Marouanvs.Splunk.Tests;

public sealed class SplunkClientLifecycleTests
{
    [Fact]
    public void CreateAppliesNormalizedBaseAddressAndConfiguredTimeoutToTheOwnedHttpClient()
    {
        var options = new SplunkClientOptions
        {
            // The .invalid TLD is reserved and never resolves, so even a bug
            // that sent a request here could not reach a real host.
            ManagementUri = new Uri("https://splunk.invalid:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            Timeout = TimeSpan.FromSeconds(7)
        };

        using var client = SplunkClient.Create(options);
        var ownedHttpClient = GetOwnedHttpClient(client);

        Assert.Equal(new Uri("https://splunk.invalid:8089/"), ownedHttpClient.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(7), ownedHttpClient.Timeout);
    }

    [Fact]
    public void CreateKeepsTheHttpClientDefaultTimeoutWhenNoTimeoutIsConfigured()
    {
        var options = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.invalid:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token")
        };

        using var client = SplunkClient.Create(options);
        var ownedHttpClient = GetOwnedHttpClient(client);

        // Documented behavior: when SplunkClientOptions.Timeout is unset, the
        // HttpClient default of 100 seconds applies.
        Assert.Equal(TimeSpan.FromSeconds(100), ownedHttpClient.Timeout);
    }

    [Fact]
    public async Task DisposingACreatedClientDisposesTheOwnedHttpClient()
    {
        var options = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.invalid:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token")
        };

        var client = SplunkClient.Create(options);
        client.Dispose();

        // The disposed-client check inside HttpClient runs before any
        // connection is attempted, so this stays hermetic: no network I/O
        // ever happens.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.Search.GetJobStatusAsync("1700000014.1"));
    }

    [Fact]
    public async Task DisposingTheClientLeavesACallerOwnedHttpClientUsable()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"error_count":"1"}]}""");

        using var httpClient = new HttpClient(handler);
        var options = SplunkClientOptions.FromToken(new Uri("https://splunk.example.com:8089"), "test-token");
        var client = new SplunkClient(httpClient, options);

        client.Dispose();

        // The constructor path never takes ownership of the HttpClient, so the
        // caller-owned transport keeps working after the SDK client is
        // disposed.
        var rows = await client.Search.GetResultsAsync("1700000014.2", new SplunkResultRequest { Count = 1 });

        Assert.Equal(1, rows[0].GetInt64("error_count"));
        Assert.Single(handler.Requests);
        handler.AssertNoPendingResponses();
    }

    private static HttpClient GetOwnedHttpClient(SplunkClient client)
    {
        // SplunkClient intentionally keeps the SDK-owned HttpClient private,
        // so the configuration applied by Create is asserted through
        // reflection to keep these tests hermetic (no request, no network).
        var field = typeof(SplunkClient).GetField("_ownedHttpClient", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        return Assert.IsType<HttpClient>(field.GetValue(client));
    }
}
