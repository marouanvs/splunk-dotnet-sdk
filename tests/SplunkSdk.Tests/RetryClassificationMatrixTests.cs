using System.Net;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class RetryClassificationMatrixTests
{
    // 503 Service Unavailable for GET and DELETE is already covered by
    // RetryAndResilienceTests, so the matrix here covers the remaining
    // transient statuses: 429, 500, 502, and 504.
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task TransientStatusCodesAreRetriedForGetRequests(HttpStatusCode statusCode)
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(statusCode, """{"messages":[{"type":"WARN","text":"transient"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"error_count":"1"}]}""");

        using var client = CreateClient(handler, maxRetries: 1);

        var rows = await client.Search.GetResultsAsync("1700000010.1", new SplunkResultRequest { Count = 1 });

        Assert.Equal(1, rows[0].GetInt64("error_count"));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        handler.AssertNoPendingResponses();
    }

    // The SDK's transient set is exactly 429, 500, 502, 503, and 504. Client
    // errors, including 408 Request Timeout, surface immediately even though
    // retries are enabled and the request method is idempotent.
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task NonTransientStatusCodesAreNeverRetriedForGetRequests(HttpStatusCode statusCode)
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(statusCode, """{"messages":[{"type":"ERROR","text":"denied"}]}""");

        using var client = CreateClient(handler, maxRetries: 2);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.GetResultsAsync("1700000010.2", new SplunkResultRequest { Count = 1 }));

        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Single(handler.Requests);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task TransportFailuresAreRetriedForGetRequests()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueException(new HttpRequestException("Connection reset by peer."));
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"error_count":"1"}]}""");

        using var client = CreateClient(handler, maxRetries: 1);

        var rows = await client.Search.GetResultsAsync("1700000010.3", new SplunkResultRequest { Count = 1 });

        Assert.Equal(1, rows[0].GetInt64("error_count"));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task TransportFailuresAreRetriedForDeleteRequests()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueException(new HttpRequestException("Connection reset by peer."));
        handler.Enqueue(HttpStatusCode.OK, """{"messages":[{"type":"INFO","text":"deleted"}]}""");

        using var client = CreateClient(handler, maxRetries: 1);

        await client.SavedSearches.DeleteAsync("checkout_errors");

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Delete, request.Method));
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task TransportFailuresAreNotRetriedForPostRequests()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueException(new HttpRequestException("Connection reset by peer."));

        // Intentionally over-enqueued: the success response must never be
        // consumed because non-idempotent POSTs are not retried, so this test
        // deliberately skips AssertNoPendingResponses().
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"1"}}
        """);

        using var client = CreateClient(handler, maxRetries: 1);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync());

        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
    }
}
