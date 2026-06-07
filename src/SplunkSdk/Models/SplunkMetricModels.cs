namespace SplunkSdk.Models;

/// <summary>
/// Query for counting error-like events in one Splunk index.
/// </summary>
/// <param name="Index">Single literal Splunk index name. Wildcards are rejected by generated SPL helpers.</param>
public sealed record ErrorCountQuery(string Index)
{
    /// <summary>
    /// Gets the literal text searched in events. Defaults to <c>error</c>.
    /// </summary>
    public string Text { get; init; } = "error";

    /// <summary>
    /// Gets exact field filters applied before aggregation.
    /// </summary>
    /// <remarks>Keys are Splunk field names and values are quoted as literals.</remarks>
    public IReadOnlyDictionary<string, string> FieldFilters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets an optional raw SPL predicate for advanced teams. Treat this as trusted input.
    /// </summary>
    public string? RawPredicate { get; init; }

    /// <summary>
    /// Gets the time range. Defaults to the last 24 hours.
    /// </summary>
    public SplunkTimeRange TimeRange { get; init; } = SplunkTimeRange.Last(TimeSpan.FromHours(24));
}

/// <summary>
/// Query for calculating the average of a numeric field.
/// </summary>
/// <param name="Index">Single literal Splunk index name. Wildcards are rejected by generated SPL helpers.</param>
/// <param name="Field">Safe unquoted numeric field name to aggregate.</param>
public sealed record AverageMetricQuery(string Index, string Field)
{
    /// <summary>
    /// Gets optional literal text searched in events before aggregation.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets exact field filters applied before aggregation.
    /// </summary>
    /// <remarks>Keys are Splunk field names and values are quoted as literals.</remarks>
    public IReadOnlyDictionary<string, string> FieldFilters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets an optional raw SPL predicate for advanced teams. Treat this as trusted input.
    /// </summary>
    public string? RawPredicate { get; init; }

    /// <summary>
    /// Gets the time range. Defaults to the last 24 hours.
    /// </summary>
    public SplunkTimeRange TimeRange { get; init; } = SplunkTimeRange.Last(TimeSpan.FromHours(24));
}

/// <summary>
/// Query for returning a timechart series for one metric field.
/// </summary>
/// <param name="Index">Single literal Splunk index name. Wildcards are rejected by generated SPL helpers.</param>
/// <param name="Field">Safe unquoted numeric field name to aggregate.</param>
public sealed record MetricTimeSeriesQuery(string Index, string Field)
{
    /// <summary>
    /// Gets the Splunk timechart span, for example <c>5m</c> or <c>1h</c>.
    /// </summary>
    public string Span { get; init; } = "5m";

    /// <summary>
    /// Gets optional literal text searched in events before aggregation.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets exact field filters applied before aggregation.
    /// </summary>
    /// <remarks>Keys are Splunk field names and values are quoted as literals.</remarks>
    public IReadOnlyDictionary<string, string> FieldFilters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the time range. Defaults to the last 24 hours.
    /// </summary>
    public SplunkTimeRange TimeRange { get; init; } = SplunkTimeRange.Last(TimeSpan.FromHours(24));
}

/// <summary>
/// A single time bucket produced by a Splunk <c>timechart</c> query.
/// </summary>
/// <param name="Time">Bucket timestamp parsed from Splunk's <c>_time</c> field when available.</param>
/// <param name="Value">Aggregate value for the bucket.</param>
public sealed record MetricTimeBucket(DateTimeOffset? Time, double? Value);
