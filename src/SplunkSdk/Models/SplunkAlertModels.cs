namespace Marouanvs.Splunk.Models;

/// <summary>
/// Splunk saved-search alert severity levels.
/// </summary>
/// <remarks>
/// Splunk saved searches use <c>1=debug</c>, <c>2=info</c>,
/// <c>3=warn</c>, <c>4=error</c>, <c>5=severe</c>, and <c>6=fatal</c>
/// for the <c>alert.severity</c> field.
/// </remarks>
public enum SplunkAlertSeverity
{
    /// <summary>Debug severity.</summary>
    Debug = 1,

    /// <summary>Info severity.</summary>
    Info = 2,

    /// <summary>Warn severity.</summary>
    Warn = 3,

    /// <summary>Error severity.</summary>
    Error = 4,

    /// <summary>Severe severity.</summary>
    Severe = 5,

    /// <summary>Fatal severity.</summary>
    Fatal = 6
}

/// <summary>
/// Base condition type for a Splunk saved search alert.
/// </summary>
public enum SplunkAlertType
{
    /// <summary>Always trigger when the scheduled search runs.</summary>
    Always,

    /// <summary>Use a custom alert condition search.</summary>
    Custom,

    /// <summary>Trigger based on event count.</summary>
    NumberOfEvents,

    /// <summary>Trigger based on host count.</summary>
    NumberOfHosts,

    /// <summary>Trigger based on source count.</summary>
    NumberOfSources
}

/// <summary>
/// Comparator for count-based Splunk alerts.
/// </summary>
public enum SplunkAlertComparator
{
    /// <summary>Greater than threshold.</summary>
    GreaterThan,

    /// <summary>Less than threshold.</summary>
    LessThan,

    /// <summary>Equal to threshold.</summary>
    EqualTo,

    /// <summary>Rises by threshold.</summary>
    RisesBy,

    /// <summary>Drops by threshold.</summary>
    DropsBy,

    /// <summary>Rises by percentage threshold.</summary>
    RisesByPercentage,

    /// <summary>Drops by percentage threshold.</summary>
    DropsByPercentage
}

/// <summary>
/// Alert-related fields on a saved search.
/// </summary>
public sealed record SplunkAlertSettings
{
    /// <summary>
    /// Gets the Splunk alert type.
    /// </summary>
    public SplunkAlertType? AlertType { get; init; }

    /// <summary>
    /// Gets the alert comparator.
    /// </summary>
    public SplunkAlertComparator? Comparator { get; init; }

    /// <summary>
    /// Gets the alert threshold.
    /// </summary>
    public string? Threshold { get; init; }

    /// <summary>
    /// Gets a custom alert condition search.
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>
    /// Gets the severity.
    /// </summary>
    public SplunkAlertSeverity? Severity { get; init; }

    /// <summary>
    /// Gets how long triggered alert records remain visible, for example <c>24h</c>.
    /// </summary>
    public string? Expires { get; init; }

    /// <summary>
    /// Gets whether Splunk tracks triggered alerts.
    /// </summary>
    public bool? Track { get; init; }

    /// <summary>
    /// Gets whether alert actions run for the whole result set.
    /// </summary>
    public bool? DigestMode { get; init; }

    /// <summary>
    /// Gets alert suppression settings stored on the saved search.
    /// </summary>
    public SplunkAlertSuppressionSettings? Suppression { get; init; }

    /// <summary>
    /// Gets email action settings. Setting this enables the <c>email</c> alert action.
    /// </summary>
    public SplunkEmailAlertActionSettings? Email { get; init; }

    /// <summary>
    /// Gets summary index action settings. Setting this enables the <c>summary_index</c> action.
    /// </summary>
    public SplunkSummaryIndexAlertActionSettings? SummaryIndex { get; init; }

