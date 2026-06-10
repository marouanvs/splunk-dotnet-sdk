using System.Net;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class AnalyticsPreviewHandlingTests
{
    [Fact]
    public async Task CountErrorsSendsPreviewFalseAndIgnoresPreviewFrames()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":true,"offset":0,"result":{"error_count":"100"}}
        {"preview":false,"offset":0,"result":{"error_count":"7"}}
        """);

        using var client = CreateClient(handler);

        var count = await client.Analytics.CountErrorsAsync(new ErrorCountQuery("team"));

        Assert.Equal(7, count);

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal("false", form["preview"]);
        Assert.Equal("1", form["count"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task AverageSendsPreviewFalseAndUsesOnlyTheFinalAggregateRow()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":true,"offset":0,"result":{"average_value":"999.99"}}
        {"preview":false,"offset":0,"result":{"average_value":"123.45"}}
        """);

        using var client = CreateClient(handler);

        var average = await client.Analytics.AverageAsync(new AverageMetricQuery("team", "duration_ms"));

        Assert.Equal(123.45, average);

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal("false", form["preview"]);
        Assert.Equal("1", form["count"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task AverageTimeSeriesSendsPreviewFalseAndSkipsPreviewBuckets()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":true,"offset":0,"result":{"_time":"1700000000","average_value":"1.5"}}
        {"preview":true,"offset":1,"result":{"_time":"1700000300","average_value":"2.5"}}
        {"preview":false,"offset":0,"result":{"_time":"1700000000","average_value":"10.5"}}
        {"preview":false,"offset":1,"result":{"_time":"1700000300","average_value":"11.5"}}
        """);

        using var client = CreateClient(handler);

        var buckets = await client.Analytics.AverageTimeSeriesAsync(
            new MetricTimeSeriesQuery("team", "duration_ms"));

        Assert.Equal(2, buckets.Count);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), buckets[0].Time);
        Assert.Equal(10.5, buckets[0].Value);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000300), buckets[1].Time);
        Assert.Equal(11.5, buckets[1].Value);

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal("false", form["preview"]);
        Assert.Equal("0", form["count"]);
        handler.AssertNoPendingResponses();
    }
}
