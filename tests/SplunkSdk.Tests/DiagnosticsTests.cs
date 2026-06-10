using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.Diagnostics;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class DiagnosticsTests
{
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
        handler.AssertNoPendingResponses();
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

        // The SDK's own REST request span carries the error status and a stable
        // error.type; Splunk message details stay off activities entirely.
        var restActivity = Assert.Single(activities.Where(activity => activity.Name == "Splunk REST request").ToArray());
        Assert.Equal(ActivityStatusCode.Error, restActivity.Status);
        Assert.Equal(nameof(SplunkApiException), restActivity.Tags["error.type"]);
        Assert.False(restActivity.Tags.ContainsKey("splunk.message_count"));
        Assert.False(restActivity.Tags.ContainsKey("splunk.message_type"));

        var exportActivity = Assert.Single(activities.Where(activity => activity.Name == "Splunk search export").ToArray());
        Assert.Equal(ActivityStatusCode.Error, exportActivity.Status);
        Assert.Equal(typeof(SplunkApiException).FullName, exportActivity.Tags["error.type"]);
        Assert.False(exportActivity.Tags.ContainsKey("splunk.message_count"));
        Assert.False(exportActivity.Tags.ContainsKey("splunk.message_type"));

        Assert.Contains(measurements, measurement => measurement.Name == SplunkDiagnostics.RestErrorMetricName);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task RetriedStatusCodeDoesNotLeakOntoRequestSpanWhenLaterAttemptThrows()
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
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{"messages":[{"type":"WARN","text":"busy"}]}""");
        handler.EnqueueException(new HttpRequestException("Connection reset."));

        using var client = CreateClient(handler, maxRetries: 1);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.Search.GetJobStatusAsync("1700000000.11"));

        // Attempt 0 received a retryable 503; attempt 1 threw with the retry
        // budget exhausted. The exported span must not pair the stale 503 from
        // the earlier attempt with the later exception, matching the duration
        // metric's per-attempt status reset.
        var restActivity = Assert.Single(activities.Where(activity => activity.Name == "Splunk REST request").ToArray());
        Assert.Equal(ActivityStatusCode.Error, restActivity.Status);
        Assert.Equal(typeof(HttpRequestException).FullName, restActivity.Tags["error.type"]);
        Assert.False(
            restActivity.Tags.ContainsKey("http.response.status_code"),
            "The request span must not carry a status code from an earlier retried attempt.");
        Assert.Equal("1", restActivity.Tags["splunk.retry_count"]);

        var duration = Assert.Single(measurements.Where(measurement =>
            measurement.Name == SplunkDiagnostics.RestRequestDurationMetricName).ToArray());
        Assert.False(duration.Tags.ContainsKey("http.response.status_code"));
        Assert.Equal(nameof(HttpRequestException), duration.Tags["error.type"]);
        handler.AssertNoPendingResponses();
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
}
