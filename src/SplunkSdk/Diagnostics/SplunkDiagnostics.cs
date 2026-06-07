using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SplunkSdk.Diagnostics;

/// <summary>
/// Diagnostic primitives emitted by the SDK.
/// </summary>
public static class SplunkDiagnostics
{
    /// <summary>
    /// Activity source name for OpenTelemetry or <see cref="ActivityListener"/> integration.
    /// </summary>
    public const string ActivitySourceName = "SplunkSdk";

    /// <summary>
    /// Meter name for metrics emitted by the SDK.
    /// </summary>
    public const string MeterName = "SplunkSdk";

    /// <summary>
    /// Histogram name for REST request duration in milliseconds.
    /// </summary>
    public const string RestRequestDurationMetricName = "splunksdk.rest.client.request.duration";

    /// <summary>
    /// Counter name for REST retries.
    /// </summary>
    public const string RestRetryMetricName = "splunksdk.rest.client.retries";

    /// <summary>
    /// Counter name for unsuccessful Splunk REST responses.
    /// </summary>
    public const string RestErrorMetricName = "splunksdk.rest.client.errors";

    /// <summary>
    /// Histogram name for search operation duration in milliseconds.
    /// </summary>
    public const string SearchOperationDurationMetricName = "splunksdk.search.operation.duration";

    /// <summary>
    /// Counter name for search result rows read by the SDK.
    /// </summary>
    public const string SearchRowsMetricName = "splunksdk.search.rows";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> RestRequestDuration = Meter.CreateHistogram<double>(
        RestRequestDurationMetricName,
        unit: "ms",
        description: "Duration of Splunk REST requests until response headers are received.");

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

    private static readonly Counter<long> SearchRows = Meter.CreateCounter<long>(
        SearchRowsMetricName,
        description: "Number of Splunk search result rows read by the SDK.");

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
        SearchOperationDuration.Record(
            duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("splunk.operation", operation),
            new KeyValuePair<string, object?>("splunk.completed", completed),
            new KeyValuePair<string, object?>("splunk.result.row_count", rowCount));
    }

    internal static void RecordSearchRows(string operation, int rowCount)
    {
        if (rowCount <= 0)
        {
            return;
        }

        SearchRows.Add(
            rowCount,
            new KeyValuePair<string, object?>("splunk.operation", operation));
    }

    internal static void SetException(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        activity?.SetTag("error.type", exception.GetType().FullName);
    }
}
