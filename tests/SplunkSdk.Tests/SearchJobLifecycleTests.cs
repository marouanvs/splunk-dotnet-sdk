using System.Net;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class SearchJobLifecycleTests
{
    [Fact]
    public async Task StartAndGetResultsUseJobLifecycleEndpoints()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"sid":"1700000000.1"}""");
        handler.Enqueue(HttpStatusCode.OK, """
        {"results":[{"duration_ms":"10"},{"duration_ms":"20"}]}
        """);

        using var client = CreateClient(handler);

        var job = await client.Search.StartAsync(
            new SplunkSearchRequest("search index=\"team\" | stats avg(duration_ms) AS average_value"),
            SplunkExecutionMode.Blocking);
        var rows = await client.Search.GetResultsAsync(job.Sid, new SplunkResultRequest { Count = 2 });

        Assert.Equal("1700000000.1", job.Sid);
        Assert.Equal(2, rows.Count);
        Assert.Equal(20, rows[1].GetInt64("duration_ms"));

        Assert.Equal("https://splunk.example.com:8089/services/search/v2/jobs", handler.Requests[0].Uri.ToString());
        Assert.Equal("blocking", ParseForm(handler.Requests[0].Body)["exec_mode"]);
        Assert.Equal(
            "https://splunk.example.com:8089/services/search/v2/jobs/1700000000.1/results?output_mode=json&count=2&offset=0",
            handler.Requests[1].Uri.ToString());
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task GetResultsSurfacesFatalMessagesWhenResultsArePresent()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {
          "messages": [
            {
              "type": "ERROR",
              "text": "The search failed while producing partial results."
            }
          ],
          "results": [
            {
              "duration_ms": "10"
            }
          ]
        }
        """);

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.GetResultsAsync("1700000000.6", new SplunkResultRequest { Count = 1 }));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("ERROR", Assert.Single(exception.Messages).Type);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task GetResultsUsesBoundedDefaultAndRejectsAllRows()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[]}""");

        using var client = CreateClient(handler);

        var rows = await client.Search.GetResultsAsync("1700000000.3");

        Assert.Empty(rows);
        Assert.Equal(
            $"https://splunk.example.com:8089/services/search/v2/jobs/1700000000.3/results?output_mode=json&count={SplunkResultRequest.DefaultCount}&offset=0",
            Assert.Single(handler.Requests).Uri.ToString());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await client.Search.GetResultsAsync("1700000000.3", new SplunkResultRequest { Count = 0 }));
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task GetResultsCountZeroIsRejectedBeforeSendingRequest()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await client.Search.GetResultsAsync("1700000000.3", new SplunkResultRequest { Count = 0 }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetResultsPreservesEscapedSearchIdWhenAppendingQuery()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[]}""");

        using var client = CreateClient(handler);

        await client.Search.GetResultsAsync("job/with#reserved?chars", new SplunkResultRequest { Count = 1, Offset = 2 });

        Assert.Equal(
            "https://splunk.example.com:8089/services/search/v2/jobs/job%2Fwith%23reserved%3Fchars/results?output_mode=json&count=1&offset=2",
            Assert.Single(handler.Requests).Uri.AbsoluteUri);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task GetResultsPostsFormParametersWhenPostProcessSearchIsProvided()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"duration_ms":"10"}]}""");

        using var client = CreateClient(handler);

        var rows = await client.Search.GetResultsAsync("1700000000.7", new SplunkResultRequest
        {
            Count = 1,
            PostProcessSearch = "| stats avg(duration_ms) AS average_value"
        });

        Assert.Single(rows);
        var sent = Assert.Single(handler.Requests);

        // The v2 results endpoint accepts the post-process 'search' parameter
        // only on POST, so the parameters move to the form body and the URL
        // stays free of query parameters.
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(
            "https://splunk.example.com:8089/services/search/v2/jobs/1700000000.7/results",
            sent.Uri.ToString());

        var form = ParseForm(sent.Body);
        Assert.Equal("json", form["output_mode"]);
        Assert.Equal("1", form["count"]);
        Assert.Equal("0", form["offset"]);
        Assert.Equal("| stats avg(duration_ms) AS average_value", form["search"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task StartDropsResultOnlySearchParameters()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"sid":"1700000000.2"}""");

        using var client = CreateClient(handler);

        var request = new SplunkSearchRequest("search index=\"team\" | stats count AS result_count")
        {
            Count = 1,
            Preview = false,
            Parameters = new Dictionary<string, string>
            {
                ["count"] = "25",
                ["preview"] = "true",
                ["dispatch.buckets"] = "10"
            }
        };

        var job = await client.Search.StartAsync(request, SplunkExecutionMode.Blocking);

        Assert.Equal("1700000000.2", job.Sid);

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal("search index=\"team\" | stats count AS result_count", form["search"]);
        Assert.Equal("json", form["output_mode"]);
        Assert.Equal("blocking", form["exec_mode"]);
        Assert.Equal("10", form["dispatch.buckets"]);
        Assert.False(form.ContainsKey("count"));
        Assert.False(form.ContainsKey("preview"));
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task OneshotSearchPostsOneshotExecModeAndParsesBufferedResults()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"error_count":"7"}]}""");

        using var client = CreateClient(handler);

        var rows = await client.Search.OneshotSearchAsync(
            new SplunkSearchRequest("search index=\"team\" | stats count AS error_count"));

        Assert.Equal(7, Assert.Single(rows).GetInt64("error_count"));

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://splunk.example.com:8089/services/search/v2/jobs", sent.Uri.ToString());

        var form = ParseForm(sent.Body);
        Assert.Equal("search index=\"team\" | stats count AS error_count", form["search"]);
        Assert.Equal("json", form["output_mode"]);
        Assert.Equal("oneshot", form["exec_mode"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task StartRejectsOneshotExecutionModeBeforeSendingAnyRequest()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Search.StartAsync(
                new SplunkSearchRequest("search index=\"team\""),
                SplunkExecutionMode.Oneshot));

        Assert.Equal("executionMode", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task JobStatusAndDeleteUseJobDetailEndpoints()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"entry":[{"content":{"sid":"1700000000.21","isDone":true,"isFailed":false,"dispatchState":"DONE","doneProgress":1.0,"eventCount":42,"resultCount":5}}]}
        """);
        handler.Enqueue(HttpStatusCode.OK, """{"messages":[{"type":"INFO","text":"deleted"}]}""");

        using var client = CreateClient(handler);

        var status = await client.Search.GetJobStatusAsync("1700000000.21");
        await client.Search.DeleteJobAsync("1700000000.21");

        Assert.Equal("1700000000.21", status.Sid);
        Assert.True(status.IsDone);
        Assert.False(status.IsFailed);
        Assert.Equal("DONE", status.DispatchState);
        Assert.Equal(1.0, status.DoneProgress);
        Assert.Equal(42, status.EventCount);
        Assert.Equal(5, status.ResultCount);

        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(
            "https://splunk.example.com:8089/services/search/v2/jobs/1700000000.21?output_mode=json",
            handler.Requests[0].Uri.ToString());
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
        Assert.Equal(
            "https://splunk.example.com:8089/services/search/v2/jobs/1700000000.21",
            handler.Requests[1].Uri.ToString());
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task WaitForJobCompletionPollsUntilTheJobIsDone()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"entry":[{"content":{"sid":"1700000000.22","isDone":false,"dispatchState":"RUNNING"}}]}
        """);
        handler.Enqueue(HttpStatusCode.OK, """
        {"entry":[{"content":{"sid":"1700000000.22","isDone":true,"dispatchState":"DONE"}}]}
        """);

        using var client = CreateClient(handler);

        var status = await client.Search.WaitForJobCompletionAsync(
            "1700000000.22",
            pollInterval: TimeSpan.FromMilliseconds(1),
            timeout: TimeSpan.FromSeconds(30));

        Assert.True(status.IsDone);
        Assert.Equal(2, handler.Requests.Count);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task WaitForJobCompletionThrowsSanitizedErrorWhenTheJobFails()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"entry":[{"content":{"sid":"1700000000.23","isDone":false,"isFailed":true,"dispatchState":"FAILED"}}]}
        """);

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.WaitForJobCompletionAsync(
                "1700000000.23",
                pollInterval: TimeSpan.FromMilliseconds(1),
                timeout: TimeSpan.FromSeconds(30)));

        var message = Assert.Single(exception.Messages);
        Assert.Equal("ERROR", message.Type);
        Assert.Contains("failed state", message.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("1700000000.23", exception.Message, StringComparison.Ordinal);
        handler.AssertNoPendingResponses();
    }
}
