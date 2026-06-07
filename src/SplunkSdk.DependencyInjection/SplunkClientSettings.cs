using SplunkSdk.Authentication;
using SplunkSdk.Configuration;
using SplunkSdk.Models;

namespace SplunkSdk.DependencyInjection;

/// <summary>
/// Bindable host-application settings for registering <see cref="SplunkClient"/> from configuration.
/// </summary>
/// <remarks>
/// These settings are intended for ASP.NET Core and generic-host applications
/// that use the Microsoft options pattern. Production applications can still
/// use the factory overload when tokens come from a custom
/// <see cref="ISplunkTokenProvider"/>.
/// </remarks>
public sealed class SplunkClientSettings
{
    /// <summary>
    /// Default configuration section name.
    /// </summary>
    public const string SectionName = "Splunk";

    /// <summary>
    /// Gets or sets the Splunk management REST endpoint, usually <c>https://host:8089</c>.
    /// </summary>
    public Uri? ManagementUri { get; set; }

    /// <summary>
    /// Gets or sets a static token value for local development or user secrets.
    /// </summary>
    /// <remarks>
    /// Do not commit real token values to source control. Production services
    /// should normally use the factory overload with a custom
    /// <see cref="ISplunkTokenProvider"/>.
    /// </remarks>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the environment variable name that contains the Splunk token.
    /// </summary>
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
    /// Gets or sets whether the DI registration should bypass server certificate validation.
    /// </summary>
    /// <remarks>
    /// This is intended only for disposable local labs with self-signed
    /// certificates. Do not enable it for Splunk Cloud or production Splunk
    /// Enterprise deployments.
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

        var tokenProvider = CreateTokenProvider();
        var defaultNamespace = DefaultNamespace?.ToSplunkNamespace();
        var retry = (Retry ?? new SplunkRetrySettings()).ToRetryOptions();

        if (string.IsNullOrWhiteSpace(UserAgent))
        {
            return new SplunkClientOptions
            {
                ManagementUri = managementUri,
                TokenProvider = tokenProvider,
                AuthorizationScheme = AuthorizationScheme,
                SearchApiVersion = SearchApiVersion,
                DefaultNamespace = defaultNamespace,
                Retry = retry
            };
        }

        return new SplunkClientOptions
        {
            ManagementUri = managementUri,
            TokenProvider = tokenProvider,
            AuthorizationScheme = AuthorizationScheme,
            SearchApiVersion = SearchApiVersion,
            DefaultNamespace = defaultNamespace,
            Retry = retry,
            UserAgent = UserAgent.Trim()
        };
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
public sealed class SplunkRetrySettings
{
    /// <summary>
    /// Gets or sets the number of retry attempts after the first failed request.
    /// </summary>
    public int? MaxRetries { get; set; }

    /// <summary>
    /// Gets or sets the first retry backoff delay.
    /// </summary>
    public TimeSpan? BaseDelay { get; set; }

    /// <summary>
    /// Gets or sets the maximum retry backoff delay.
    /// </summary>
    public TimeSpan? MaxDelay { get; set; }

    internal SplunkRetryOptions ToRetryOptions()
    {
        var options = new SplunkRetryOptions
        {
            MaxRetries = MaxRetries ?? 2,
            BaseDelay = BaseDelay ?? TimeSpan.FromMilliseconds(200),
            MaxDelay = MaxDelay ?? TimeSpan.FromSeconds(2)
        };

        Validate(options);
        return options;
    }

    private static void Validate(SplunkRetryOptions options)
    {
        if (options.MaxRetries < 0)
        {
            throw new SplunkConfigurationException("Splunk:Retry:MaxRetries must be zero or greater.");
        }

        if (options.BaseDelay < TimeSpan.Zero)
        {
            throw new SplunkConfigurationException("Splunk:Retry:BaseDelay must be zero or greater.");
        }

        if (options.MaxDelay < TimeSpan.Zero)
        {
            throw new SplunkConfigurationException("Splunk:Retry:MaxDelay must be zero or greater.");
        }

        if (options.MaxRetries > 0 && options.BaseDelay <= TimeSpan.Zero)
        {
            throw new SplunkConfigurationException("Splunk:Retry:BaseDelay must be greater than zero when retries are enabled.");
        }

        if (options.MaxRetries > 0 && options.MaxDelay <= TimeSpan.Zero)
        {
            throw new SplunkConfigurationException("Splunk:Retry:MaxDelay must be greater than zero when retries are enabled.");
        }
    }
}
