namespace Marouanvs.Splunk.Models;

/// <summary>
/// Describes a Splunk search request.
/// </summary>
/// <remarks>
/// The <see cref="Search"/> value is full SPL and is treated as trusted input.
/// Use analytics helpers or <see cref="Marouanvs.Splunk.Search.SplunkQueryBuilder"/>
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
    /// <para>
    /// The following keys are reserved and rejected with
    /// <see cref="SplunkConfigurationException"/> because the SDK owns them:
    /// <c>search</c>, <c>output_mode</c>, and <c>exec_mode</c> are always
    /// reserved; <c>earliest_time</c> and <c>latest_time</c> are reserved when
    /// <see cref="TimeRange"/> is set; <c>count</c> is reserved when
    /// <see cref="Count"/> is set; and <c>preview</c> is reserved when
    /// <see cref="Preview"/> is set or the SDK applies its export preview
    /// default. Rejecting collisions avoids sending duplicate REST parameters
    /// with ambiguous server-side precedence.
    /// </para>
    /// <para>
    /// Result-only parameters such as <c>count</c> and <c>preview</c> are
    /// dropped for <see cref="Marouanvs.Splunk.Search.ISplunkSearchClient.StartAsync"/>
    /// job dispatch; oneshot and export requests send them. Prefer setting
    /// them through <see cref="Count"/> and <see cref="Preview"/>.
    /// </para>
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
    /// <para>
    /// When this is <c>null</c>, streaming export sends <c>preview=false</c>
    /// explicitly: final results are the SDK's documented contract, because
    /// preview frames carry partial aggregates that silently corrupt consumers
    /// that treat every row as final. Set <see langword="true"/> to opt into
    /// preview frames and check <see cref="SplunkSearchResult.Preview"/> on
    /// each row.
    /// </para>
    /// <para>
    /// Preview affects export streams only.
    /// <see cref="Marouanvs.Splunk.Search.ISplunkSearchClient.StartAsync"/>
    /// omits it from job dispatch;
    /// <see cref="Marouanvs.Splunk.Search.ISplunkSearchClient.OneshotSearchAsync"/>
    /// includes it in the dispatch form when set, where Splunk ignores it
    /// because oneshot returns final results.
    /// </para>
    /// </remarks>
    public bool? Preview { get; init; }

    internal IEnumerable<KeyValuePair<string, string>> ToFormParameters(
        SplunkOutputMode outputMode,
        bool includeResultOptions = true,
        bool defaultPreviewToFalse = false)
    {
        var effectivePreview = includeResultOptions
            ? Preview ?? (defaultPreviewToFalse ? false : (bool?)null)
            : null;
        ValidateParameterCollisions(includeResultOptions, previewIsSdkOwned: effectivePreview is not null);

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

        if (effectivePreview is not null)
        {
            yield return new KeyValuePair<string, string>("preview", effectivePreview.Value ? "true" : "false");
        }

        if (Parameters is null)
        {
            yield break;
        }

        foreach (var parameter in Parameters)
        {
            if (!includeResultOptions &&
                (string.Equals(parameter.Key, "count", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(parameter.Key, "preview", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return parameter;
        }
    }

    private void ValidateParameterCollisions(bool includeResultOptions, bool previewIsSdkOwned)
    {
        if (Parameters is null)
        {
            return;
        }

        foreach (var key in Parameters.Keys)
        {
            if (string.Equals(key, "search", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "output_mode", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "exec_mode", StringComparison.OrdinalIgnoreCase))
            {
                throw new SplunkConfigurationException(
                    $"REST parameter '{key}' is reserved because the SDK controls it for search requests.");
            }

            if (TimeRange is not null &&
                (string.Equals(key, "earliest_time", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(key, "latest_time", StringComparison.OrdinalIgnoreCase)))
            {
                throw new SplunkConfigurationException(
                    $"REST parameter '{key}' collides with the TimeRange property. Set time bounds through TimeRange only.");
            }

            if (includeResultOptions &&
                Count is not null &&
                string.Equals(key, "count", StringComparison.OrdinalIgnoreCase))
            {
                throw new SplunkConfigurationException(
                    "REST parameter 'count' collides with the Count property. Set the row limit through Count only.");
            }

            if (previewIsSdkOwned &&
                string.Equals(key, "preview", StringComparison.OrdinalIgnoreCase))
            {
                throw new SplunkConfigurationException(
                    "REST parameter 'preview' collides with the Preview property. Set preview behavior through Preview only.");
            }
        }
    }
}
