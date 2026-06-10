using Marouanvs.Splunk.Authentication;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.Models;

namespace Marouanvs.Splunk.DependencyInjection;

/// <summary>
/// Bindable host-application settings for registering <see cref="SplunkClient"/> from configuration.
/// </summary>
/// <remarks>
/// <para>
/// These settings are intended for ASP.NET Core and generic-host applications
/// that use the Microsoft options pattern.
/// </para>
/// <para>
/// Token values resolved from <see cref="Token"/> or
/// <see cref="TokenEnvironmentVariable"/> are snapshotted once, when the client
/// options are first materialized. Configuration reloads and later environment
/// variable changes are not observed. Production services that rotate tokens
/// should use the <c>AddSplunkClient</c> factory overload with a custom
/// <see cref="ISplunkTokenProvider"/> backed by a secret store or rotation
/// service.
/// </para>
/// </remarks>
public sealed class SplunkClientSettings
{
    /// <summary>
    /// Default configuration section name.
    /// </summary>
    public const string SectionName = "Splunk";

    /// <summary>
    /// Core default user agent, read once from a probe options instance so this
    /// mapping never duplicates the core default computation.
    /// </summary>
    private static readonly string DefaultUserAgent = new SplunkClientOptions
    {
        ManagementUri = new Uri("https://localhost:8089", UriKind.Absolute),
        TokenProvider = UserAgentProbeTokenProvider.Instance
    }.UserAgent;

    /// <summary>
    /// Gets or sets the Splunk management REST endpoint, usually <c>https://host:8089</c>.
    /// </summary>
    public Uri? ManagementUri { get; set; }

    /// <summary>
    /// Gets or sets a static token value for local development or user secrets.
    /// </summary>
    /// <remarks>
    /// Do not commit real token values to source control. The value is
    /// snapshotted once when the client options are first materialized and is
    /// not refreshed by configuration reloads. Production services should
    /// normally use the factory overload with a custom
    /// <see cref="ISplunkTokenProvider"/>, which is the supported token
    /// rotation path.
    /// </remarks>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the environment variable name that contains the Splunk token.
    /// </summary>
    /// <remarks>
    /// The environment variable is read once, when the client options are first
    /// materialized; later changes to the variable are not observed. Use the
    /// factory overload with a custom <see cref="ISplunkTokenProvider"/> when
    /// tokens rotate at runtime.
    /// </remarks>
    public string? TokenEnvironmentVariable { get; set; }

    /// <summary>
    /// Gets or sets the authorization header scheme. Defaults to <see cref="SplunkAuthorizationScheme.Bearer"/>.
    /// </summary>
    public SplunkAuthorizationScheme AuthorizationScheme { get; set; } = SplunkAuthorizationScheme.Bearer;

    /// <summary>
    /// Gets or sets the search REST API generation. Defaults to semantic v2 endpoints.
    /// </summary>
    public SplunkSearchApiVersion SearchApiVersion { get; set; } = SplunkSearchApiVersion.V2;

    /// <summary>
    /// Gets or sets the default Splunk REST namespace.
    /// </summary>
    public SplunkNamespaceSettings? DefaultNamespace { get; set; }

    /// <summary>
    /// Gets retry settings for SDK-owned retries.
    /// </summary>
    public SplunkRetrySettings Retry { get; set; } = new();

    /// <summary>
    /// Gets or sets a request user-agent override.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Gets or sets whether a plain <c>http://</c> management URI is allowed. Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Maps to <see cref="SplunkClientOptions.AllowInsecureHttp"/>. Leave this
    /// disabled except for disposable local labs: tokens sent over plain HTTP
    /// can be captured on the network.
    /// </remarks>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>
    /// Gets or sets an optional request timeout. When unset, the
    /// <see cref="HttpClient"/> default of 100 seconds applies.
    /// </summary>
    /// <remarks>
    /// Maps to <see cref="SplunkClientOptions.Timeout"/>, and the DI
    /// registration also applies the value to the named
    /// <see cref="HttpClient"/> it creates. Blocking search submissions
    /// (<c>exec_mode=blocking</c>) hold the HTTP request open until the search
    /// finishes, so long searches can require a larger value.
    /// </remarks>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets whether the DI registration may bypass server certificate
    /// validation for loopback labs. Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// This flag is honored only when <see cref="ManagementUri"/> points at a
    /// loopback host (<c>localhost</c>, <c>127.0.0.1</c>, or <c>::1</c>); any
    /// other host fails options validation. It exists solely for disposable
    /// local labs with self-signed certificates. Never enable it for Splunk
    /// Cloud or production Splunk Enterprise deployments; production hosts
    /// that need a custom TLS policy should construct
    /// <see cref="SplunkClient"/> with a caller-owned <see cref="HttpClient"/>.
    /// </remarks>
    public bool AllowUntrustedCertificates { get; set; }

