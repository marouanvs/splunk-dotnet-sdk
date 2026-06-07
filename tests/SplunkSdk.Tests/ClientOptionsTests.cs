using System.Net;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class ClientOptionsTests
{
    [Fact]
    public async Task DefaultUserAgentUsesSdkProductTokenAndSemanticVersion()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[]}""");

        using var client = CreateClient(handler);

        await client.Search.GetResultsAsync("1700000000.8", new SplunkResultRequest { Count = 1 });

        var sent = Assert.Single(handler.Requests);

        // Assert the literal expected shape (Marouanvs.Splunk product token plus a
        // semantic version with any +build metadata stripped) instead of
        // re-deriving the value from the same assembly attribute the SDK reads.
        Assert.StartsWith("Marouanvs.Splunk/", sent.UserAgent, StringComparison.Ordinal);
        Assert.Matches(
            @"^Marouanvs\.Splunk/\d+\.\d+\.\d+(-[0-9A-Za-z]+(\.[0-9A-Za-z-]+)*)?$",
            sent.UserAgent);
        handler.AssertNoPendingResponses();
    }
}
