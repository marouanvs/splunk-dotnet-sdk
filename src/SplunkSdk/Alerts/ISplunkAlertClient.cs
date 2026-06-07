using Marouanvs.Splunk.Models;

namespace Marouanvs.Splunk.Alerts;

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
    /// Suppresses an alert until the supplied expiration elapses.
    /// </summary>
    /// <remarks>
    /// This posts <c>expiration</c> in whole seconds to Splunk's saved-search
    /// <c>suppress</c> endpoint. Suppression is an operational action on the
    /// alert state; it does not disable or delete the saved-search alert
    /// definition, and it is separate from the configured
    /// <c>alert.suppress.period</c> saved-search field.
    /// </remarks>
    /// <param name="name">Saved-search alert name.</param>
    /// <param name="expiration">How long the alert stays suppressed. Sub-second values round up to one second.</param>
    /// <param name="splunkNamespace">Optional owner/app namespace for the alert.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expiration"/> is zero or negative.</exception>
    Task SuppressAsync(
        string name,
        TimeSpan expiration,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an active suppression from an alert.
    /// </summary>
    /// <remarks>
    /// This posts <c>expiration=0</c> to Splunk's saved-search <c>suppress</c>
    /// endpoint, which clears any in-progress suppression window.
    /// </remarks>
    /// <param name="name">Saved-search alert name.</param>
    /// <param name="splunkNamespace">Optional owner/app namespace for the alert.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    Task UnsuppressAsync(
        string name,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the current suppression state of an alert.
    /// </summary>
    /// <param name="name">Saved-search alert name.</param>
    /// <param name="splunkNamespace">Optional owner/app namespace for the alert.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    /// <returns>The suppression state reported by Splunk.</returns>
    /// <exception cref="SplunkResponseFormatException">
    /// Splunk returned a success status without a parseable suppression entry.
    /// </exception>
    Task<SplunkAlertSuppression> GetSuppressionAsync(
        string name,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists fired-alert groups visible to the configured token.
    /// </summary>
    /// <remarks>
    /// This is a read-only call to <c>alerts/fired_alerts</c>. Each group is
    /// keyed by the saved search that triggered the alerts.
    /// </remarks>
    /// <param name="splunkNamespace">Optional owner/app namespace for the listing.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    /// <returns>Fired-alert groups reported by Splunk.</returns>
    Task<IReadOnlyList<SplunkFiredAlertGroup>> ListFiredAlertGroupsAsync(
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists fired (triggered) alert records for one fired-alert group.
    /// </summary>
    /// <remarks>
    /// This is a read-only call to <c>alerts/fired_alerts/{name}</c>. Alert
    /// records require alert tracking to be enabled on the saved search.
    /// </remarks>
    /// <param name="savedSearchName">Fired-alert group name, usually the triggering saved-search name.</param>
    /// <param name="splunkNamespace">Optional owner/app namespace for the listing.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    /// <returns>Fired alerts reported by Splunk for the group.</returns>
    Task<IReadOnlyList<SplunkFiredAlert>> ListFiredAlertsAsync(
        string savedSearchName,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default);
}
