using System.Net.Http.Headers;
using System.Reflection;
using Marouanvs.Splunk.Authentication;
using Marouanvs.Splunk.Models;

namespace Marouanvs.Splunk.Configuration;

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
    /// Splunk Enterprise commonly uses port <c>8089</c>. Plain <c>http://</c>
    /// URIs are rejected unless <see cref="AllowInsecureHttp"/> is enabled.
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
    /// <remarks>
    /// The value must be a valid HTTP <c>User-Agent</c> header value, for example
    /// <c>MyApp/1.0</c>. Invalid values fail validation with a
    /// <see cref="SplunkConfigurationException"/> when the client is created.
    /// </remarks>
    public string UserAgent { get; init; } = CreateDefaultUserAgent();

    /// <summary>
    /// Gets or sets a value indicating whether plain <c>http://</c> management URIs are allowed.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// This switch is for local labs only. Splunk tokens are sent in the
    /// <c>Authorization</c> header on every request, so a plain HTTP management URI
    /// exposes the credential in cleartext on the network. Keep this disabled and use
    /// <c>https://</c> for any shared, production, or Splunk Cloud deployment.
    /// </remarks>
    public bool AllowInsecureHttp { get; init; }

    /// <summary>
    /// Gets or sets the request timeout applied to the SDK-owned <see cref="HttpClient"/>
    /// created by <see cref="SplunkClient.Create"/>. When unset, the
    /// <see cref="HttpClient"/> default of 100 seconds applies.
    /// </summary>
    /// <remarks>
    /// Blocking search submissions (<c>exec_mode=blocking</c>) hold the HTTP request open
    /// until the search finishes, so slow searches can exceed the 100-second default;
    /// raise this value for long-running blocking searches. The value must be greater
    /// than zero. This option is ignored for caller-owned <see cref="HttpClient"/>
    /// instances passed to the <see cref="SplunkClient(HttpClient, SplunkClientOptions)"/>
    /// constructor — configure <see cref="HttpClient.Timeout"/> directly in that case.
    /// </remarks>
    public TimeSpan? Timeout { get; init; }

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

    /// <summary>
    /// Validates the configured options and throws when a value is missing or invalid.
    /// </summary>
    /// <exception cref="SplunkConfigurationException">
    /// A required value is missing or a configured value is invalid, for example a plain
    /// HTTP management URI without <see cref="AllowInsecureHttp"/>, an unparseable
    /// <see cref="UserAgent"/>, a non-positive <see cref="Timeout"/>, or invalid
    /// <see cref="Retry"/> settings.
    /// </exception>
    /// <remarks>
    /// <see cref="SplunkClient.Create"/> calls this automatically. Hosting integrations,
    /// such as options validators, can call it directly to fail fast at startup.
    /// </remarks>
    public void Validate()
    {
        if (ManagementUri is null)
        {
            throw new SplunkConfigurationException("A Splunk management URI is required.");
        }

        if (!ManagementUri.IsAbsoluteUri)
        {
            throw new SplunkConfigurationException("The Splunk management URI must be absolute.");
        }

        if (ManagementUri.Scheme != Uri.UriSchemeHttps && ManagementUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new SplunkConfigurationException("The Splunk management URI must use HTTP or HTTPS.");
        }

        if (ManagementUri.Scheme == Uri.UriSchemeHttp && !AllowInsecureHttp)
        {
            throw new SplunkConfigurationException(
                "The Splunk management URI uses plain HTTP, which sends the Splunk token unencrypted. Use an https:// management URI, or set AllowInsecureHttp to true for local lab use only.");
        }

        if (TokenProvider is null)
        {
            throw new SplunkConfigurationException("A Splunk token provider is required.");
        }

        if (string.IsNullOrWhiteSpace(UserAgent))
        {
            throw new SplunkConfigurationException("The user agent must not be empty.");
        }

        try
        {
            _ = ParseUserAgentValues(UserAgent);
        }
        catch (FormatException exception)
        {
            throw new SplunkConfigurationException(
                "The user agent must be a valid HTTP User-Agent header value, for example \"MyApp/1.0\".",
                exception);
        }

        if (Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new SplunkConfigurationException("Timeout must be greater than zero when set.");
        }

        Retry.Validate();
    }

    internal static ProductInfoHeaderValue[] ParseUserAgentValues(string userAgent)
    {
        using var probe = new HttpRequestMessage();
        probe.Headers.UserAgent.ParseAdd(userAgent);
        return probe.Headers.UserAgent.ToArray();
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

        return $"Marouanvs.Splunk/{version}";
    }
}
