using System.Net;
using System.Text.Json;
using Marouanvs.Splunk.Analytics;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class SearchExportTests
{
    [Fact]
    public async Task ExportAddsBearerAuthAndFormParameters()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"lastrow":true,"result":{"error_count":"42"}}
        """);

        using var client = CreateClient(handler);

        var request = new SplunkSearchRequest("search index=\"team_a\" | stats count AS error_count")
        {
            TimeRange = SplunkTimeRange.Last(TimeSpan.FromHours(1)),
            Count = 1
        };

        var rows = await client.Search.ExportAsync(request).ToListAsync();

        Assert.Single(rows);
        Assert.Equal(42, rows[0].GetInt64("error_count"));
        Assert.True(rows[0].LastRow, "Expected lastrow metadata to be parsed.");

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://splunk.example.com:8089/services/search/v2/jobs/export", sent.Uri.ToString());
        Assert.Equal("Bearer", sent.Authorization?.Scheme);
        Assert.Equal("test-token", sent.Authorization?.Parameter);
        Assert.Equal($"Marouanvs.Splunk/{SdkInformationalVersion()}", sent.UserAgent);

        var form = ParseForm(sent.Body);
        Assert.Equal("search index=\"team_a\" | stats count AS error_count", form["search"]);
        Assert.Equal("json", form["output_mode"]);
        Assert.Equal("-1h", form["earliest_time"]);
        Assert.Equal("now", form["latest_time"]);
        Assert.Equal("1", form["count"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ExportSkipsNonResultMessageFrames()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"messages":[{"type":"INFO","text":"search job completed"}]}
        {"messages":[{"type":"WARN","text":"search produced a warning"}]}
        {"preview":false,"offset":0,"result":{"error_count":"9"}}
        {"lastrow":true}
        """);

        using var client = CreateClient(handler);

        var count = await client.Analytics.CountErrorsAsync(new ErrorCountQuery("team")
        {
            TimeRange = SplunkTimeRange.Last(TimeSpan.FromMinutes(5))
        });

        Assert.Equal(9, count);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ExportSurfacesErrorMessageFrames()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"messages":[{"type":"ERROR","text":"The search failed before producing results."}]}
        """);

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Analytics.CountErrorsAsync(new ErrorCountQuery("team")
            {
                TimeRange = SplunkTimeRange.Last(TimeSpan.FromMinutes(5))
            }));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Single(exception.Messages);
        Assert.Equal("ERROR", exception.Messages[0].Type);
        Assert.Equal("The search failed before producing results.", exception.Messages[0].Text);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ExportSurfacesFatalMessageFramesAfterRows()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"9"}}
        {"messages":[{"type":"FATAL","text":"The search process terminated."}]}
        """);

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync());

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Single(exception.Messages);
        Assert.Equal("FATAL", exception.Messages[0].Type);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ExportParsesPrettyPrintedTopLevelFrames()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {
          "preview": false,
          "offset": 0,
          "result": {
            "service": "checkout"
          }
        }
        {
          "preview": false,
          "offset": 1,
          "result": {
            "service": "billing"
          }
        }
        """);

        using var client = CreateClient(handler);

        var rows = await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | table service")).ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal("checkout", rows[0].GetString("service"));
        Assert.Equal("billing", rows[1].GetString("service"));
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ExportMalformedJsonRaisesSanitizedSplunkException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"service":"checkout"}
        """);

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | table service")).ToListAsync());

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("Splunk returned malformed JSON in the search result stream.", exception.Message);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.DoesNotContain("checkout", exception.Message, StringComparison.Ordinal);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task SearchAndAnalyticsNullCollectionsAreTreatedAsEmpty()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"2"}}
        """);
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"3"}}
        """);
        handler.Enqueue(HttpStatusCode.OK, """{"results":[]}""");

        using var client = CreateClient(handler);

        var count = await client.Analytics.CountErrorsAsync(new ErrorCountQuery("team")
        {
            FieldFilters = null!
        });
        var rows = await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")
        {
            Count = 1,
            Parameters = null!
        }).ToListAsync();
        var resultRows = await client.Search.GetResultsAsync("1700000000.8", new SplunkResultRequest
        {
            Count = 1,
            Fields = null!
        });

        Assert.Equal(2, count);
        Assert.Single(rows);
        Assert.Empty(resultRows);

        Assert.Equal(
            "search index=\"team\" \"error\" | stats count AS error_count",
            ParseForm(handler.Requests[0].Body)["search"]);
        Assert.Equal("1", ParseForm(handler.Requests[1].Body)["count"]);
        Assert.Equal(
            "https://splunk.example.com:8089/services/search/v2/jobs/1700000000.8/results?output_mode=json&count=1&offset=0",
            handler.Requests[2].Uri.ToString());
        handler.AssertNoPendingResponses();
    }
}
