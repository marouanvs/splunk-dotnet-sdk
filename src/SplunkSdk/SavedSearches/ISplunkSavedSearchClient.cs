using SplunkSdk.Models;

namespace SplunkSdk.SavedSearches;

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
    Task<SplunkSavedSearch> CreateAsync(
        CreateSavedSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a saved search.
    /// </summary>
    /// <param name="name">Saved search name.</param>
    /// <param name="request">Fields to replace on the saved search.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    /// <returns>The updated saved search returned by Splunk.</returns>
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
    /// <param name="name">Saved search name.</param>
    /// <param name="request">Optional namespace and dispatch parameters.</param>
    /// <param name="cancellationToken">Cancellation token for the REST request.</param>
    /// <returns>The created Splunk search job.</returns>
    Task<SplunkSearchJob> DispatchAsync(
        string name,
        SplunkDispatchSavedSearchRequest? request = null,
        CancellationToken cancellationToken = default);
}
