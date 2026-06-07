namespace SplunkSdk.Authentication;

/// <summary>
/// Supplies Splunk authentication tokens for outbound REST calls.
/// </summary>
/// <remarks>
/// Implement this interface when tokens are loaded from a secret store, rotated at runtime,
/// or refreshed through a privileged service. Implementations must not log token values.
/// </remarks>
public interface ISplunkTokenProvider
{
    /// <summary>
    /// Gets the token to place in the Splunk REST API <c>Authorization</c> header.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for token retrieval.</param>
    /// <returns>The full Splunk authentication token.</returns>
    ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default);
}
