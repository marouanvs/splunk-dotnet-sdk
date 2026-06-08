using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SplunkSdk;
using SplunkSdk.Analytics;
using SplunkSdk.Authentication;
using SplunkSdk.Configuration;
using SplunkSdk.DependencyInjection;
using SplunkSdk.Diagnostics;
using SplunkSdk.Mapping;
using SplunkSdk.Models;
using SplunkSdk.Search;
using Xunit;

namespace SplunkSdk.Tests;

public sealed class SplunkSdkTests
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
        Assert.Equal($"SplunkSdk/{SdkInformationalVersion()}", sent.UserAgent);

        var form = ParseForm(sent.Body);
        Assert.Equal("search index=\"team_a\" | stats count AS error_count", form["search"]);
        Assert.Equal("json", form["output_mode"]);
        Assert.Equal("-1h", form["earliest_time"]);
        Assert.Equal("now", form["latest_time"]);
        Assert.Equal("1", form["count"]);
    }

    [Fact]
    public async Task DefaultUserAgentUsesAssemblyInformationalVersion()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[]}""");

        using var client = CreateClient(handler);

        await client.Search.GetResultsAsync("1700000000.8", new SplunkResultRequest { Count = 1 });

        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"SplunkSdk/{SdkInformationalVersion()}", sent.UserAgent);
    }

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
    }

    [Fact]
    public async Task ExportMalformedJsonRaisesSanitizedSplunkException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"service":"checkout"}
        """);

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | table service")).ToListAsync());

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Empty(exception.ResponseSnippet);
        Assert.DoesNotContain("checkout", exception.Message, StringComparison.Ordinal);
    }

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
    }

    [Fact]
    public async Task GetResultsUsesPostWhenPostProcessSearchIsProvided()
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
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://splunk.example.com:8089/services/search/v2/jobs/1700000000.7/results", sent.Uri.ToString());

        var form = ParseForm(sent.Body);
        Assert.Equal("json", form["output_mode"]);
        Assert.Equal("1", form["count"]);
        Assert.Equal("0", form["offset"]);
        Assert.Equal("| stats avg(duration_ms) AS average_value", form["search"]);
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
    }

    [Fact]
    public async Task TransientResponsesAreRetried()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"busy"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """
        {"results":[{"error_count":"1"}]}
        """);

        using var client = CreateClient(handler, maxRetries: 1);

        var rows = await client.Search.GetResultsAsync("1700000000.4", new SplunkResultRequest { Count = 1 });

        Assert.Equal(1, rows[0].GetInt64("error_count"));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task RetryGateRetriesDeleteRequests()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"busy"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """{"messages":[{"type":"INFO","text":"deleted"}]}""");

        using var client = CreateClient(handler, maxRetries: 1);

        await client.SavedSearches.DeleteAsync("checkout_errors");

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Delete, request.Method));
    }

    [Fact]
    public async Task NonIdempotentPostsAreNotRetried()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"busy"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"1"}}
        """);

        using var client = CreateClient(handler, maxRetries: 1);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NonIdempotentPostTimeoutsAreNotRetried()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueException(new TaskCanceledException("Simulated client timeout."));
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"1"}}
        """);

        using var client = CreateClient(handler, maxRetries: 1);

        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync());

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TlsCertificateFailuresAreConfigurationErrorsAndAreNotRetried()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueException(new HttpRequestException(
            "The SSL connection could not be established.",
            new AuthenticationException("The remote certificate is invalid according to the validation procedure.")));
        handler.Enqueue(HttpStatusCode.OK, """{"results":[]}""");

        using var client = CreateClient(handler, maxRetries: 1);

        var exception = await Assert.ThrowsAsync<SplunkConfigurationException>(async () =>
            await client.Search.GetResultsAsync("1700000000.9", new SplunkResultRequest { Count = 1 }));

        Assert.Contains("TLS certificate validation failed", exception.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void RetryValidationRejectsZeroBackoffWhenRetriesAreEnabled()
    {
        var handler = new QueueHttpMessageHandler();

        var zeroBaseDelay = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            Retry = new SplunkRetryOptions
            {
                MaxRetries = 1,
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.FromMilliseconds(1)
            }
        };

        Assert.Throws<SplunkConfigurationException>(() => new SplunkClient(new HttpClient(handler), zeroBaseDelay));

        var zeroMaxDelay = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            Retry = new SplunkRetryOptions
            {
                MaxRetries = 1,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.Zero
            }
        };

        Assert.Throws<SplunkConfigurationException>(() => new SplunkClient(new HttpClient(handler), zeroMaxDelay));
    }

    [Fact]
    public async Task RetryBackoffClampsBeforeOverflow()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"busy"}]}""");
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"still busy"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"error_count":"1"}]}""");

        using var client = CreateClient(
            handler,
            new SplunkRetryOptions
            {
                MaxRetries = 2,
                BaseDelay = TimeSpan.MaxValue,
                MaxDelay = TimeSpan.FromMilliseconds(1)
            });

        var rows = await client.Search.GetResultsAsync("1700000000.5", new SplunkResultRequest { Count = 1 });

        Assert.Equal(1, rows[0].GetInt64("error_count"));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task SdkRetriesCanBeDisabledForHostOwnedResilience()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"busy"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"1"}}
        """);

        using var client = CreateClient(handler, maxRetries: 0);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Single(handler.Requests);
    }

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

        Assert.Empty(exception.ResponseSnippet);
        Assert.DoesNotContain("secret_payments", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("4111111111111111", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiagnosticsEmitSanitizedActivitiesAndMetrics()
    {
        var activities = new List<ActivitySnapshot>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SplunkDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(ActivitySnapshot.FromActivity(activity))
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new List<MeasurementSnapshot>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SplunkDiagnostics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            measurements.Add(MeasurementSnapshot.FromMeasurement(instrument.Name, measurement, tags)));
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            measurements.Add(MeasurementSnapshot.FromMeasurement(instrument.Name, measurement, tags)));
        meterListener.Start();

        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"5"}}
        """);

        using var client = CreateClient(handler);
        var rows = await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync();

        Assert.Single(rows);

        var restActivity = Assert.Single(activities.Where(activity => activity.Name == "Splunk REST request").ToArray());
        Assert.Equal("POST", restActivity.Tags["http.request.method"]);
        Assert.Equal("search.jobs.export", restActivity.Tags["splunk.endpoint"]);
        Assert.Equal("v2", restActivity.Tags["splunk.search_api_version"]);
        Assert.False(restActivity.Tags.ContainsKey("url.full"), "Diagnostics must not expose full URLs or private hostnames.");

        var exportActivity = Assert.Single(activities.Where(activity => activity.Name == "Splunk search export").ToArray());
        Assert.Equal("search.export", exportActivity.Tags["splunk.operation"]);
        Assert.Equal("1", exportActivity.Tags["splunk.result.row_count"]);

        Assert.Contains(measurements, measurement => measurement.Name == SplunkDiagnostics.RestRequestDurationMetricName);
        Assert.Contains(measurements, measurement => measurement.Name == SplunkDiagnostics.SearchOperationDurationMetricName);
        Assert.Contains(measurements, measurement => measurement.Name == SplunkDiagnostics.SearchRowsMetricName);
    }

    [Fact]
    public async Task DiagnosticsTagSplunkApiErrors()
    {
        var activities = new List<ActivitySnapshot>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SplunkDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(ActivitySnapshot.FromActivity(activity))
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new List<MeasurementSnapshot>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SplunkDiagnostics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            measurements.Add(MeasurementSnapshot.FromMeasurement(instrument.Name, measurement, tags)));
        meterListener.Start();

        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, """
        {"messages":[{"type":"ERROR","text":"requires capability: search"}]}
        """);

        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\"")).ToListAsync());

        var exportActivity = Assert.Single(activities.Where(activity => activity.Name == "Splunk search export").ToArray());
        Assert.Equal(ActivityStatusCode.Error.ToString(), exportActivity.Status.ToString());
        Assert.Equal(nameof(SplunkApiException), exportActivity.Tags["error.type"]);
        Assert.Equal("1", exportActivity.Tags["splunk.message_count"]);
        Assert.Equal("ERROR", exportActivity.Tags["splunk.message_type"]);

        Assert.Contains(measurements, measurement => measurement.Name == SplunkDiagnostics.RestErrorMetricName);
    }

    [Fact]
    public async Task DiagnosticsRecordRestFailuresBeforeRequestIsSent()
    {
        var activities = new List<ActivitySnapshot>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SplunkDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(ActivitySnapshot.FromActivity(activity))
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new List<MeasurementSnapshot>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == SplunkDiagnostics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            measurements.Add(MeasurementSnapshot.FromMeasurement(instrument.Name, measurement, tags)));
        meterListener.Start();

        var handler = new QueueHttpMessageHandler();
        using var client = new SplunkClient(
            new HttpClient(handler),
            new SplunkClientOptions
            {
                ManagementUri = new Uri("https://splunk.example.com:8089"),
                TokenProvider = new EmptySplunkTokenProvider(),
                Retry = new SplunkRetryOptions
                {
                    MaxRetries = 0,
                    BaseDelay = TimeSpan.Zero,
                    MaxDelay = TimeSpan.Zero
                }
            });

        var exception = await Assert.ThrowsAsync<SplunkConfigurationException>(async () =>
            await client.Search.GetResultsAsync("1700000000.10", new SplunkResultRequest { Count = 1 }));

        Assert.Contains("empty token", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);

        var restActivity = Assert.Single(activities.Where(activity => activity.Name == "Splunk REST request").ToArray());
        Assert.Equal(ActivityStatusCode.Error, restActivity.Status);
        Assert.Contains(nameof(SplunkConfigurationException), restActivity.Tags["error.type"], StringComparison.Ordinal);
        Assert.Equal("0", restActivity.Tags["splunk.retry_count"]);

        var duration = Assert.Single(measurements.Where(measurement =>
            measurement.Name == SplunkDiagnostics.RestRequestDurationMetricName).ToArray());
        Assert.Equal("GET", duration.Tags["http.request.method"]);
        Assert.Equal("search.jobs.results", duration.Tags["splunk.endpoint"]);
        Assert.Equal(nameof(SplunkConfigurationException), duration.Tags["error.type"]);
    }

    [Fact]
    public Task TypedMaterializationMapsSplunkRows()
    {
        var row = CreateRow("""
        {
          "service": "checkout",
          "error_count": "42",
          "average_value": "123.45",
          "is_enabled": "1",
          "observed_at": "2026-06-04T21:30:00Z"
        }
        """);

        var mapped = row.ToObject<MetricRow>();

        Assert.Equal("checkout", mapped.Service);
        Assert.Equal(42L, mapped.ErrorCount);
        Assert.Equal(123.45, mapped.Average);
        Assert.True(mapped.IsEnabled, "Expected numeric booleans to map to bool.");
        Assert.Equal(DateTimeOffset.Parse("2026-06-04T21:30:00Z", System.Globalization.CultureInfo.InvariantCulture), mapped.ObservedAt);

        Assert.Throws<SplunkMappingException>(() =>
            CreateRow("""{"error_count":"not-a-number"}""").ToObject<MetricRow>());

        return Task.CompletedTask;
    }

    [Fact]
    public Task TypedMaterializationRejectsNullForNonNullableValueTypes()
    {
        var exception = Assert.Throws<SplunkMappingException>(() =>
            CreateRow("""{"error_count":null}""").ToObject<MetricRow>());

        Assert.Contains("non-nullable", exception.Message, StringComparison.Ordinal);

        var mapped = CreateRow("""{"average_value":null}""").ToObject<MetricRow>();

        Assert.Null(mapped.Average);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TypedMaterializationMapsMultiValueFieldsToCollections()
    {
        var row = CreateRow("""
        {
          "service": "checkout",
          "users": ["alice", "bob"],
          "durations": ["10", "20"]
        }
        """);

        var mapped = row.ToObject<MultiValueMetricRow>();

        Assert.Equal(new[] { "alice", "bob" }, mapped.Users);
        Assert.Equal(new[] { 10L, 20L }, mapped.Durations);
        return Task.CompletedTask;
    }

    [Fact]
    public Task MappingExceptionsDoNotEchoRawFieldValues()
    {
        var exception = Assert.Throws<SplunkMappingException>(() =>
            CreateRow("""{"error_count":"not-a-number"}""").ToObject<MetricRow>());

        Assert.DoesNotContain("not-a-number", exception.Message, StringComparison.Ordinal);

        var multiValueException = Assert.Throws<SplunkMappingException>(() =>
            CreateRow("""{"service":["alice","bob"]}""").ToObject<MetricRow>());

        Assert.DoesNotContain("alice", multiValueException.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("bob", multiValueException.Message, StringComparison.Ordinal);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AsyncTypedMaterializationMapsExportRows()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"service":"checkout","error_count":"3","average_value":"10.5","is_enabled":"true"}}
        {"preview":false,"offset":1,"result":{"service":"billing","error_count":"4","average_value":"11.5","is_enabled":"false"}}
        """);

        using var client = CreateClient(handler);

        var rows = await client.Search
            .ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count"))
            .ToObjectsAsync<MetricRow>()
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal("checkout", rows[0].Service);
        Assert.Equal(4L, rows[1].ErrorCount);
        Assert.False(rows[1].IsEnabled);
    }

    [Fact]
    public async Task DependencyInjectionRegistersSplunkClients()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"12"}}
        """);

        var services = new ServiceCollection();
        services
            .AddSplunkClient(new SplunkClientOptions
            {
                ManagementUri = new Uri("https://splunk.example.com:8089"),
                TokenProvider = new StaticSplunkTokenProvider("di-token"),
                Retry = new SplunkRetryOptions { MaxRetries = 0 }
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<SplunkClient>();
        var searchClient = provider.GetRequiredService<ISplunkSearchClient>();

        var rows = await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync();

        Assert.Equal(12, rows[0].GetInt64("error_count"));
        Assert.True(searchClient is not null, "Expected ISplunkSearchClient to be registered.");
        Assert.Equal("Bearer", handler.Requests[0].Authorization?.Scheme);
        Assert.Equal("di-token", handler.Requests[0].Authorization?.Parameter);
    }

    [Fact]
    public async Task DependencyInjectionBindsSplunkClientFromConfigurationSection()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"17"}}
        """);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Splunk:ManagementUri"] = "https://splunk.example.com:8089",
                ["Splunk:Token"] = " config-token ",
                ["Splunk:AuthorizationScheme"] = "Splunk",
                ["Splunk:SearchApiVersion"] = "V1",
                ["Splunk:DefaultNamespace:Owner"] = "nobody",
                ["Splunk:DefaultNamespace:App"] = "search",
                ["Splunk:Retry:MaxRetries"] = "0",
                ["Splunk:UserAgent"] = "ConfigHost/2.0"
            })
            .Build();

        var services = new ServiceCollection();
        services
            .AddSplunkClient(configuration.GetSection("Splunk"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();

        var searchClient = provider.GetRequiredService<ISplunkSearchClient>();
        var rows = await searchClient
            .ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count"))
            .ToListAsync();

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(17, rows[0].GetInt64("error_count"));
        Assert.Equal(
            "https://splunk.example.com:8089/servicesNS/nobody/search/search/jobs/export",
            sent.Uri.ToString());
        Assert.Equal("Splunk", sent.Authorization?.Scheme);
        Assert.Equal("config-token", sent.Authorization?.Parameter);
        Assert.Equal("ConfigHost/2.0", sent.UserAgent);
    }

    [Fact]
    public async Task DependencyInjectionBindsSplunkClientFromDefaultConfigurationSection()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"8"}}
        """);

        var environmentVariableName = $"SPLUNKSDK_TEST_TOKEN_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(environmentVariableName, "env-token");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Splunk:ManagementUri"] = "https://splunk.example.com:8089",
                    ["Splunk:TokenEnvironmentVariable"] = environmentVariableName,
                    ["Splunk:Retry:MaxRetries"] = "0"
                })
                .Build();

            var services = new ServiceCollection();
            services
                .AddSplunkClient(configuration)
                .ConfigurePrimaryHttpMessageHandler(() => handler);

            using var provider = services.BuildServiceProvider();

            var analytics = provider.GetRequiredService<ISplunkAnalyticsClient>();
            var errors = await analytics.CountErrorsAsync(new ErrorCountQuery("team"));

            Assert.Equal(8, errors);
            Assert.Equal("Bearer", handler.Requests[0].Authorization?.Scheme);
            Assert.Equal("env-token", handler.Requests[0].Authorization?.Parameter);
            Assert.Equal(
                "https://splunk.example.com:8089/services/search/v2/jobs/export",
                handler.Requests[0].Uri.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariableName, null);
        }
    }

    [Fact]
    public void DependencyInjectionRejectsAmbiguousConfiguredTokens()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Splunk:ManagementUri"] = "https://splunk.example.com:8089",
                ["Splunk:Token"] = "inline-token",
                ["Splunk:TokenEnvironmentVariable"] = "SPLUNK_TOKEN"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSplunkClient(configuration);

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<SplunkClient>());
        Assert.Contains("Configure either Splunk:Token or Splunk:TokenEnvironmentVariable", exception.Message);
    }

    [Fact]
    public async Task SavedSearchesCallSplunkEndpoints()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" WARN"));
        handler.Enqueue(HttpStatusCode.OK, """{"sid":"1710000000.10"}""");
        handler.Enqueue(HttpStatusCode.OK, """{"messages":[{"type":"INFO","text":"deleted"}]}""");

        using var client = CreateClient(handler);

        var savedSearches = await client.SavedSearches.ListAsync(new SplunkSavedSearchListRequest { Count = 10 });
        var created = await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest("checkout_errors", "search index=\"team\" ERROR")
        {
            Description = "Checkout error count",
            IsScheduled = true,
            CronSchedule = "*/5 * * * *",
            TimeRange = SplunkTimeRange.Relative("-15m", "now"),
            Dispatch = new SplunkSavedSearchDispatchSettings
            {
                Buckets = 10,
                MaxCount = 5000,
                Lookups = true,
                TimeFormat = "%s"
            }
        });
        var updated = await client.SavedSearches.UpdateAsync("checkout_errors", new UpdateSavedSearchRequest
        {
            Search = "search index=\"team\" WARN",
            Disabled = true,
            Dispatch = new SplunkSavedSearchDispatchSettings
            {
                Lookups = false
            }
        });
        var job = await client.SavedSearches.DispatchAsync("checkout_errors", new SplunkDispatchSavedSearchRequest
        {
            Parameters = new Dictionary<string, string> { ["dispatch.now"] = "1" }
        });
        await client.SavedSearches.DeleteAsync("checkout_errors");

        Assert.Single(savedSearches);
        Assert.Equal("checkout_errors", created.Name);
        Assert.Equal(SplunkAlertType.NumberOfEvents, created.Alert?.AlertType);
        Assert.Equal(SplunkAlertSeverity.Error, created.Alert?.Severity);
        Assert.Equal("search index=\"team\" WARN", updated.Search);
        Assert.Equal("1710000000.10", job.Sid);

        Assert.Equal("https://splunk.example.com:8089/services/saved/searches?output_mode=json&count=10", handler.Requests[0].Uri.ToString());

        var createForm = ParseForm(handler.Requests[1].Body);
        Assert.Equal("checkout_errors", createForm["name"]);
        Assert.Equal("search index=\"team\" ERROR", createForm["search"]);
        Assert.Equal("1", createForm["is_scheduled"]);
        Assert.Equal("*/5 * * * *", createForm["cron_schedule"]);
        Assert.Equal("-15m", createForm["dispatch.earliest_time"]);
        Assert.Equal("now", createForm["dispatch.latest_time"]);
        Assert.Equal("10", createForm["dispatch.buckets"]);
        Assert.Equal("5000", createForm["dispatch.max_count"]);
        Assert.Equal("1", createForm["dispatch.lookups"]);
        Assert.Equal("%s", createForm["dispatch.time_format"]);

        var updateForm = ParseForm(handler.Requests[2].Body);
        Assert.Equal("search index=\"team\" WARN", updateForm["search"]);
        Assert.Equal("1", updateForm["disabled"]);
        Assert.Equal("0", updateForm["dispatch.lookups"]);

        Assert.Equal("https://splunk.example.com:8089/services/saved/searches/checkout_errors/dispatch", handler.Requests[3].Uri.ToString());
        Assert.Equal("1", ParseForm(handler.Requests[3].Body)["dispatch.now"]);
        Assert.Equal(HttpMethod.Delete, handler.Requests[4].Method);
    }

    [Fact]
    public async Task SavedSearchesParseTypedDispatchAndAlertActionFields()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, AdvancedSavedSearchFeed("checkout_alert", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        var savedSearch = Assert.Single(await client.SavedSearches.ListAsync());

        Assert.Equal(25, savedSearch.Dispatch?.Buckets);
        Assert.Equal(1000, savedSearch.Dispatch?.MaxCount);
        Assert.True(savedSearch.Dispatch?.Lookups);
        Assert.Equal("%s", savedSearch.Dispatch?.TimeFormat);
        Assert.Equal("2h", savedSearch.Alert?.Expires);
        Assert.True(savedSearch.Alert?.Suppression?.Enabled);
        Assert.Equal("30m", savedSearch.Alert?.Suppression?.Period);
        Assert.Equal(["service", "host"], savedSearch.Alert?.Suppression?.Fields);
        Assert.Equal(["checkout-oncall@example.com", "checkout-backup@example.com"], savedSearch.Alert?.Email?.To);
        Assert.Equal("Checkout alert", savedSearch.Alert?.Email?.Subject);
        Assert.Equal("Checkout errors detected", savedSearch.Alert?.Email?.Message);
        Assert.Equal("summary_errors", savedSearch.Alert?.SummaryIndex?.Name);
    }

    [Fact]
    public async Task SavedSearchGetReturnsNullForNotFound()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NotFound, """{"messages":[{"type":"ERROR","text":"Not found"}]}""");

        using var client = CreateClient(handler);

        var savedSearch = await client.SavedSearches.GetAsync("missing_search");

        Assert.Null(savedSearch);
        Assert.Equal(
            "https://splunk.example.com:8089/services/saved/searches/missing_search?output_mode=json",
            Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task ScheduledSavedSearchCreateRequiresCronSchedule()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest("checkout_errors", "search index=\"team\" ERROR")
            {
                IsScheduled = true
            }));

        Assert.Equal("CronSchedule", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SavedSearchUpdateSendsEmptyDescription()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        await client.SavedSearches.UpdateAsync("checkout_errors", new UpdateSavedSearchRequest
        {
            Description = string.Empty
        });

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal("json", form["output_mode"]);
        Assert.True(form.ContainsKey("description"), "Expected empty description to be sent explicitly.");
        Assert.Equal(string.Empty, form["description"]);
    }

    [Fact]
    public async Task SavedSearchesRejectAdditionalParametersThatOverrideTypedProperties()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest("checkout_errors", "search index=\"team\" ERROR")
            {
                CronSchedule = "0 6 * * 1",
                AdditionalParameters = new Dictionary<string, string>
                {
                    ["cron_schedule"] = "*/5 * * * *"
                }
            }));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest("checkout_errors", "search index=\"team\" ERROR")
            {
                Dispatch = new SplunkSavedSearchDispatchSettings { Buckets = 10 },
                AdditionalParameters = new Dictionary<string, string>
                {
                    ["dispatch.buckets"] = "25"
                }
            }));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SavedSearches.UpdateAsync("checkout_errors", new UpdateSavedSearchRequest
            {
                Disabled = true,
                AdditionalParameters = new Dictionary<string, string>
                {
                    ["disabled"] = "0"
                }
            }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SavedSearchAndAlertNullParameterCollectionsAreTreatedAsEmpty()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" WARN"));
        handler.Enqueue(HttpStatusCode.OK, """{"sid":"1710000000.11"}""");
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_alert", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        var created = await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest("checkout_errors", "search index=\"team\" ERROR")
        {
            AdditionalParameters = null!
        });
        var updated = await client.SavedSearches.UpdateAsync("checkout_errors", new UpdateSavedSearchRequest
        {
            Search = "search index=\"team\" WARN",
            AdditionalParameters = null!
        });
        var job = await client.SavedSearches.DispatchAsync("checkout_errors", new SplunkDispatchSavedSearchRequest
        {
            Parameters = null!
        });
        var alert = await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
            "checkout_alert",
            "search index=\"team\" ERROR",
            "*/5 * * * *")
        {
            AdditionalParameters = null!
        });

        Assert.Equal("checkout_errors", created.Name);
        Assert.Equal("search index=\"team\" WARN", updated.Search);
        Assert.Equal("1710000000.11", job.Sid);
        Assert.Equal("checkout_alert", alert.Name);

        Assert.Equal("search index=\"team\" ERROR", ParseForm(handler.Requests[0].Body)["search"]);
        Assert.Equal("search index=\"team\" WARN", ParseForm(handler.Requests[1].Body)["search"]);
        Assert.Empty(ParseForm(handler.Requests[2].Body));

        var alertForm = ParseForm(handler.Requests[3].Body);
        Assert.Equal("1", alertForm["is_scheduled"]);
        Assert.Equal("number of events", alertForm["alert_type"]);
    }

    [Fact]
    public async Task SavedSearchesMalformedJsonRaisesSanitizedSplunkException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"entry":[""");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.SavedSearches.ListAsync());

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Empty(exception.ResponseSnippet);
        Assert.DoesNotContain("entry", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlertsCallSavedSearchAlertEndpoints()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_alert", "search index=\"team\" ERROR"));
        handler.Enqueue(HttpStatusCode.OK, """{"messages":[{"type":"INFO","text":"acknowledged"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """{"messages":[{"type":"INFO","text":"suppressed"}]}""");

        using var client = CreateClient(handler);

        var alert = await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
            "checkout_alert",
            "search index=\"team\" ERROR",
            "*/5 * * * *")
        {
            Alert = new SplunkAlertSettings
            {
                AlertType = SplunkAlertType.NumberOfEvents,
                Comparator = SplunkAlertComparator.GreaterThan,
                Threshold = "0",
                Severity = SplunkAlertSeverity.Error,
                Expires = "2h",
                Track = true,
                DigestMode = true,
                Actions = ["email"],
                Suppression = new SplunkAlertSuppressionSettings
                {
                    Enabled = true,
                    Period = "30m",
                    Fields = ["service", "host"]
                },
                Email = new SplunkEmailAlertActionSettings
                {
                    To = ["checkout-oncall@example.com", "checkout-backup@example.com"],
                    Cc = ["checkout-managers@example.com"],
                    Subject = "Checkout alert",
                    Message = "Checkout errors detected",
                    AuthUsername = "smtp-alerts",
                    PdfView = "search"
                },
                SummaryIndex = new SplunkSummaryIndexAlertActionSettings
                {
                    Name = "summary_errors"
                }
            }
        });
        await client.Alerts.AcknowledgeAsync("checkout_alert");
        await client.Alerts.SuppressAsync("checkout_alert", "30m");

        Assert.Equal("checkout_alert", alert.Name);

        var createForm = ParseForm(handler.Requests[0].Body);
        Assert.Equal("1", createForm["is_scheduled"]);
        Assert.Equal("number of events", createForm["alert_type"]);
        Assert.Equal("greater than", createForm["alert_comparator"]);
        Assert.Equal("0", createForm["alert_threshold"]);
        Assert.Equal("4", createForm["alert.severity"]);
        Assert.Equal("2h", createForm["alert.expires"]);
        Assert.Equal("1", createForm["alert.track"]);
        Assert.Equal("1", createForm["alert.digest_mode"]);
        Assert.Equal("1", createForm["alert.suppress"]);
        Assert.Equal("30m", createForm["alert.suppress.period"]);
        Assert.Equal("service,host", createForm["alert.suppress.fields"]);
        Assert.Equal("email,summary_index", createForm["actions"]);
        Assert.Equal("1", createForm["action.email"]);
        Assert.Equal("checkout-oncall@example.com,checkout-backup@example.com", createForm["action.email.to"]);
        Assert.Equal("checkout-managers@example.com", createForm["action.email.cc"]);
        Assert.Equal("Checkout alert", createForm["action.email.subject"]);
        Assert.Equal("Checkout errors detected", createForm["action.email.message.alert"]);
        Assert.Equal("smtp-alerts", createForm["action.email.auth_username"]);
        Assert.Equal("search", createForm["action.email.pdfview"]);
        Assert.Equal("1", createForm["action.summary_index"]);
        Assert.Equal("summary_errors", createForm["action.summary_index._name"]);

        Assert.Equal("https://splunk.example.com:8089/services/saved/searches/checkout_alert/acknowledge", handler.Requests[1].Uri.ToString());
        Assert.Equal("https://splunk.example.com:8089/services/saved/searches/checkout_alert/suppress", handler.Requests[2].Uri.ToString());
        Assert.Equal("30m", ParseForm(handler.Requests[2].Body)["period"]);
    }

    [Fact]
    public async Task AlertsValidateDocumentedCrossFieldRules()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings
                {
                    Actions = ["email"]
                }
            }));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings
                {
                    AlertType = SplunkAlertType.NumberOfEvents,
                    Condition = "search count > 0"
                }
            }));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings
                {
                    Suppression = new SplunkAlertSuppressionSettings
                    {
                        Enabled = true
                    }
                }
            }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AlertCreateRequiresCronSchedule()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                " ")));

        Assert.Equal("CronSchedule", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AlertsRejectUnsafeActionNames()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings
                {
                    AlertType = SplunkAlertType.Always,
                    Actions = ["webhook,summary_index"]
                }
            }));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings
                {
                    AlertType = SplunkAlertType.Always,
                    Actions = ["custom/action"]
                }
            }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AlertsRejectAdditionalParametersThatOverrideTypedAlertSettings()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings
                {
                    Actions = ["email"],
                    Email = new SplunkEmailAlertActionSettings
                    {
                        To = ["checkout-oncall@example.com"]
                    }
                },
                AdditionalParameters = new Dictionary<string, string>
                {
                    ["actions"] = "webhook"
                }
            }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void AlertSeverityUsesSavedSearchScale()
    {
        Assert.Equal(1, (int)SplunkAlertSeverity.Debug);
        Assert.Equal(2, (int)SplunkAlertSeverity.Info);
        Assert.Equal(3, (int)SplunkAlertSeverity.Warn);
        Assert.Equal(4, (int)SplunkAlertSeverity.Error);
        Assert.Equal(5, (int)SplunkAlertSeverity.Severe);
        Assert.Equal(6, (int)SplunkAlertSeverity.Fatal);
    }

    [Fact]
    public Task QueryBuilderRejectsUnsafeFieldNames()
    {
        Assert.Throws<ArgumentException>(() =>
            SplunkQueryBuilder.FromIndex("team").FieldEquals("host | delete", "api-01"));

        var search = SplunkQueryBuilder.FromIndex("team")
            .SearchText("quoted \"value\"")
            .FieldEquals("service", "billing")
            .StatsAverage("duration_ms", "avg_duration")
            .Build();

        Assert.Equal(
            "search index=\"team\" \"quoted \\\"value\\\"\" service=\"billing\" | stats avg(duration_ms) AS avg_duration",
            search);

        return Task.CompletedTask;
    }

    [Fact]
    public Task QueryBuilderRejectsWildcardIndexScopes()
    {
        Assert.Throws<ArgumentException>(() => SplunkQueryBuilder.FromIndex("*"));
        Assert.Throws<ArgumentException>(() => SplunkQueryBuilder.FromIndex("team_*"));
        Assert.Throws<ArgumentException>(() => SplunkQueryBuilder.FromIndex("team?"));

        var search = SplunkQueryBuilder.FromIndex("team-prod_01")
            .StatsCount("event_count")
            .Build();

        Assert.Equal("search index=\"team-prod_01\" | stats count AS event_count", search);
        return Task.CompletedTask;
    }

    [Fact]
    public Task QueryBuilderRejectsUnsafeAggregateIdentifiers()
    {
        Assert.Throws<ArgumentException>(() =>
            SplunkQueryBuilder.FromIndex("team").StatsAverage("duration-ms", "avg_duration"));
        Assert.Throws<ArgumentException>(() =>
            SplunkQueryBuilder.FromIndex("team").StatsAverage("duration_ms", "avg-duration"));
        Assert.Throws<ArgumentException>(() =>
            SplunkQueryBuilder.FromIndex("team").TimechartAverage("5m", "duration-ms", "avg_duration"));

        return Task.CompletedTask;
    }

    private static SplunkClient CreateClient(QueueHttpMessageHandler handler, int maxRetries = 0)
    {
        var retry = maxRetries > 0
            ? new SplunkRetryOptions
            {
                MaxRetries = maxRetries,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(1)
            }
            : new SplunkRetryOptions
            {
                MaxRetries = 0,
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero
            };

        return CreateClient(handler, retry);
    }

    private static SplunkClient CreateClient(QueueHttpMessageHandler handler, SplunkRetryOptions retry)
    {
        var httpClient = new HttpClient(handler);
        var options = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            Retry = retry
        };

        return new SplunkClient(httpClient, options);
    }

    private static Dictionary<string, string> ParseForm(string body)
    {
        return body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]).Replace('+', ' '),
                pair => Uri.UnescapeDataString(pair.ElementAtOrDefault(1) ?? string.Empty).Replace('+', ' '),
                StringComparer.Ordinal);
    }

    private static SplunkSearchResult CreateRow(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var fields = document.RootElement.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.Clone(),
                StringComparer.Ordinal);

        return new SplunkSearchResult(fields);
    }

    private static string SdkInformationalVersion()
    {
        var version = typeof(SplunkClientOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            version = typeof(SplunkClientOptions).Assembly.GetName().Version?.ToString();
        }

        return string.IsNullOrWhiteSpace(version)
            ? "0.0.0"
            : version.Split('+', 2)[0];
    }

    private static string SavedSearchFeed(string name, string search) =>
    $$"""
    {
      "entry": [
        {
          "name": "{{name}}",
          "content": {
            "search": "{{JsonEscape(search)}}",
            "description": "Checkout search",
            "is_scheduled": "1",
            "cron_schedule": "*/5 * * * *",
            "disabled": "0",
            "alert_type": "number of events",
            "alert_comparator": "greater than",
            "alert_threshold": "0",
            "alert.severity": "4",
            "alert.track": "1",
            "alert.digest_mode": "1",
            "actions": "email"
          }
        }
      ]
    }
    """;

    private static string AdvancedSavedSearchFeed(string name, string search) =>
    $$"""
    {
      "entry": [
        {
          "name": "{{name}}",
          "content": {
            "search": "{{JsonEscape(search)}}",
            "description": "Checkout search",
            "is_scheduled": "1",
            "cron_schedule": "*/5 * * * *",
            "disabled": "0",
            "dispatch.buckets": "25",
            "dispatch.max_count": "1000",
            "dispatch.lookups": "1",
            "dispatch.time_format": "%s",
            "alert_type": "number of events",
            "alert_comparator": "greater than",
            "alert_threshold": "0",
            "alert.severity": "4",
            "alert.expires": "2h",
            "alert.track": "1",
            "alert.digest_mode": "1",
            "alert.suppress": "1",
            "alert.suppress.period": "30m",
            "alert.suppress.fields": "service,host",
            "actions": "email,summary_index",
            "action.email.to": "checkout-oncall@example.com,checkout-backup@example.com",
            "action.email.subject": "Checkout alert",
            "action.email.message.alert": "Checkout errors detected",
            "action.summary_index._name": "summary_errors"
          }
        }
      ]
    }
    """;

    private static string JsonEscape(string value) =>
    value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
}

