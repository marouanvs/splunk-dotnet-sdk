namespace Marouanvs.Splunk.Models;

/// <summary>
/// Saved search or alert configuration returned by Splunk.
/// </summary>
public sealed record SplunkSavedSearch
{
    /// <summary>
    /// Gets the saved search name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the SPL stored in the saved search.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>
    /// Gets the saved search description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets whether the saved search is scheduled.
    /// </summary>
    public bool IsScheduled { get; init; }

    /// <summary>
    /// Gets the cron schedule for scheduled searches.
    /// </summary>
    public string? CronSchedule { get; init; }

    /// <summary>
    /// Gets whether the saved search is disabled.
    /// </summary>
    public bool Disabled { get; init; }

    /// <summary>
    /// Gets dispatch settings stored on the saved search.
    /// </summary>
    public SplunkSavedSearchDispatchSettings? Dispatch { get; init; }

    /// <summary>
    /// Gets alert settings when the saved search has alert-related fields.
    /// </summary>
    public SplunkAlertSettings? Alert { get; init; }

    /// <summary>
    /// Gets raw Splunk content keys for advanced settings not modeled by the SDK.
    /// </summary>
    public IReadOnlyDictionary<string, string> Content { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// Options for listing saved searches.
/// </summary>
public sealed record SplunkSavedSearchListRequest
{
    /// <summary>
    /// Gets an optional namespace overriding the client's default namespace.
    /// </summary>
    public SplunkNamespace? Namespace { get; init; }

    /// <summary>
    /// Gets the maximum number of entries to request from Splunk.
    /// </summary>
    public int? Count { get; init; }

    /// <summary>
    /// Gets the first entry offset.
    /// </summary>
    public int? Offset { get; init; }

    internal IEnumerable<KeyValuePair<string, string>> ToQueryParameters()
    {
        yield return new KeyValuePair<string, string>("output_mode", "json");

        if (Count is not null)
        {
            if (Count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Count), "Count must be zero or greater.");
            }

            yield return new KeyValuePair<string, string>("count", Count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (Offset is not null)
        {
            if (Offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Offset), "Offset must be zero or greater.");
            }

            yield return new KeyValuePair<string, string>("offset", Offset.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>
/// Request for creating a saved search.
/// </summary>
/// <param name="Name">Saved search name to create.</param>
/// <param name="Search">SPL stored in the saved search.</param>
public sealed record CreateSavedSearchRequest(string Name, string Search)
{
    /// <summary>
    /// Gets an optional namespace overriding the client's default namespace.
    /// </summary>
    public SplunkNamespace? Namespace { get; init; }

    /// <summary>
    /// Gets an optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets whether the saved search is scheduled.
    /// </summary>
    public bool IsScheduled { get; init; }

    /// <summary>
    /// Gets the cron schedule for scheduled searches.
    /// </summary>
    public string? CronSchedule { get; init; }

    /// <summary>
    /// Gets dispatch time range parameters stored on the saved search.
    /// </summary>
    public SplunkTimeRange? TimeRange { get; init; }

    /// <summary>
    /// Gets optional dispatch settings stored on the saved search.
    /// </summary>
    public SplunkSavedSearchDispatchSettings? Dispatch { get; init; }

    /// <summary>
    /// Gets whether the search should be created disabled.
    /// </summary>
    public bool Disabled { get; init; }

    /// <summary>
    /// Gets additional Splunk saved search parameters.
    /// </summary>
    /// <remarks>
    /// Use this for saved-search fields that the SDK does not model directly,
    /// including Splunk alert-action fields such as <c>action.email.to</c>.
    /// </remarks>
    public IReadOnlyDictionary<string, string> AdditionalParameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// Request for updating an existing saved search.
/// </summary>
public sealed record UpdateSavedSearchRequest
{
    /// <summary>
    /// Gets an optional namespace overriding the client's default namespace.
    /// </summary>
    public SplunkNamespace? Namespace { get; init; }

    /// <summary>
    /// Gets replacement SPL.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>
    /// Gets replacement description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets whether to schedule or unschedule the saved search.
    /// </summary>
    public bool? IsScheduled { get; init; }

    /// <summary>
    /// Gets a replacement cron schedule.
    /// </summary>
    public string? CronSchedule { get; init; }

    /// <summary>
    /// Gets replacement dispatch time range parameters.
    /// </summary>
    public SplunkTimeRange? TimeRange { get; init; }

    /// <summary>
    /// Gets replacement dispatch settings.
    /// </summary>
    public SplunkSavedSearchDispatchSettings? Dispatch { get; init; }

    /// <summary>
    /// Gets whether the search should be disabled.
    /// </summary>
    public bool? Disabled { get; init; }

    /// <summary>
    /// Gets additional Splunk saved search parameters.
    /// </summary>
    /// <remarks>
    /// Use this for saved-search fields that the SDK does not model directly,
    /// including Splunk alert-action fields such as <c>action.email.to</c>.
    /// </remarks>
    public IReadOnlyDictionary<string, string> AdditionalParameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// Dispatch settings stored on a Splunk saved search.
/// </summary>
public sealed record SplunkSavedSearchDispatchSettings
{
    /// <summary>
    /// Gets the maximum number of timeline buckets.
    /// </summary>
    public int? Buckets { get; init; }

    /// <summary>
    /// Gets the maximum number of results before finalizing the search.
    /// </summary>
    public int? MaxCount { get; init; }

    /// <summary>
    /// Gets whether lookups are enabled for the search.
    /// </summary>
    public bool? Lookups { get; init; }

    /// <summary>
    /// Gets the time format used when absolute dispatch times are supplied.
    /// </summary>
    public string? TimeFormat { get; init; }
}

/// <summary>
/// Options used when dispatching a saved search.
/// </summary>
public sealed record SplunkDispatchSavedSearchRequest
{
    /// <summary>
    /// Gets an optional namespace overriding the client's default namespace.
    /// </summary>
    public SplunkNamespace? Namespace { get; init; }

    /// <summary>
    /// Gets optional dispatch arguments such as saved search template args.
    /// </summary>
    /// <remarks>
    /// The SDK owns <c>output_mode</c> for dispatch responses; supplying it here
    /// causes the dispatch call to throw <see cref="SplunkConfigurationException"/>.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
