using System.Reflection;
using SplunkSdk.Authentication;
using SplunkSdk.Models;

namespace SplunkSdk.Configuration;

/// <summary>
/// Runtime configuration for <see cref="SplunkClient"/>.
/// </summary>
/// <remarks>
/// Options are immutable after construction and validated when a client is
/// created. Keep secrets behind <see cref="ISplunkTokenProvider"/>; do not put
/// token values directly in logs or configuration files.
/// </remarks>
public sealed class SplunkClientOptions
{
    /// <summary>
    /// Gets or sets the Splunk management API URI, usually <c>https://host:8089</c>.
    /// </summary>
    /// <remarks>
    /// This is the Splunk management REST endpoint, not the Splunk Web UI port.
    /// Splunk Enterprise commonly uses port <c>8089</c>.
    /// </remarks>
    public required Uri ManagementUri { get; init; }

    /// <summary>
    /// Gets or sets the token provider used for every REST call.
    /// </summary>
    public required ISplunkTokenProvider TokenProvider { get; init; }

    /// <summary>
    /// Gets or sets the authorization header scheme. Defaults to <see cref="SplunkAuthorizationScheme.Bearer"/>.
    /// </summary>
    /// <remarks>
    /// Use <see cref="SplunkAuthorizationScheme.Bearer"/> for Splunk JWT tokens.
    /// Some app-specific endpoints can require <see cref="SplunkAuthorizationScheme.Splunk"/>.
    /// </remarks>
    public SplunkAuthorizationScheme AuthorizationScheme { get; init; } = SplunkAuthorizationScheme.Bearer;

    /// <summary>
    /// Gets or sets the search endpoint API generation. Defaults to semantic v2 endpoints.
    /// </summary>
    /// <remarks>
    /// Keep the default unless targeting an older Splunk deployment that does
    /// not support semantic v2 search endpoints.
    /// </remarks>
    public SplunkSearchApiVersion SearchApiVersion { get; init; } = SplunkSearchApiVersion.V2;

    /// <summary>
    /// Gets or sets the default Splunk namespace. When unset, the SDK calls <c>/services</c>.
    /// </summary>
    public SplunkNamespace? DefaultNamespace { get; init; }

    /// <summary>
    /// Gets retry settings for transient Splunk or network failures.
    /// </summary>
    /// <remarks>
    /// Set <see cref="SplunkRetryOptions.MaxRetries"/> to <c>0</c> when the host
    /// application owns retries through Polly, Microsoft.Extensions.Http.Resilience,
    /// a service mesh, or another platform policy.
    /// </remarks>
    public SplunkRetryOptions Retry { get; init; } = new();

    /// <summary>
    /// Gets or sets a user agent appended to SDK requests.
    /// </summary>
    public string UserAgent { get; init; } = CreateDefaultUserAgent();

    /// <summary>
    /// Creates options from a management endpoint and static token.
    /// </summary>
    /// <param name="managementUri">Splunk management REST endpoint.</param>
    /// <param name="token">Splunk authentication token.</param>
    /// <returns>Client options using <see cref="StaticSplunkTokenProvider"/>.</returns>
    /// <remarks>
    /// This is convenient for examples and local tools. Production applications
    /// should normally provide a custom <see cref="ISplunkTokenProvider"/> backed
    /// by a secret store or token rotation service.
    /// </remarks>
    public static SplunkClientOptions FromToken(Uri managementUri, string token) =>
        new()
        {
            ManagementUri = managementUri,
            TokenProvider = new StaticSplunkTokenProvider(token)
        };

    internal Uri NormalizedManagementUri
    {
        get
        {
            Validate();
            var value = ManagementUri.ToString();
            return value.EndsWith("/", StringComparison.Ordinal) ? ManagementUri : new Uri(value + "/", UriKind.Absolute);
        }
    }

    internal void Validate()
    {
        if (!ManagementUri.IsAbsoluteUri)
        {
            throw new SplunkConfigurationException("The Splunk management URI must be absolute.");
        }

        if (ManagementUri.Scheme != Uri.UriSchemeHttps && ManagementUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new SplunkConfigurationException("The Splunk management URI must use HTTP or HTTPS.");
        }

        if (TokenProvider is null)
        {
            throw new SplunkConfigurationException("A Splunk token provider is required.");
        }

        if (string.IsNullOrWhiteSpace(UserAgent))
        {
            throw new SplunkConfigurationException("The user agent must not be empty.");
        }

        Retry.Validate();
    }

    private static string CreateDefaultUserAgent()
    {
        var version = typeof(SplunkClientOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            version = typeof(SplunkClientOptions).Assembly.GetName().Version?.ToString();
        }

        version = string.IsNullOrWhiteSpace(version)
            ? "0.0.0"
            : version.Split('+', 2)[0];

        return $"SplunkSdk/{version}";
    }
}
