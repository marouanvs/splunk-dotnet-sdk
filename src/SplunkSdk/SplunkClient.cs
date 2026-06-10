using Marouanvs.Splunk.Analytics;
using Marouanvs.Splunk.Alerts;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.SavedSearches;
using Marouanvs.Splunk.Search;

namespace Marouanvs.Splunk;

/// <summary>
/// Entry point for querying Splunk Enterprise or Splunk Cloud REST endpoints.
/// </summary>
/// <remarks>
/// The client exposes separate surfaces for low-level searches, high-level
/// analytics, saved searches, and saved-search alerts. Create it directly when
/// you manage <see cref="HttpClient"/> yourself, or register it with
/// <c>Marouanvs.Splunk.DependencyInjection</c> in hosted applications.
/// </remarks>
public sealed class SplunkClient : IDisposable
{
    private readonly HttpClient? _ownedHttpClient;

    /// <summary>
    /// Initializes a client with a caller-owned <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">HTTP client used for Splunk management REST calls.</param>
    /// <param name="options">Validated Splunk SDK options.</param>
    /// <remarks>
    /// The SDK does not dispose a caller-owned <see cref="HttpClient"/>. Use
    /// this constructor when the application owns handlers, proxies, TLS policy,
    /// or host-level resilience.
    /// </remarks>
    public SplunkClient(HttpClient httpClient, SplunkClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        var restClient = new SplunkRestClient(httpClient, options);
        var endpointBuilder = new SplunkEndpointBuilder(options);
        Search = new SplunkSearchClient(restClient, endpointBuilder);
        Analytics = new SplunkAnalyticsClient(Search);
        SavedSearches = new SplunkSavedSearchClient(restClient, endpointBuilder);
        Alerts = new SplunkAlertClient(SavedSearches, restClient, endpointBuilder);
    }

    private SplunkClient(HttpClient httpClient, SplunkClientOptions options, bool ownsHttpClient)
        : this(httpClient, options)
    {
        _ownedHttpClient = ownsHttpClient ? httpClient : null;
    }

    /// <summary>
    /// Gets low-level Splunk search operations.
    /// </summary>
    public ISplunkSearchClient Search { get; }

    /// <summary>
    /// Gets high-level analytics helpers for common operational metrics.
    /// </summary>
    public ISplunkAnalyticsClient Analytics { get; }

    /// <summary>
    /// Gets saved search management operations.
    /// </summary>
    public ISplunkSavedSearchClient SavedSearches { get; }

    /// <summary>
    /// Gets alert management operations.
    /// </summary>
    public ISplunkAlertClient Alerts { get; }

    /// <summary>
    /// Creates a client with an SDK-owned <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="options">Splunk SDK options.</param>
    /// <returns>A disposable Splunk client that owns its internal HTTP client.</returns>
    /// <remarks>
    /// <para>
    /// This convenience factory is useful for console tools and small jobs. For
    /// services, prefer dependency injection or a caller-managed
    /// <see cref="HttpClient"/> so socket lifetime, handlers, and resilience are
    /// controlled by the host.
    /// </para>
    /// <para>
    /// The owned client disables automatic redirect following: Splunk management
    /// endpoints do not legitimately redirect, and following a redirect could replay
    /// the <c>Authorization</c> header (and form bodies) to an unexpected target.
    /// </para>
    /// <para>
    /// When <see cref="SplunkClientOptions.Timeout"/> is unset, the
    /// <see cref="HttpClient"/> default of 100 seconds applies; blocking search
    /// submissions (<c>exec_mode=blocking</c>) on slow searches can need a larger value.
    /// </para>
    /// </remarks>
    public static SplunkClient Create(SplunkClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false
        };

        var httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = options.NormalizedManagementUri
        };

        if (options.Timeout is { } timeout)
        {
            httpClient.Timeout = timeout;
        }

        return new SplunkClient(httpClient, options, ownsHttpClient: true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _ownedHttpClient?.Dispose();
    }
}
