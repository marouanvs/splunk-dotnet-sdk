namespace SplunkSdk.Models;

/// <summary>
/// Describes options for fetching rows from an existing Splunk search job.
/// </summary>
public sealed record SplunkResultRequest
{
    /// <summary>
    /// Default number of rows buffered by <c>GetResultsAsync</c> when no count is supplied.
    /// </summary>
    public const int DefaultCount = 1000;

    /// <summary>
    /// Gets an optional namespace overriding the client's default namespace.
    /// </summary>
    public SplunkNamespace? Namespace { get; init; }

    /// <summary>
    /// Gets the maximum number of results to return.
    /// </summary>
    /// <remarks>
    /// Buffered result reads are bounded by default. Use a positive page size and
    /// advance <see cref="Offset"/> for pagination, or use streaming export APIs
    /// for large result streams.
    /// </remarks>
    public int Count { get; init; } = DefaultCount;

    /// <summary>
    /// Gets the first result offset.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets fields to return through repeated <c>f</c> parameters.
    /// </summary>
    public IReadOnlyList<string> Fields { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets an optional post-processing search applied by Splunk to job results.
    /// </summary>
    /// <remarks>
    /// Post-process search is raw SPL executed by Splunk against existing job
    /// results. Treat it as trusted input.
    /// </remarks>
    public string? PostProcessSearch { get; init; }

    internal IEnumerable<KeyValuePair<string, string>> ToQueryParameters(SplunkOutputMode outputMode)
    {
        if (Count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Count), "Count must be greater than zero for buffered result reads.");
        }

        if (Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Offset), "Offset must be zero or greater.");
        }

        yield return new KeyValuePair<string, string>("output_mode", outputMode.ToSplunkValue());
        yield return new KeyValuePair<string, string>("count", Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        yield return new KeyValuePair<string, string>("offset", Offset.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (Fields is null)
        {
            yield break;
        }

        foreach (var field in Fields)
        {
            SplunkSearchSyntax.ValidateFieldName(field, nameof(Fields));
            yield return new KeyValuePair<string, string>("f", field);
        }

        if (!string.IsNullOrWhiteSpace(PostProcessSearch))
        {
            yield return new KeyValuePair<string, string>("search", PostProcessSearch!);
        }
    }
}
