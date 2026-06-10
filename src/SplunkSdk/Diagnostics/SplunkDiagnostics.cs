using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Marouanvs.Splunk.Diagnostics;

/// <summary>
/// Diagnostic primitives emitted by the SDK.
/// </summary>
public static class SplunkDiagnostics
{
    /// <summary>
    /// Activity source name for OpenTelemetry or <see cref="ActivityListener"/> integration.
    /// </summary>
    public const string ActivitySourceName = "Marouanvs.Splunk";

    /// <summary>
    /// Meter name for metrics emitted by the SDK.
    /// </summary>
    public const string MeterName = "Marouanvs.Splunk";

    /// <summary>
    /// Histogram name for REST request duration in milliseconds.
    /// </summary>
    public const string RestRequestDurationMetricName = "marouanvs.splunk.rest.client.request.duration";

    /// <summary>
    /// Counter name for REST retries.
    /// </summary>
    public const string RestRetryMetricName = "marouanvs.splunk.rest.client.retries";

    /// <summary>
    /// Counter name for unsuccessful Splunk REST responses.
    /// </summary>
    public const string RestErrorMetricName = "marouanvs.splunk.rest.client.errors";

    /// <summary>
    /// Histogram name for search operation duration in milliseconds.
    /// </summary>
    public const string SearchOperationDurationMetricName = "marouanvs.splunk.search.operation.duration";

    /// <summary>
    /// Histogram name for search result rows read per search operation.
    /// </summary>
    public const string SearchRowsMetricName = "marouanvs.splunk.search.rows";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> RestRequestDuration = Meter.CreateHistogram<double>(
        RestRequestDurationMetricName,
        unit: "ms",
        description: "Total elapsed time of Splunk REST requests across all retry attempts, including backoff delays.");

    private static readonly Counter<long> RestRetries = Meter.CreateCounter<long>(
        RestRetryMetricName,
        description: "Number of Splunk REST retry attempts.");

    private static readonly Counter<long> RestErrors = Meter.CreateCounter<long>(
        RestErrorMetricName,
        description: "Number of unsuccessful Splunk REST responses surfaced by the SDK.");

    private static readonly Histogram<double> SearchOperationDuration = Meter.CreateHistogram<double>(
        SearchOperationDurationMetricName,
        unit: "ms",
        description: "Duration of Splunk search operations.");

    private static readonly Histogram<long> SearchRows = Meter.CreateHistogram<long>(
        SearchRowsMetricName,
        unit: "{row}",
        description: "Number of Splunk search result rows read per search operation.");

    internal static void RecordRestRequestDuration(
        TimeSpan duration,
        string method,
        string endpoint,
        int? statusCode,
        int retryCount,
        string? errorType = null)
    {
        var tags = new TagList
        {
            { "http.request.method", method },
            { "splunk.endpoint", endpoint },
            { "splunk.retry_count", retryCount }
        };

        if (statusCode is not null)
        {
            tags.Add("http.response.status_code", statusCode.Value);
        }

        if (!string.IsNullOrWhiteSpace(errorType))
        {
            tags.Add("error.type", errorType);
        }

        RestRequestDuration.Record(duration.TotalMilliseconds, tags);
    }

    internal static void RecordRestRetry(string method, string endpoint, string reason)
    {
        RestRetries.Add(
            1,
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("splunk.endpoint", endpoint),
            new KeyValuePair<string, object?>("splunk.retry.reason", reason));
    }

    internal static void RecordRestError(int statusCode, int messageCount, string? messageType)
    {
        RestErrors.Add(
            1,
            new KeyValuePair<string, object?>("http.response.status_code", statusCode),
            new KeyValuePair<string, object?>("splunk.message_count", messageCount),
            new KeyValuePair<string, object?>("splunk.message_type", string.IsNullOrWhiteSpace(messageType) ? "unknown" : messageType));
    }

    internal static void RecordSearchOperationDuration(
        TimeSpan duration,
        string operation,
        bool completed,
        int rowCount = 0)
    {
        // rowCount is retained for source compatibility; row counts are recorded as
        // histogram values through RecordSearchRows so metric tags stay low-cardinality.
        _ = rowCount;

        SearchOperationDuration.Record(
            duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("splunk.operation", operation),
            new KeyValuePair<string, object?>("splunk.completed", completed));
    }

    internal static void RecordSearchRows(string operation, int rowCount)
    {
        if (rowCount < 0)
        {
            return;
        }

        SearchRows.Record(
            rowCount,
            new KeyValuePair<string, object?>("splunk.operation", operation));
    }

    internal static void SetException(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        activity?.SetTag("error.type", exception.GetType().FullName);
    }
}