    /// <summary>
    /// Gets enabled alert action names, such as <c>email</c> or a custom action installed in Splunk.
    /// </summary>
    /// <remarks>
    /// This only enables the action on the saved search. Action-specific settings,
    /// for example <c>action.email.to</c>, are supplied through
    /// <see cref="CreateSplunkAlertRequest.AdditionalParameters"/>.
    /// </remarks>
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Suppression settings for a Splunk saved-search alert.
/// </summary>
public sealed record SplunkAlertSuppressionSettings
{
    /// <summary>
    /// Gets whether alert suppression is enabled.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>
    /// Gets the suppression period, for example <c>30m</c> or <c>1h</c>.
    /// </summary>
    public string? Period { get; init; }

    /// <summary>
    /// Gets fields used for per-result suppression.
    /// </summary>
    public IReadOnlyList<string> Fields { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Current suppression state reported by the Splunk saved-search suppress endpoint.
/// </summary>
/// <remarks>
/// This reflects the operational suppression state returned by
/// <c>saved/searches/{name}/suppress</c>, not the configured
/// <c>alert.suppress.period</c> stored on the saved search.
/// </remarks>
public sealed record SplunkAlertSuppression
{
    /// <summary>
    /// Gets whether the alert is currently suppressed.
    /// </summary>
    public required bool Suppressed { get; init; }

    /// <summary>
    /// Gets the remaining suppression time reported by Splunk.
    /// </summary>
    /// <remarks>
    /// Splunk reports this as whole seconds. The value is
    /// <see cref="TimeSpan.Zero"/> when the alert is not suppressed.
    /// </remarks>
    public TimeSpan Expiration { get; init; }
}

/// <summary>
/// Email action settings for a Splunk saved-search alert.
/// </summary>
public sealed record SplunkEmailAlertActionSettings
{
    /// <summary>
    /// Gets recipient email addresses.
    /// </summary>
    public IReadOnlyList<string> To { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets carbon-copy recipient email addresses.
    /// </summary>
    public IReadOnlyList<string> Cc { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets blind-carbon-copy recipient email addresses.
    /// </summary>
    public IReadOnlyList<string> Bcc { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the email subject.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// Gets the alert email body.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the SMTP authentication username override for this alert action.
    /// </summary>
    public string? AuthUsername { get; init; }

    /// <summary>
    /// Gets the dashboard view to send when PDF delivery is enabled.
    /// </summary>
    public string? PdfView { get; init; }
}

/// <summary>
/// Summary index action settings for a Splunk saved-search alert.
/// </summary>
public sealed record SplunkSummaryIndexAlertActionSettings
{
    /// <summary>
    /// Gets the target summary index name.
    /// </summary>
    public string? Name { get; init; }
}

/// <summary>
/// Request for creating a scheduled Splunk alert.
/// </summary>
/// <param name="Name">Saved-search alert name to create.</param>
/// <param name="Search">SPL stored in the scheduled alert.</param>
/// <param name="CronSchedule">Splunk cron expression controlling alert schedule.</param>
public sealed record CreateSplunkAlertRequest(string Name, string Search, string CronSchedule)
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
    /// Gets dispatch time range parameters stored on the alert.
    /// </summary>
    public SplunkTimeRange? TimeRange { get; init; }

    /// <summary>
    /// Gets alert trigger and action settings.
    /// </summary>
    /// <remarks>
    /// These settings define when Splunk triggers the saved-search alert and
    /// which alert actions are enabled. They do not, by themselves, define who
    /// receives a notification.
    /// </remarks>
    public SplunkAlertSettings Alert { get; init; } = new()
    {
        AlertType = SplunkAlertType.NumberOfEvents,
        Comparator = SplunkAlertComparator.GreaterThan,
        Threshold = "0",
        Severity = SplunkAlertSeverity.Error,
        Track = true,
        DigestMode = true
    };

    /// <summary>
    /// Gets whether the alert should be created disabled.
    /// </summary>
    public bool Disabled { get; init; }

    /// <summary>
    /// Gets additional saved search parameters, including action-specific delivery settings.
    /// </summary>
    /// <remarks>
    /// Use this for Splunk saved-search fields that the SDK does not model
    /// directly, such as <c>action.email.to</c>, <c>action.email.subject</c>,
    /// webhook action parameters, or custom alert action settings.
    /// </remarks>
    public IReadOnlyDictionary<string, string> AdditionalParameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
