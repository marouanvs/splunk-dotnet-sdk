using SplunkSdk.Models;

namespace SplunkSdk.Analytics;

/// <summary>
/// High-level analytics helpers for common operational metrics.
/// </summary>
/// <remarks>
/// These helpers generate scoped SPL with <see cref="SplunkSdk.Search.SplunkQueryBuilder"/>
/// and are the preferred entry point when application/user input is assembled
/// into a query.
/// </remarks>
public interface ISplunkAnalyticsClient
{
    /// <summary>
    /// Counts error-like events in a team index.
    /// </summary>
    /// <param name="query">Index, filters, text predicate, and time range.</param>
    /// <param name="cancellationToken">Cancellation token for the export request.</param>
    /// <returns>The count returned by Splunk, or <c>0</c> when no aggregate row is returned.</returns>
    Task<long> CountErrorsAsync(ErrorCountQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the average of a numeric metric field in a team index.
    /// </summary>
    /// <param name="query">Index, numeric field, filters, text predicate, and time range.</param>
    /// <param name="cancellationToken">Cancellation token for the export request.</param>
    /// <returns>The average value, or <c>null</c> when Splunk returns no value.</returns>
    Task<double?> AverageAsync(AverageMetricQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a time-bucketed average series for a numeric metric field.
    /// </summary>
    /// <param name="query">Index, numeric field, timechart span, filters, and time range.</param>
    /// <param name="cancellationToken">Cancellation token for the export request.</param>
    /// <returns>Ordered time buckets returned by Splunk.</returns>
    Task<IReadOnlyList<MetricTimeBucket>> AverageTimeSeriesAsync(
        MetricTimeSeriesQuery query,
        CancellationToken cancellationToken = default);
}
