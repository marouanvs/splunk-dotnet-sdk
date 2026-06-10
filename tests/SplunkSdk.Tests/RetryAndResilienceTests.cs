using System.Net;
using System.Security.Authentication;
using Marouanvs.Splunk.Authentication;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class RetryAndResilienceTests
{
    [Fact]
    public async Task TransientResponsesAreRetried()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"busy"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """
        {"results":[{"error_count":"1"}]}
        """);

        using var client = CreateClient(handler, maxRetries: 1);

        var rows = await client.Search.GetResultsAsync("1700000000.4", new SplunkResultRequest { Count = 1 });

        Assert.Equal(1, rows[0].GetInt64("error_count"));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task RetryGateRetriesDeleteRequests()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"busy"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """{"messages":[{"type":"INFO","text":"deleted"}]}""");

        using var client = CreateClient(handler, maxRetries: 1);

        await client.SavedSearches.DeleteAsync("checkout_errors");

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Delete, request.Method));
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task NonIdempotentPostsAreNotRetried()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"busy"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"1"}}
        """);

        using var client = CreateClient(handler, maxRetries: 1);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NonIdempotentPostTimeoutsAreNotRetried()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueException(new TaskCanceledException("Simulated client timeout."));
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"1"}}
        """);

        using var client = CreateClient(handler, maxRetries: 1);

        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync());

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TlsCertificateFailuresAreConfigurationErrorsAndAreNotRetried()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueException(new HttpRequestException(
            "The SSL connection could not be established.",
            new AuthenticationException("The remote certificate is invalid according to the validation procedure.")));
        handler.Enqueue(HttpStatusCode.OK, """{"results":[]}""");

        using var client = CreateClient(handler, maxRetries: 1);

        var exception = await Assert.ThrowsAsync<SplunkConfigurationException>(async () =>
            await client.Search.GetResultsAsync("1700000000.9", new SplunkResultRequest { Count = 1 }));

        Assert.Contains("TLS certificate validation failed", exception.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void RetryValidationRejectsZeroBackoffWhenRetriesAreEnabled()
    {
        var handler = new QueueHttpMessageHandler();

        var zeroBaseDelay = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            Retry = new SplunkRetryOptions
            {
                MaxRetries = 1,
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.FromMilliseconds(1)
            }
        };

        Assert.Throws<SplunkConfigurationException>(() => new SplunkClient(new HttpClient(handler), zeroBaseDelay));

        var zeroMaxDelay = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            Retry = new SplunkRetryOptions
            {
                MaxRetries = 1,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.Zero
            }
        };

        Assert.Throws<SplunkConfigurationException>(() => new SplunkClient(new HttpClient(handler), zeroMaxDelay));
    }

    [Fact]
    public async Task RetryBackoffIsBoundedByMaxDelayAndValidationRejectsInvertedDelays()
    {
        var handler = new QueueHttpMessageHandler();

        // MaxDelay < BaseDelay is rejected at client construction so jittered
        // exponential backoff can never overflow or invert its bounds.
        var invertedDelays = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            Retry = new SplunkRetryOptions
            {
                MaxRetries = 2,
                BaseDelay = TimeSpan.MaxValue,
                MaxDelay = TimeSpan.FromMilliseconds(1)
            }
        };

        var exception = Assert.Throws<SplunkConfigurationException>(
            () => new SplunkClient(new HttpClient(handler), invertedDelays));
        Assert.Equal("MaxDelay must be greater than or equal to BaseDelay.", exception.Message);

        // With valid bounds, repeated transient failures still retry to success.
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"busy"}]}""");
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"still busy"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"error_count":"1"}]}""");

        using var client = CreateClient(
            handler,
            new SplunkRetryOptions
            {
                MaxRetries = 2,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(2)
            });

        var rows = await client.Search.GetResultsAsync("1700000000.5", new SplunkResultRequest { Count = 1 });

        Assert.Equal(1, rows[0].GetInt64("error_count"));
        Assert.Equal(3, handler.Requests.Count);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task SdkRetriesCanBeDisabledForHostOwnedResilience()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"busy"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"1"}}
        """);

        using var client = CreateClient(handler, maxRetries: 0);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Single(handler.Requests);
    }
}
