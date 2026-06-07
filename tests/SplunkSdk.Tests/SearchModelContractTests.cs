using System.Net;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class SearchModelContractTests
{
    [Fact]
    public async Task AbsoluteTimeRangeEmitsMillisecondPrecisionForBothBounds()
    {
        var range = SplunkTimeRange.Absolute(
            DateTimeOffset.FromUnixTimeMilliseconds(1700000000123),
            DateTimeOffset.FromUnixTimeMilliseconds(1700000000987));

        Assert.Equal("1700000000.123", range.Earliest);
        Assert.Equal("1700000000.987", range.Latest);

        var trimmed = SplunkTimeRange.Absolute(
            DateTimeOffset.FromUnixTimeSeconds(1700000000),
            DateTimeOffset.FromUnixTimeMilliseconds(1700000000500));

        Assert.Equal("1700000000", trimmed.Earliest);
        Assert.Equal("1700000000.5", trimmed.Latest);

        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"1"}}
        """);

        using var client = CreateClient(handler);

        await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")
        {
            TimeRange = range
        }).ToListAsync();

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal("1700000000.123", form["earliest_time"]);
        Assert.Equal("1700000000.987", form["latest_time"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public Task AbsoluteTimeRangeRequiresLatestAfterEarliest()
    {
        var instant = DateTimeOffset.FromUnixTimeSeconds(1700000000);

        var equalBounds = Assert.Throws<ArgumentException>(() => SplunkTimeRange.Absolute(instant, instant));
        Assert.Equal("latest", equalBounds.ParamName);
        Assert.StartsWith("Latest must be after earliest.", equalBounds.Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => SplunkTimeRange.Absolute(instant, instant.AddMilliseconds(-1)));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AllTimeRangeEmitsDocumentedAllTimeBounds()
    {
        var range = SplunkTimeRange.AllTime();

        Assert.Equal("1", range.Earliest);
        Assert.Equal("now", range.Latest);

        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"1"}}
        """);

        using var client = CreateClient(handler);

        await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")
        {
            TimeRange = range
        }).ToListAsync();

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal("1", form["earliest_time"]);
        Assert.Equal("now", form["latest_time"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public Task RelativeTimeRangesPassModifiersThroughUnchanged()
    {
        var defaultLatest = SplunkTimeRange.Relative("-24h");
        Assert.Equal("-24h", defaultLatest.Earliest);
        Assert.Equal("now", defaultLatest.Latest);

        var snapped = SplunkTimeRange.Relative("-7d@d", "@d");
        Assert.Equal("-7d@d", snapped.Earliest);
        Assert.Equal("@d", snapped.Latest);

        var earliestWhitespace = Assert.Throws<ArgumentException>(() => SplunkTimeRange.Relative("-24 h"));
        Assert.Equal("earliest", earliestWhitespace.ParamName);
        Assert.StartsWith("Splunk time modifiers must not contain whitespace.", earliestWhitespace.Message, StringComparison.Ordinal);

        var latestWhitespace = Assert.Throws<ArgumentException>(() => SplunkTimeRange.Relative("-24h", "no w"));
        Assert.Equal("latest", latestWhitespace.ParamName);

        var emptyEarliest = Assert.Throws<ArgumentException>(() => SplunkTimeRange.Relative(""));
        Assert.Equal("earliest", emptyEarliest.ParamName);
        Assert.StartsWith("A Splunk time modifier is required.", emptyEarliest.Message, StringComparison.Ordinal);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ResultRequestKeepsPostProcessSearchWhenFieldsIsNull()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[]}""");

        using var client = CreateClient(handler);

        var rows = await client.Search.GetResultsAsync("1700000000.41", new SplunkResultRequest
        {
            Count = 5,
            Fields = null!,
            PostProcessSearch = "| stats count AS error_count"
        });

        Assert.Empty(rows);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(
            "https://splunk.example.com:8089/services/search/v2/jobs/1700000000.41/results",
            sent.Uri.ToString());

        var form = ParseForm(sent.Body);
        Assert.Equal("json", form["output_mode"]);
        Assert.Equal("5", form["count"]);
        Assert.Equal("0", form["offset"]);
        Assert.Equal("| stats count AS error_count", form["search"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ResultRequestEmitsRepeatedFieldProjectionParameters()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[]}""");

        using var client = CreateClient(handler);

        await client.Search.GetResultsAsync("1700000000.42", new SplunkResultRequest
        {
            Count = 2,
            Fields = ["service", "duration_ms"]
        });

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Empty(sent.Body);
        Assert.Equal(
            "https://splunk.example.com:8089/services/search/v2/jobs/1700000000.42/results?output_mode=json&count=2&offset=0&f=service&f=duration_ms",
            sent.Uri.ToString());
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ResultRequestFieldProjectionValidatesFieldNamesBeforeSending()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        var unsafeField = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Search.GetResultsAsync("1700000000.43", new SplunkResultRequest
            {
                Count = 1,
                Fields = ["service", "bad field"]
            }));
        Assert.Equal("Fields", unsafeField.ParamName);
        Assert.StartsWith("'bad field' is not a safe unquoted SPL field name.", unsafeField.Message, StringComparison.Ordinal);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ResultRequestFieldProjectionAcceptsDefaultSplunkFields()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[]}""");

        using var client = CreateClient(handler);

        // The f parameter is a REST projection filter and never enters SPL, so
        // default Splunk fields and SPL scoping tokens used as stats aliases
        // are legitimate projections.
        await client.Search.GetResultsAsync("1700000000.44", new SplunkResultRequest
        {
            Count = 1,
            Fields = ["index", "splunk_server", "earliest"]
        });

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal(
            "https://splunk.example.com:8089/services/search/v2/jobs/1700000000.44/results?output_mode=json&count=1&offset=0&f=index&f=splunk_server&f=earliest",
            sent.Uri.ToString());
        handler.AssertNoPendingResponses();
    }

    [Theory]
    [InlineData("search")]
    [InlineData("output_mode")]
    [InlineData("Exec_Mode")]
    public async Task SearchRequestRejectsSdkOwnedRestParameters(string parameterName)
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkConfigurationException>(async () =>
            await client.Search.OneshotSearchAsync(new SplunkSearchRequest("search index=\"team\"")
            {
                Parameters = new Dictionary<string, string> { [parameterName] = "value" }
            }));

        Assert.Equal(
            $"REST parameter '{parameterName}' is reserved because the SDK controls it for search requests.",
            exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SearchRequestRejectsTimeRangeParameterCollisionsOnlyWhenTimeRangeIsSet()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[]}""");

        using var client = CreateClient(handler);

        var earliestCollision = await Assert.ThrowsAsync<SplunkConfigurationException>(async () =>
            await client.Search.OneshotSearchAsync(new SplunkSearchRequest("search index=\"team\"")
            {
                TimeRange = SplunkTimeRange.Last(TimeSpan.FromHours(1)),
                Parameters = new Dictionary<string, string> { ["earliest_time"] = "-2h" }
            }));
        Assert.Equal(
            "REST parameter 'earliest_time' collides with the TimeRange property. Set time bounds through TimeRange only.",
            earliestCollision.Message);

        var latestCollision = await Assert.ThrowsAsync<SplunkConfigurationException>(async () =>
            await client.Search.OneshotSearchAsync(new SplunkSearchRequest("search index=\"team\"")
            {
                TimeRange = SplunkTimeRange.Last(TimeSpan.FromHours(1)),
                Parameters = new Dictionary<string, string> { ["latest_time"] = "now" }
            }));
        Assert.Equal(
            "REST parameter 'latest_time' collides with the TimeRange property. Set time bounds through TimeRange only.",
            latestCollision.Message);

        Assert.Empty(handler.Requests);

        // Without TimeRange the same key is caller-owned and passes through.
        await client.Search.OneshotSearchAsync(new SplunkSearchRequest("search index=\"team\"")
        {
            Parameters = new Dictionary<string, string> { ["earliest_time"] = "-2h" }
        });

        Assert.Equal("-2h", ParseForm(Assert.Single(handler.Requests).Body)["earliest_time"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task SearchRequestRejectsCountCollisionOnlyWhenCountIsSet()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[]}""");

        using var client = CreateClient(handler);

        var collision = await Assert.ThrowsAsync<SplunkConfigurationException>(async () =>
            await client.Search.OneshotSearchAsync(new SplunkSearchRequest("search index=\"team\"")
            {
                Count = 1,
                Parameters = new Dictionary<string, string> { ["count"] = "5" }
            }));
        Assert.Equal(
            "REST parameter 'count' collides with the Count property. Set the row limit through Count only.",
            collision.Message);
        Assert.Empty(handler.Requests);

        // Without Count the same key is caller-owned and passes through.
        await client.Search.OneshotSearchAsync(new SplunkSearchRequest("search index=\"team\"")
        {
            Parameters = new Dictionary<string, string> { ["count"] = "5" }
        });

        Assert.Equal("5", ParseForm(Assert.Single(handler.Requests).Body)["count"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task SearchRequestRejectsPreviewCollisionWhenTheSdkOwnsPreview()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        var explicitPreview = await Assert.ThrowsAsync<SplunkConfigurationException>(async () =>
            await client.Search.OneshotSearchAsync(new SplunkSearchRequest("search index=\"team\"")
            {
                Preview = true,
                Parameters = new Dictionary<string, string> { ["preview"] = "false" }
            }));
        Assert.Equal(
            "REST parameter 'preview' collides with the Preview property. Set preview behavior through Preview only.",
            explicitPreview.Message);

        // Export sends preview=false by default when Preview is null, so the SDK
        // owns the parameter there even without an explicit Preview value.
        var exportDefault = await Assert.ThrowsAsync<SplunkConfigurationException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\"")
            {
                Parameters = new Dictionary<string, string> { ["preview"] = "true" }
            }).ToListAsync());
        Assert.Equal(
            "REST parameter 'preview' collides with the Preview property. Set preview behavior through Preview only.",
            exportDefault.Message);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SearchRequestPassesNonCollidingParametersThrough()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"1"}}
        """);

        using var client = CreateClient(handler);

        var rows = await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")
        {
            TimeRange = SplunkTimeRange.Last(TimeSpan.FromHours(1)),
            Count = 1,
            Preview = true,
            Parameters = new Dictionary<string, string>
            {
                ["adhoc_search_level"] = "smart",
                ["max_time"] = "60"
            }
        }).ToListAsync();

        Assert.Single(rows);

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal("search index=\"team\" | stats count AS error_count", form["search"]);
        Assert.Equal("json", form["output_mode"]);
        Assert.Equal("-1h", form["earliest_time"]);
        Assert.Equal("now", form["latest_time"]);
        Assert.Equal("1", form["count"]);
        Assert.Equal("true", form["preview"]);
        Assert.Equal("smart", form["adhoc_search_level"]);
        Assert.Equal("60", form["max_time"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task AnalyticsQueryRawPredicatesPassThroughVerbatim()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"3"}}
        """);
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"average_value":"12.5"}}
        """);
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"_time":"1700000000","average_value":"10.5"}}
        """);

        using var client = CreateClient(handler);

        var count = await client.Analytics.CountErrorsAsync(new ErrorCountQuery("team")
        {
            RawPredicate = "sourcetype=access_* OR sourcetype=proxy"
        });
        var average = await client.Analytics.AverageAsync(new AverageMetricQuery("team", "duration_ms")
        {
            RawPredicate = "status=500 OR status=503"
        });
        var buckets = await client.Analytics.AverageTimeSeriesAsync(new MetricTimeSeriesQuery("team", "duration_ms")
        {
            RawPredicate = "host=web-* NOT host=web-99"
        });

        Assert.Equal(3, count);
        Assert.Equal(12.5, average);
        Assert.Equal(10.5, Assert.Single(buckets).Value);

        Assert.Equal(
            "search index=\"team\" \"error\" sourcetype=access_* OR sourcetype=proxy | stats count AS error_count",
            ParseForm(handler.Requests[0].Body)["search"]);
        Assert.Equal(
            "search index=\"team\" status=500 OR status=503 | stats avg(duration_ms) AS average_value",
            ParseForm(handler.Requests[1].Body)["search"]);
        Assert.Equal(
            "search index=\"team\" host=web-* NOT host=web-99 | timechart span=5m avg(duration_ms) AS average_value",
            ParseForm(handler.Requests[2].Body)["search"]);
        handler.AssertNoPendingResponses();
    }
}
