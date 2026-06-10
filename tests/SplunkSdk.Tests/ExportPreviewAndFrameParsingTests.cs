using System.Net;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class ExportPreviewAndFrameParsingTests
{
    [Fact]
    public async Task ExportSendsPreviewFalseWhenPreviewIsUnset()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"lastrow":true,"result":{"service":"checkout"}}
        """);

        using var client = CreateClient(handler);

        var rows = await client.Search.ExportAsync(
            new SplunkSearchRequest("search index=\"team\" | table service")).ToListAsync();

        Assert.False(Assert.Single(rows).Preview);

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal("false", form["preview"]);
        Assert.False(form.ContainsKey("count"));
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ExportPreviewOptInSendsPreviewTrueAndFlagsPreviewRows()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":true,"offset":0,"result":{"error_count":"3"}}
        {"preview":false,"offset":0,"lastrow":true,"result":{"error_count":"7"}}
        """);

        using var client = CreateClient(handler);

        var rows = await client.Search.ExportAsync(
            new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")
            {
                Preview = true
            }).ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].Preview);
        Assert.Equal(3, rows[0].GetInt64("error_count"));
        Assert.False(rows[1].Preview);
        Assert.True(rows[1].LastRow);
        Assert.Equal(7, rows[1].GetInt64("error_count"));

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal("true", form["preview"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ResultsArrayTopLevelPreviewFlagIsAppliedToEveryRow()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":true,"results":[{"service":"checkout"},{"service":"billing"}]}
        """);
        handler.Enqueue(HttpStatusCode.OK, """
        {"results":[{"service":"checkout"}]}
        """);

        using var client = CreateClient(handler);

        var previewRows = await client.Search.GetResultsAsync("1700000000.31", new SplunkResultRequest { Count = 2 });
        var finalRows = await client.Search.GetResultsAsync("1700000000.31", new SplunkResultRequest { Count = 1 });

        Assert.Equal(2, previewRows.Count);
        Assert.All(previewRows, row => Assert.True(row.Preview));
        Assert.Equal("checkout", previewRows[0].GetString("service"));
        Assert.Equal("billing", previewRows[1].GetString("service"));
        Assert.Null(previewRows[0].Offset);
        Assert.False(previewRows[0].LastRow);

        Assert.False(Assert.Single(finalRows).Preview);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ExportToleratesNonIntegerOffsetsInsteadOfThrowing()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":"not-a-number","result":{"service":"checkout"}}
        {"preview":false,"offset":null,"result":{"service":"billing"}}
        {"preview":false,"offset":2.5,"result":{"service":"payments"}}
        {"preview":false,"offset":3,"result":{"service":"shipping"}}
        """);

        using var client = CreateClient(handler);

        var rows = await client.Search.ExportAsync(
            new SplunkSearchRequest("search index=\"team\" | table service")).ToListAsync();

        Assert.Equal(4, rows.Count);
        Assert.Null(rows[0].Offset);
        Assert.Null(rows[1].Offset);
        Assert.Null(rows[2].Offset);
        Assert.Equal(3, rows[3].Offset);
        Assert.Equal("checkout", rows[0].GetString("service"));
        Assert.Equal("shipping", rows[3].GetString("service"));
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ExportSkipsMessageFramesWithNonStringTypeOrTextValues()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"messages":[{"type":42,"text":{"detail":"indexing latency"}},{"type":null,"text":null},"draining buffers"]}
        {"preview":false,"offset":0,"result":{"error_count":"5"}}
        """);

        using var client = CreateClient(handler);

        var rows = await client.Search.ExportAsync(
            new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync();

        Assert.Equal(5, Assert.Single(rows).GetInt64("error_count"));
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ExportFatalMessageFramesWithNonStringTextUseSanitizedApiErrors()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"messages":[{"type":"FATAL","text":12345}]}
        """);
        handler.Enqueue(HttpStatusCode.OK, """
        {"messages":[{"type":"ERROR","text":null}]}
        """);

        using var client = CreateClient(handler);

        var fatal = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.ExportAsync(
                new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync());

        Assert.Equal(HttpStatusCode.OK, fatal.StatusCode);
        var fatalMessage = Assert.Single(fatal.Messages);
        Assert.Equal("FATAL", fatalMessage.Type);
        Assert.Equal("12345", fatalMessage.Text);

        var error = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.ExportAsync(
                new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync());

        var errorMessage = Assert.Single(error.Messages);
        Assert.Equal("ERROR", errorMessage.Type);
        Assert.Equal(string.Empty, errorMessage.Text);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task StartWithNormalExecutionModeSendsExecModeNormal()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"sid":"1700000000.41"}""");

        using var client = CreateClient(handler);

        var job = await client.Search.StartAsync(
            new SplunkSearchRequest("search index=\"team\" | stats count AS error_count"),
            SplunkExecutionMode.Normal);

        Assert.Equal("1700000000.41", job.Sid);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://splunk.example.com:8089/services/search/v2/jobs", sent.Uri.ToString());

        var form = ParseForm(sent.Body);
        Assert.Equal("normal", form["exec_mode"]);
        Assert.Equal("json", form["output_mode"]);
        Assert.False(form.ContainsKey("preview"));
        handler.AssertNoPendingResponses();
    }
}
