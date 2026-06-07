using SplunkSdk.Analytics;
using SplunkSdk.Alerts;
using SplunkSdk.Configuration;
using SplunkSdk.SavedSearches;
using SplunkSdk.Search;

namespace SplunkSdk;

/// <summary>
/// Entry point for querying Splunk Enterprise or Splunk Cloud REST endpoints.
/// </summary>
/// <remarks>
/// The client exposes separate surfaces for low-level searches, high-level
/// analytics, saved searches, and saved-search alerts. Create it directly when
/// you manage <see cref="HttpClient"/> yourself, or register it with
/// <c>SplunkSdk.DependencyInjection</c> in hosted applications.
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
    /// This convenience factory is useful for console tools and small jobs. For
    /// services, prefer dependency injection or a caller-managed
    /// <see cref="HttpClient"/> so socket lifetime, handlers, and resilience are
    /// controlled by the host.
    /// </remarks>
    public static SplunkClient Create(SplunkClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var httpClient = new HttpClient
        {
            BaseAddress = options.NormalizedManagementUri
        };

        return new SplunkClient(httpClient, options, ownsHttpClient: true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _ownedHttpClient?.Dispose();
    }
}
