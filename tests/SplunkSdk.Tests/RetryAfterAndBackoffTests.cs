using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Marouanvs.Splunk.Authentication;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.Models;
using Xunit;

namespace Marouanvs.Splunk.Tests;

public sealed class RetryAfterAndBackoffTests
{
    [Fact]
    public async Task RetryAfterDeltaIsHonoredAboveMaxDelay()
    {
        var handler = new RetryAfterQueueHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.TooManyRequests,
            """{"messages":[{"type":"WARN","text":"search quota exceeded"}]}""",
            retryAfter: TimeSpan.FromSeconds(1));
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"error_count":"1"}]}""");

        using var client = CreateRetryClient(handler, new SplunkRetryOptions
        {
            MaxRetries = 1,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(50),
            MaxServerDelay = TimeSpan.FromSeconds(10)
        });

        var rows = await client.Search.GetResultsAsync("1700000011.1", new SplunkResultRequest { Count = 1 });

        Assert.Equal(1, rows[0].GetInt64("error_count"));
        Assert.Equal(2, handler.RequestTimes.Count);

        // The server requested a one-second wait, which must be honored even
        // though it is far above MaxDelay (50 ms). Task.Delay never completes
        // early, so the measured gap is at least roughly the requested delay;
        // the 900 ms floor allows for clock granularity.
        var backoff = handler.RequestTimes[1] - handler.RequestTimes[0];
        Assert.True(
            backoff >= TimeSpan.FromMilliseconds(900),
            $"Expected the server-requested 1s delay to be honored above MaxDelay, but the retry happened after {Format(backoff)} ms.");
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task RetryAfterBeyondMaxServerDelaySurfacesErrorWithoutRetrying()
    {
        var handler = new RetryAfterQueueHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.ServiceUnavailable,
            """{"messages":[{"type":"WARN","text":"busy"}]}""",
            retryAfter: TimeSpan.FromSeconds(1));

        using var client = CreateRetryClient(handler, new SplunkRetryOptions
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(50),
            MaxServerDelay = TimeSpan.FromMilliseconds(200)
        });

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.GetResultsAsync("1700000011.2", new SplunkResultRequest { Count = 1 }));

        // A server-requested delay above MaxServerDelay disables the retry
        // entirely: the response error surfaces immediately and no second
        // request is sent despite the remaining retry budget.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("busy", exception.Message, StringComparison.Ordinal);
        Assert.Single(handler.RequestTimes);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task RetryAfterZeroRetriesImmediately()
    {
        var handler = new RetryAfterQueueHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.TooManyRequests,
            """{"messages":[{"type":"WARN","text":"busy"}]}""",
            retryAfter: TimeSpan.Zero);
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"error_count":"1"}]}""");

        // The deliberately huge backoff bounds prove that Retry-After: 0
        // bypasses the jittered exponential delay; a fallback to backoff
        // would usually stall this test for several seconds.
        using var client = CreateRetryClient(handler, new SplunkRetryOptions
        {
            MaxRetries = 1,
            BaseDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromSeconds(10)
        });

        var rows = await client.Search.GetResultsAsync("1700000011.3", new SplunkResultRequest { Count = 1 });

        Assert.Equal(1, rows[0].GetInt64("error_count"));
        Assert.Equal(2, handler.RequestTimes.Count);

        var backoff = handler.RequestTimes[1] - handler.RequestTimes[0];
        Assert.True(
            backoff < TimeSpan.FromSeconds(3),
            $"Expected an immediate retry for Retry-After: 0, but the retry happened after {Format(backoff)} ms.");
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task JitteredExponentialBackoffStaysWithinConfiguredCaps()
    {
        var handler = new RetryAfterQueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"busy"}]}""");
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"still busy"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"error_count":"1"}]}""");

        using var client = CreateRetryClient(handler, new SplunkRetryOptions
        {
            MaxRetries = 2,
            BaseDelay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromMilliseconds(150)
        });

        var rows = await client.Search.GetResultsAsync("1700000011.4", new SplunkResultRequest { Count = 1 });

        Assert.Equal(1, rows[0].GetInt64("error_count"));
        Assert.Equal(3, handler.RequestTimes.Count);

        // Full jitter draws each wait uniformly from [0, cap], where the cap
        // doubles from BaseDelay and is clamped at MaxDelay: 100 ms before the
        // first retry, then min(200, 150) = 150 ms before the second. The
        // asserted upper bounds add a generous scheduling allowance so the
        // test stays deterministic under load while still failing for
        // second-scale unbounded backoff regressions.
        var firstBackoff = handler.RequestTimes[1] - handler.RequestTimes[0];
        var secondBackoff = handler.RequestTimes[2] - handler.RequestTimes[1];
        Assert.InRange(firstBackoff, TimeSpan.Zero, TimeSpan.FromMilliseconds(100 + 2500));
        Assert.InRange(secondBackoff, TimeSpan.Zero, TimeSpan.FromMilliseconds(150 + 2500));
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task CancellationDuringRetryBackoffPropagatesPromptly()
    {
        var handler = new RetryAfterQueueHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.ServiceUnavailable,
            """{"messages":[{"type":"WARN","text":"busy"}]}""",
            retryAfter: TimeSpan.FromSeconds(20));

        using var client = CreateRetryClient(handler, new SplunkRetryOptions
        {
            MaxRetries = 1,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(50)
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.Search.GetResultsAsync("1700000011.5", new SplunkResultRequest { Count = 1 }, cancellation.Token));
        stopwatch.Stop();

        // Cancellation must interrupt the twenty-second server-requested
        // backoff instead of waiting it out, and no further request may be
        // sent. The ten-second ceiling leaves a very wide scheduling margin
        // while still proving the backoff did not run to completion.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Expected cancellation to interrupt the backoff promptly, but the call took {Format(stopwatch.Elapsed)} ms.");
        Assert.Single(handler.RequestTimes);
        handler.AssertNoPendingResponses();
    }

    private static string Format(TimeSpan value) =>
        value.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);

    private static SplunkClient CreateRetryClient(RetryAfterQueueHttpMessageHandler handler, SplunkRetryOptions retry) =>
        new(new HttpClient(handler), new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            Retry = retry
        });

    // Queue-based fake transport that can attach a Retry-After delta to queued
    // responses and records a monotonic timestamp per request so tests can
    // assert backoff timing without any network access. Kept private to this
    // class because only Retry-After and backoff-timing tests need it; other
    // tests should keep using the shared QueueHttpMessageHandler.
    private sealed class RetryAfterQueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public List<TimeSpan> RequestTimes { get; } = [];

        public void Enqueue(HttpStatusCode statusCode, string body, TimeSpan? retryAfter = null)
        {
            _responses.Enqueue(() =>
            {
                var response = new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    ReasonPhrase = statusCode.ToString()
                };

                if (retryAfter is { } delta)
                {
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
                }

                return response;
            });
        }

        public void AssertNoPendingResponses()
        {
            Assert.True(
                _responses.Count == 0,
                $"Expected every queued fake response to be consumed, but {_responses.Count} response(s) remained.");
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No fake response was queued.");
            }

            RequestTimes.Add(_clock.Elapsed);
            return Task.FromResult(_responses.Dequeue()());
        }
    }
}
