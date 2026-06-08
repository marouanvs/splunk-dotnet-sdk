namespace SplunkSdk.Models;

/// <summary>
/// Describes a Splunk search request.
/// </summary>
/// <remarks>
/// The <see cref="Search"/> value is full SPL and is treated as trusted input.
/// Use analytics helpers or <see cref="SplunkSdk.Search.SplunkQueryBuilder"/>
/// when composing SPL from user-provided values.
/// </remarks>
public sealed record SplunkSearchRequest
{
    /// <summary>
    /// Initializes a search request.
    /// </summary>
    /// <param name="search">Complete trusted SPL search string.</param>
    public SplunkSearchRequest(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            throw new ArgumentException("A Splunk search string is required.", nameof(search));
        }

        Search = search;
    }

    /// <summary>
    /// Gets the SPL search string.
    /// </summary>
    public string Search { get; }

    /// <summary>
    /// Gets optional time bounds supplied as REST parameters.
    /// </summary>
    public SplunkTimeRange? TimeRange { get; init; }

    /// <summary>
    /// Gets an optional namespace overriding the client's default namespace.
    /// </summary>
    public SplunkNamespace? Namespace { get; init; }

    /// <summary>
    /// Gets optional additional Splunk REST parameters.
    /// </summary>
    /// <remarks>
    /// The SDK owns <c>search</c> and <c>output_mode</c>. Result-only parameters
    /// such as <c>count</c> and <c>preview</c> are ignored for job dispatch and
    /// should be set through <see cref="Count"/> and <see cref="Preview"/>.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the maximum number of rows to return when a result endpoint supports <c>count</c>.
    /// A value of <c>0</c> means all available rows in Splunk REST APIs that support it.
    /// </summary>
    public int? Count { get; init; }

    /// <summary>
    /// Gets whether preview results are enabled for export.
    /// </summary>
    /// <remarks>
    /// Preview applies to export streams. It is not sent to the job dispatch
    /// endpoint.
    /// </remarks>
    public bool? Preview { get; init; }

    internal IEnumerable<KeyValuePair<string, string>> ToFormParameters(
        SplunkOutputMode outputMode,
        bool includeResultOptions = true)
    {
        yield return new KeyValuePair<string, string>("search", Search);
        yield return new KeyValuePair<string, string>("output_mode", outputMode.ToSplunkValue());

        if (TimeRange is not null)
        {
            foreach (var item in TimeRange.ToFormParameters())
            {
                yield return item;
            }
        }

        if (includeResultOptions && Count is not null)
        {
            if (Count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Count), "Count must be zero or greater.");
            }

            yield return new KeyValuePair<string, string>("count", Count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (includeResultOptions && Preview is not null)
        {
            yield return new KeyValuePair<string, string>("preview", Preview.Value ? "true" : "false");
        }

        if (Parameters is null)
        {
            yield break;
        }

        foreach (var parameter in Parameters)
        {
            if (string.Equals(parameter.Key, "search", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parameter.Key, "output_mode", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Parameter '{parameter.Key}' is controlled by the SDK for search requests.", nameof(Parameters));
            }

            if (!includeResultOptions &&
                (string.Equals(parameter.Key, "count", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(parameter.Key, "preview", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return parameter;
        }
    }
}
