using System.Net;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class ApiExceptionTests
{
    [Fact]
    public async Task ApiExceptionsParseSplunkMessages()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, """
        <response><messages><msg type="ERROR">requires capability: search</msg></messages></response>
        """, "application/xml");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\"")).ToListAsync());

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Single(exception.Messages);
        Assert.Equal("ERROR", exception.Messages[0].Type);
        Assert.Equal("requires capability: search", exception.Messages[0].Text);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ApiExceptionMessageDoesNotEchoRawResponseBodies()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, """
        search index="secret_payments" card_number=4111111111111111
        """);

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"secret_payments\"")).ToListAsync());

        Assert.DoesNotContain("secret_payments", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("4111111111111111", exception.Message, StringComparison.Ordinal);
        handler.AssertNoPendingResponses();
    }
}
