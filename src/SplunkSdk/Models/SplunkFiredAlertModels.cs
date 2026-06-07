namespace Marouanvs.Splunk.Models;

/// <summary>
/// Summary entry for a group of fired alerts returned by <c>alerts/fired_alerts</c>.
/// </summary>
/// <remarks>
/// Splunk groups fired (triggered) alerts by the saved search that raised them.
/// Use the group name with the per-group fired-alert listing to read individual
/// triggered alert records.
/// </remarks>
public sealed record SplunkFiredAlertGroup
{
    /// <summary>
    /// Gets the fired-alert group name, usually the triggering saved-search name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the number of triggered alerts reported for the group, when available.
    /// </summary>
    public int? TriggeredAlertCount { get; init; }
}

/// <summary>
/// A single fired (triggered) alert record returned by <c>alerts/fired_alerts/{name}</c>.
/// </summary>
public sealed record SplunkFiredAlert
{
    /// <summary>
    /// Gets the fired-alert entry name assigned by Splunk.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the saved-search name that triggered the alert, when reported.
    /// </summary>
    public string? SavedSearchName { get; init; }

    /// <summary>
    /// Gets the search ID of the dispatched search that triggered the alert, when reported.
    /// </summary>
    public string? SearchId { get; init; }

    /// <summary>
    /// Gets the Splunk alert type label, for example <c>historical</c>, when reported.
    /// </summary>
    public string? AlertType { get; init; }

    /// <summary>
    /// Gets the alert severity when Splunk reports a value on the documented 1-6 scale.
    /// </summary>
    public SplunkAlertSeverity? Severity { get; init; }

    /// <summary>
    /// Gets when the alert triggered, when Splunk reports a parseable trigger time.
    /// </summary>
    public DateTimeOffset? TriggerTime { get; init; }

    /// <summary>
    /// Gets the number of triggered alert records for digest-mode alerts, when available.
    /// </summary>
    public int? TriggeredAlertCount { get; init; }

    /// <summary>
    /// Gets the alert action names that ran for this fired alert.
    /// </summary>
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
}
