using System.Net;
using System.Text.Json;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class ErrorSurfaceSanitizationTests
{
    [Fact]
    public async Task UnauthorizedResponsesSurfaceStructuredSplunkMessages()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, """
        {"messages":[{"type":"WARN","text":"call not properly authenticated"},{"type":"INFO","text":"Token may be expired, disabled, or issued by a different Splunk instance"}]}
        """);

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.GetResultsAsync("1700000012.1", new SplunkResultRequest { Count = 1 }));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal(2, exception.Messages.Count);
        Assert.Equal(new SplunkMessage("WARN", "call not properly authenticated"), exception.Messages[0]);
        Assert.Equal(new SplunkMessage("INFO", "Token may be expired, disabled, or issued by a different Splunk instance"), exception.Messages[1]);

        // The server's diagnostic text must survive into Exception.Message so
        // standard logging preserves the authentication reason.
        Assert.Contains("call not properly authenticated", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            "Splunk API request failed with 401 Unauthorized. Splunk messages: WARN: call not properly authenticated; INFO: Token may be expired, disabled, or issued by a different Splunk instance",
            exception.Message);

        // Only the structured message text is surfaced: no raw JSON body
        // fragments and no credentials.
        Assert.DoesNotContain("\"messages\"", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-token", exception.Message, StringComparison.Ordinal);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task OversizedErrorBodiesYieldStatusOnlySanitizedExceptions()
    {
        // 70,000 ASCII bytes exceed the 64 KiB error-body read cap, so the
        // JSON is truncated mid-string and message parsing yields nothing.
        var oversizedText = new string('x', 70_000);
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, $$"""{"messages":[{"type":"ERROR","text":"{{oversizedText}}"}]}""");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.GetResultsAsync("1700000012.2", new SplunkResultRequest { Count = 1 }));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Empty(exception.Messages);
        Assert.Equal("Splunk API request failed with 400 BadRequest.", exception.Message);
        Assert.DoesNotContain("xxxxxxxx", exception.Message, StringComparison.Ordinal);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task UnparseableSuccessResponsesReportOkStatusWithDedicatedReason()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"entry":[{"content":broken}]}""");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
            await client.Search.GetJobStatusAsync("1700000012.3"));

        // The HTTP exchange succeeded, so the exception reports 200 OK with
        // the dedicated "Unparseable response" reason phrase, an empty Splunk
        // message list, and the underlying parse failure preserved as the
        // inner exception (positions only, never payload text).
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("Unparseable response", exception.ReasonPhrase);
        Assert.Empty(exception.Messages);
        Assert.Equal("Splunk returned malformed JSON for a search job status response.", exception.Message);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        handler.AssertNoPendingResponses();
    }
}