    internal SplunkClientOptions ToClientOptions()
    {
        var managementUri = ManagementUri
            ?? throw new SplunkConfigurationException("Splunk:ManagementUri is required.");

        if (!managementUri.IsAbsoluteUri)
        {
            throw new SplunkConfigurationException("Splunk:ManagementUri must be absolute.");
        }

        if (managementUri.Scheme != Uri.UriSchemeHttps && managementUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new SplunkConfigurationException("Splunk:ManagementUri must use HTTP or HTTPS.");
        }

        if (AllowUntrustedCertificates && !managementUri.IsLoopback)
        {
            throw new SplunkConfigurationException(
                "Splunk:AllowUntrustedCertificates is honored only for loopback management URIs " +
                "(localhost, 127.0.0.1, or ::1) in disposable local labs. Keep certificate validation " +
                "enabled for every other host; production deployments that need a custom TLS policy " +
                "should construct SplunkClient with a caller-owned HttpClient.");
        }

        var clientOptions = new SplunkClientOptions
        {
            ManagementUri = managementUri,
            TokenProvider = CreateTokenProvider(),
            AuthorizationScheme = AuthorizationScheme,
            SearchApiVersion = SearchApiVersion,
            DefaultNamespace = DefaultNamespace?.ToSplunkNamespace(),
            Retry = (Retry ?? new SplunkRetrySettings()).ToRetryOptions(),
            AllowInsecureHttp = AllowInsecureHttp,
            Timeout = Timeout,
            UserAgent = Normalize(UserAgent) ?? DefaultUserAgent
        };

        clientOptions.Validate();
        return clientOptions;
    }

    private ISplunkTokenProvider CreateTokenProvider()
    {
        var token = Normalize(Token);
        var environmentVariableName = Normalize(TokenEnvironmentVariable);

        if (token is not null && environmentVariableName is not null)
        {
            throw new SplunkConfigurationException(
                "Configure either Splunk:Token or Splunk:TokenEnvironmentVariable, not both.");
        }

        if (token is not null)
        {
            return new StaticSplunkTokenProvider(token);
        }

        if (environmentVariableName is not null)
        {
            var environmentToken = Normalize(Environment.GetEnvironmentVariable(environmentVariableName));
            if (environmentToken is null)
            {
                throw new SplunkConfigurationException(
                    $"The environment variable '{environmentVariableName}' configured by Splunk:TokenEnvironmentVariable is missing or empty.");
            }

            return new StaticSplunkTokenProvider(environmentToken);
        }

        throw new SplunkConfigurationException(
            "Configure Splunk:Token, Splunk:TokenEnvironmentVariable, or use the factory overload with a custom ISplunkTokenProvider.");
    }

    private static string? Normalize(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Token provider used only to construct the user-agent probe options.
    /// It never supplies a token and is never attached to a real client.
    /// </summary>
    private sealed class UserAgentProbeTokenProvider : ISplunkTokenProvider
    {
        public static readonly UserAgentProbeTokenProvider Instance = new();

        private UserAgentProbeTokenProvider()
        {
        }

        public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The user-agent probe token provider never supplies tokens.");
    }
}

/// <summary>
/// Bindable Splunk namespace settings.
/// </summary>
public sealed class SplunkNamespaceSettings
{
    /// <summary>
    /// Gets or sets the Splunk knowledge-object owner segment.
    /// </summary>
    public string? Owner { get; set; }

    /// <summary>
    /// Gets or sets the Splunk app segment.
    /// </summary>
    public string? App { get; set; }

    internal SplunkNamespace? ToSplunkNamespace()
    {
        var owner = Normalize(Owner);
        var app = Normalize(App);

        if (owner is null && app is null)
        {
            return null;
        }

        if (owner is null || app is null)
        {
            throw new SplunkConfigurationException(
                "Both Splunk:DefaultNamespace:Owner and Splunk:DefaultNamespace:App are required when a default namespace is configured.");
        }

        try
        {
            return SplunkNamespace.Create(owner, app);
        }
        catch (ArgumentException exception)
        {
            throw new SplunkConfigurationException("Splunk:DefaultNamespace is invalid.", exception);
        }
    }

    private static string? Normalize(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

/// <summary>
/// Bindable retry settings for SDK-owned retries.
/// </summary>
/// <remarks>
/// Unset values fall back to the core <see cref="SplunkRetryOptions"/>
/// defaults. Validation is owned by the core options validation, which also
/// enforces that <see cref="MaxDelay"/> is not smaller than
/// <see cref="BaseDelay"/>.
/// </remarks>
public sealed class SplunkRetrySettings
{
    /// <summary>
    /// Gets or sets the number of retry attempts after the first failed request.
    /// </summary>
    /// <remarks>
    /// Set <c>0</c> when the host owns retries through Polly,
    /// Microsoft.Extensions.Http.Resilience, or another retry layer.
    /// </remarks>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Gets or sets the first retry backoff delay.
    /// </summary>
    public TimeSpan? BaseDelay { get; set; }

    /// <summary>
    /// Gets or sets the maximum retry backoff delay.
    /// </summary>
    public TimeSpan? MaxDelay { get; set; }

    /// <summary>
    /// Gets or sets the maximum server-requested (<c>Retry-After</c>) delay the SDK honors.
    /// </summary>
    public TimeSpan? MaxServerDelay { get; set; }

    internal SplunkRetryOptions ToRetryOptions()
    {
        var defaults = new SplunkRetryOptions();

        var options = new SplunkRetryOptions
        {
            MaxRetries = MaxRetries ?? defaults.MaxRetries,
            BaseDelay = BaseDelay ?? defaults.BaseDelay,
            MaxDelay = MaxDelay ?? defaults.MaxDelay,
            MaxServerDelay = MaxServerDelay ?? defaults.MaxServerDelay
        };

        options.Validate();
        return options;
    }
}
