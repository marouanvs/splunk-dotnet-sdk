using System.Globalization;
using SplunkSdk.Models;
using SplunkSdk.Search;

namespace SplunkSdk.Analytics;

/// <summary>
/// Default high-level analytics implementation.
/// </summary>
public sealed class SplunkAnalyticsClient : ISplunkAnalyticsClient
{
    private const string ErrorCountAlias = "error_count";
    private const string AverageAlias = "average_value";

    private readonly ISplunkSearchClient _searchClient;

    internal SplunkAnalyticsClient(ISplunkSearchClient searchClient)
    {
        _searchClient = searchClient;
    }

    /// <inheritdoc />
    public async Task<long> CountErrorsAsync(ErrorCountQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var builder = BuildBaseQuery(query.Index, query.Text, query.FieldFilters, query.RawPredicate)
            .StatsCount(ErrorCountAlias);

        var row = await FirstOrDefaultAsync(
            new SplunkSearchRequest(builder.Build()) { TimeRange = query.TimeRange, Count = 1 },
            cancellationToken)
            .ConfigureAwait(false);

        return row?.GetInt64(ErrorCountAlias) ?? 0;
    }

    /// <inheritdoc />
    public async Task<double?> AverageAsync(AverageMetricQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SplunkSearchSyntax.ValidateFieldName(query.Field, nameof(query.Field));

        var builder = BuildBaseQuery(query.Index, query.Text, query.FieldFilters, query.RawPredicate)
            .StatsAverage(query.Field, AverageAlias);

        var row = await FirstOrDefaultAsync(
            new SplunkSearchRequest(builder.Build()) { TimeRange = query.TimeRange, Count = 1 },
            cancellationToken)
            .ConfigureAwait(false);

        return row?.GetDouble(AverageAlias);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MetricTimeBucket>> AverageTimeSeriesAsync(
        MetricTimeSeriesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SplunkSearchSyntax.ValidateFieldName(query.Field, nameof(query.Field));
        SplunkSearchSyntax.ValidateSpan(query.Span, nameof(query.Span));

        var builder = BuildBaseQuery(query.Index, query.Text, query.FieldFilters, rawPredicate: null)
            .TimechartAverage(query.Span, query.Field, AverageAlias);

        var request = new SplunkSearchRequest(builder.Build()) { TimeRange = query.TimeRange, Count = 0 };
        var buckets = new List<MetricTimeBucket>();

        await foreach (var row in _searchClient.ExportAsync(request, cancellationToken).ConfigureAwait(false))
        {
            buckets.Add(new MetricTimeBucket(ParseSplunkTime(row.GetString("_time")), row.GetDouble(AverageAlias)));
        }

        return buckets;
    }

    private async Task<SplunkSearchResult?> FirstOrDefaultAsync(
        SplunkSearchRequest request,
        CancellationToken cancellationToken)
    {
        SplunkSearchResult? first = null;
        await foreach (var row in _searchClient.ExportAsync(request, cancellationToken).ConfigureAwait(false))
        {
            first ??= row;
        }

        return first;
    }

    private static SplunkQueryBuilder BuildBaseQuery(
        string index,
        string? text,
        IReadOnlyDictionary<string, string>? fieldFilters,
        string? rawPredicate)
    {
        var builder = SplunkQueryBuilder.FromIndex(index);

        if (!string.IsNullOrWhiteSpace(text))
        {
            builder.SearchText(text);
        }

        if (fieldFilters is not null)
        {
            foreach (var filter in fieldFilters)
            {
                builder.FieldEquals(filter.Key, filter.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(rawPredicate))
        {
            builder.RawPredicate(rawPredicate!);
        }

        return builder;
    }

    private static DateTimeOffset? ParseSplunkTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var epoch))
        {
            var seconds = Math.Truncate(epoch);
            var fractional = epoch - seconds;
            return DateTimeOffset.FromUnixTimeSeconds((long)seconds).AddSeconds(fractional);
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