internal sealed class QueueHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<RequestSnapshot> Requests { get; } = [];

    public void Enqueue(HttpStatusCode statusCode, string body, string mediaType = "application/json")
    {
        _responses.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType),
            ReasonPhrase = statusCode.ToString()
        }));
    }

    public void EnqueueException(Exception exception)
    {
        _responses.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No fake response was queued.");
        }

        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RequestSnapshot(
            request.Method,
            request.RequestUri ?? throw new InvalidOperationException("Request URI missing."),
            request.Headers.Authorization,
            request.Headers.UserAgent.ToString(),
            body));

        return await _responses.Dequeue()(request, cancellationToken);
    }
}

internal sealed record RequestSnapshot(
    HttpMethod Method,
    Uri Uri,
    AuthenticationHeaderValue? Authorization,
    string UserAgent,
    string Body);

internal sealed class EmptySplunkTokenProvider : ISplunkTokenProvider
{
    public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(string.Empty);
    }
}

internal sealed class MetricRow
{
    [SplunkField("service")]
    public string? Service { get; set; }

    [SplunkField("error_count")]
    public long ErrorCount { get; set; }

    [SplunkField("average_value")]
    public double? Average { get; set; }

    [SplunkField("is_enabled")]
    public bool IsEnabled { get; set; }

    [SplunkField("observed_at")]
    public DateTimeOffset? ObservedAt { get; set; }
}

internal sealed class MultiValueMetricRow
{
    [SplunkField("users")]
    public IReadOnlyList<string>? Users { get; set; }

    [SplunkField("durations")]
    public long[]? Durations { get; set; }
}

internal sealed record ActivitySnapshot(
    string Name,
    ActivityStatusCode Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static ActivitySnapshot FromActivity(Activity activity) =>
        new(
            activity.DisplayName,
            activity.Status,
            activity.TagObjects.ToDictionary(
                tag => tag.Key,
                tag => tag.Value?.ToString() ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record MeasurementSnapshot(
    string Name,
    double Value,
    IReadOnlyDictionary<string, string> Tags)
{
    public static MeasurementSnapshot FromMeasurement<T>(
        string name,
        T value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct
    {
        var copiedTags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            copiedTags[tag.Key] = tag.Value?.ToString() ?? string.Empty;
        }

        return new MeasurementSnapshot(name, Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), copiedTags);
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> values)
    {
        var results = new List<T>();
        await foreach (var value in values)
        {
            results.Add(value);
        }

        return results;
    }
}
