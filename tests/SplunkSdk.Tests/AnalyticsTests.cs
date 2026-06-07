using System.Net;
using Marouanvs.Splunk.Analytics;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class AnalyticsTests
{
    [Fact]
    public async Task AnalyticsCountBuildsSafeSpl()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"7"}}
        """);

        using var client = CreateClient(handler);

        var count = await client.Analytics.CountErrorsAsync(new ErrorCountQuery("team-index")
        {
            Text = "ERROR",
            FieldFilters = new Dictionary<string, string> { ["service"] = "billing-api" },
            TimeRange = SplunkTimeRange.Relative("-15m", "now")
        });

        Assert.Equal(7, count);

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal(
            "search index=\"team-index\" \"ERROR\" service=\"billing-api\" | stats count AS error_count",
            form["search"]);
        Assert.Equal("-15m", form["earliest_time"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task AverageAndTimeSeriesHelpersParseMetrics()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"average_value":"123.45"}}
        """);
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"_time":"1700000000","average_value":"10.5"}}
        {"preview":false,"offset":1,"result":{"_time":"1700000300","average_value":"11.5"}}
        """);

        using var client = CreateClient(handler);

        var average = await client.Analytics.AverageAsync(new AverageMetricQuery("team", "duration_ms")
        {
            Text = "completed"
        });
        var buckets = await client.Analytics.AverageTimeSeriesAsync(new MetricTimeSeriesQuery("team", "duration_ms")
        {
            Span = "5m"
        });

        Assert.Equal(123.45, average);
        Assert.Equal(2, buckets.Count);
        Assert.Equal(10.5, buckets[0].Value);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), buckets[0].Time);

        var averageForm = ParseForm(handler.Requests[0].Body);
        Assert.Equal("search index=\"team\" \"completed\" | stats avg(duration_ms) AS average_value", averageForm["search"]);

        var seriesForm = ParseForm(handler.Requests[1].Body);
        Assert.Equal("search index=\"team\" | timechart span=5m avg(duration_ms) AS average_value", seriesForm["search"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task AggregateHelpersDrainExportStreamsBeforeReturning()
    {
        var countHandler = new QueueHttpMessageHandler();
        countHandler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"9"}}
        {"messages":[{"type":"FATAL","text":"The count search failed after producing an aggregate row."}]}
        """);

        using (var client = CreateClient(countHandler))
        {
            var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
                await client.Analytics.CountErrorsAsync(new ErrorCountQuery("team")));

            Assert.Equal("FATAL", Assert.Single(exception.Messages).Type);
        }

        var averageHandler = new QueueHttpMessageHandler();
        averageHandler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"average_value":"123.45"}}
        {"messages":[{"type":"ERROR","text":"The average search failed after producing an aggregate row."}]}
        """);

        using (var client = CreateClient(averageHandler))
        {
            var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
                await client.Analytics.AverageAsync(new AverageMetricQuery("team", "duration_ms")));

            Assert.Equal("ERROR", Assert.Single(exception.Messages).Type);
        }

        countHandler.AssertNoPendingResponses();
        averageHandler.AssertNoPendingResponses();
    }
}
