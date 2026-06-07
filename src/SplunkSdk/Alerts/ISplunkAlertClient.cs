using SplunkSdk.Models;

namespace SplunkSdk.Alerts;

/// <summary>
/// Manages Splunk saved-search alerts.
/// </summary>
public interface ISplunkAlertClient
{
    /// <summary>
    /// Creates a scheduled saved-search alert.
    /// </summary>
    /// <remarks>
    /// Splunk sends notifications only through the alert actions configured on
    /// the saved search. For email delivery, enable the <c>email</c> action and
    /// provide recipients with a saved-search parameter such as
    /// <c>action.email.to</c>.
    /// </remarks>
    /// <param name="request">Alert definition and action settings.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    /// <returns>The saved-search alert returned by Splunk.</returns>
    Task<SplunkSavedSearch> CreateAsync(
        CreateSplunkAlertRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges a tracked alert without changing the saved-search definition.
    /// </summary>
    /// <remarks>
    /// This posts to Splunk's saved-search <c>acknowledge</c> endpoint. It is
    /// useful when alert tracking is enabled and an operator wants to mark the
    /// alert state as acknowledged.
    /// </remarks>
    /// <param name="name">Saved-search alert name.</param>
    /// <param name="splunkNamespace">Optional owner/app namespace for the alert.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    Task AcknowledgeAsync(
        string name,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suppresses an alert for the supplied period.
    /// </summary>
    /// <remarks>
    /// Suppression is an operational action on the alert state; it does not
    /// disable or delete the saved-search alert definition.
    /// </remarks>
    /// <param name="name">Saved-search alert name.</param>
    /// <param name="period">Splunk suppression period, for example <c>30m</c> or <c>1h</c>.</param>
    /// <param name="splunkNamespace">Optional owner/app namespace for the alert.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    Task SuppressAsync(
        string name,
        string period,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default);
}
