using Marouanvs.Splunk.Models;

namespace Marouanvs.Splunk.SavedSearches;

/// <summary>
/// Manages Splunk saved searches.
/// </summary>
/// <remarks>
/// Saved searches are Splunk knowledge objects. Creating, updating, or deleting
/// them changes state on the target Splunk deployment and should use an
/// appropriately scoped app namespace and role.
/// </remarks>
public interface ISplunkSavedSearchClient
{
    /// <summary>
    /// Lists saved searches.
    /// </summary>
    /// <param name="request">Optional namespace and paging options.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    /// <returns>Saved searches visible to the configured token.</returns>
    Task<IReadOnlyList<SplunkSavedSearch>> ListAsync(
        SplunkSavedSearchListRequest? request = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one saved search by name.
    /// </summary>
    /// <param name="name">Saved search name.</param>
    /// <param name="splunkNamespace">Optional owner/app namespace for the lookup.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    /// <returns>The saved search, or <c>null</c> when Splunk returns no entry.</returns>
    Task<SplunkSavedSearch?> GetAsync(
        string name,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a saved search.
    /// </summary>
    /// <param name="request">Saved-search definition and optional Splunk parameters.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    /// <returns>The saved search returned by Splunk.</returns>
    /// <exception cref="SplunkResponseFormatException">
    /// Splunk returned a success status without a parseable saved search entry.
    /// </exception>
    Task<SplunkSavedSearch> CreateAsync(
        CreateSavedSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a saved search.
    /// </summary>
    /// <remarks>
    /// Setting <see cref="UpdateSavedSearchRequest.IsScheduled"/> to <c>true</c>
    /// requires a <see cref="UpdateSavedSearchRequest.CronSchedule"/>, matching
    /// the validation applied by <see cref="CreateAsync"/>.
    /// </remarks>
    /// <param name="name">Saved search name.</param>
    /// <param name="request">Fields to replace on the saved search.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    /// <returns>The updated saved search returned by Splunk.</returns>
    /// <exception cref="SplunkResponseFormatException">
    /// Splunk returned a success status without a parseable saved search entry.
    /// </exception>
    Task<SplunkSavedSearch> UpdateAsync(
        string name,
        UpdateSavedSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a saved search.
    /// </summary>
    /// <param name="name">Saved search name.</param>
    /// <param name="splunkNamespace">Optional owner/app namespace for the delete.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    Task DeleteAsync(
        string name,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a saved search and returns the created search job.
    /// </summary>
    /// <remarks>
    /// The SDK requests <c>output_mode=json</c> for the dispatch response, so
    /// <see cref="SplunkDispatchSavedSearchRequest.Parameters"/> must not supply
    /// <c>output_mode</c>.
    /// </remarks>
    /// <param name="name">Saved search name.</param>
    /// <param name="request">Optional namespace and dispatch parameters.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    /// <returns>The created Splunk search job.</returns>
    /// <exception cref="SplunkConfigurationException">
    /// A dispatch parameter name is empty or reserved by the SDK.
    /// </exception>
    /// <exception cref="SplunkResponseFormatException">
    /// Splunk returned a success status without a parseable search ID.
    /// </exception>
    Task<SplunkSearchJob> DispatchAsync(
        string name,
        SplunkDispatchSavedSearchRequest? request = null,
        CancellationToken cancellationToken = default);
}
