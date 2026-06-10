using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Marouanvs.Splunk.Alerts;
using Marouanvs.Splunk.Analytics;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.SavedSearches;
using Marouanvs.Splunk.Search;

namespace Marouanvs.Splunk.DependencyInjection;

/// <summary>
/// Service registration helpers for Splunk SDK clients.
/// </summary>
/// <remarks>
/// <para>
/// The extensions register <see cref="SplunkClient"/> plus the search,
/// analytics, saved search, and alert interfaces. The returned
/// <see cref="IHttpClientBuilder"/> lets the host attach handlers, proxies,
/// TLS settings, and resilience policies.
/// </para>
/// <para>
/// Each logical client name, including the default, can be registered exactly
/// once. A second <c>AddSplunkClient</c> call for the same name throws
/// <see cref="InvalidOperationException"/> instead of silently rewiring the
/// earlier registration. Use the named overloads to target multiple Splunk
/// deployments from one host; named registrations are exposed as keyed
/// services with the logical name as the service key.
/// </para>
/// <para>
/// <see cref="SplunkClient"/> is registered as a transient
/// <see cref="IDisposable"/>, so instances resolved from the root provider are
/// tracked until the provider is disposed. This is benign here because
/// <see cref="SplunkClient.Dispose"/> does not dispose factory-created
/// <see cref="HttpClient"/> instances, but prefer resolving from bounded
/// scopes in long-lived services.
/// </para>
/// <para>
/// Tokens taken from configuration or environment variables are snapshotted
/// once when the options singleton is first materialized. Use the factory
/// overloads with a custom <c>ISplunkTokenProvider</c> when tokens rotate.
/// </para>
/// <para>
/// When <see cref="SplunkClientOptions.Timeout"/> is set, the registration
/// applies it to the named <see cref="HttpClient"/> it creates; hosts can
/// override it through the returned <see cref="IHttpClientBuilder"/>.
/// </para>
/// </remarks>
public static class SplunkServiceCollectionExtensions
{
    /// <summary>
    /// Named <see cref="HttpClient"/> used by the default (unnamed) SDK registration.
    /// </summary>
    public const string HttpClientName = "Marouanvs.Splunk";

    /// <summary>
    /// Gets the <see cref="HttpClient"/> name used for a logical Splunk client name.
    /// </summary>
    /// <param name="name">Logical Splunk client name; empty for the default registration.</param>
    /// <returns>
    /// <see cref="HttpClientName"/> for the default registration, otherwise
    /// <c>Marouanvs.Splunk:{name}</c>.
    /// </returns>
    public static string GetHttpClientName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.Length == 0 ? HttpClientName : $"{HttpClientName}:{name}";
    }

    /// <summary>
    /// Registers the default <see cref="SplunkClient"/> and related SDK clients from the <c>Splunk</c> configuration section.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration root.</param>
    /// <returns>The underlying HTTP client builder for handler/policy customization.</returns>
    /// <remarks>
    /// Configured tokens are snapshotted once; use the factory overload with a
    /// custom <c>ISplunkTokenProvider</c> for token rotation.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The default Splunk client is already registered.</exception>
    public static IHttpClientBuilder AddSplunkClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddSplunkClient(configuration.GetSection(SplunkClientSettings.SectionName));
    }

    /// <summary>
    /// Registers the default <see cref="SplunkClient"/> and related SDK clients from a configuration section.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configurationSection">Configuration section bound to <see cref="SplunkClientSettings"/>.</param>
    /// <returns>The underlying HTTP client builder for handler/policy customization.</returns>
    /// <remarks>
    /// <para>
    /// Configured tokens are snapshotted once; use the factory overload with a
    /// custom <c>ISplunkTokenProvider</c> for token rotation.
    /// </para>
    /// <para>
    /// When <see cref="SplunkClientSettings.AllowUntrustedCertificates"/> is
    /// enabled and the management URI is a loopback host, the registration
    /// installs a primary handler that bypasses server certificate validation.
    /// This is for disposable local labs only; non-loopback hosts fail options
    /// validation.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The default Splunk client is already registered.</exception>
    public static IHttpClientBuilder AddSplunkClient(
        this IServiceCollection services,
        IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return AddSplunkClientFromSettings(services, Options.DefaultName, configurationSection);
    }

    /// <summary>
    /// Registers a named <see cref="SplunkClient"/> and related SDK clients from a configuration section.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="name">Logical client name used as the options name and keyed-service key.</param>
    /// <param name="configurationSection">Configuration section bound to <see cref="SplunkClientSettings"/>.</param>
    /// <returns>The underlying HTTP client builder for handler/policy customization.</returns>
    /// <remarks>
    /// <para>
    /// Resolve the named client and its interfaces as keyed services, for
    /// example with <c>GetRequiredKeyedService&lt;SplunkClient&gt;(name)</c> or
    /// <c>[FromKeyedServices(name)]</c>. The registration uses the dedicated
    /// HTTP client name returned by <see cref="GetHttpClientName"/>.
    /// </para>
    /// <para>
    /// Configured tokens are snapshotted once; use the named factory overload
    /// with a custom <c>ISplunkTokenProvider</c> for token rotation. The
    /// loopback-only <see cref="SplunkClientSettings.AllowUntrustedCertificates"/>
    /// behavior matches the default-name overload.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The logical client name is already registered.</exception>
    public static IHttpClientBuilder AddSplunkClient(
        this IServiceCollection services,
        string name,
        IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return AddSplunkClientFromSettings(services, name, configurationSection);
    }

    /// <summary>
    /// Registers the default <see cref="SplunkClient"/> and related SDK clients with dependency injection.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="options">Validated Splunk SDK options.</param>
    /// <returns>The underlying HTTP client builder for handler/policy customization.</returns>
    /// <remarks>
    /// Use this overload when options are already built by the host. For
    /// per-environment or secret-store construction, use the factory overload.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The default Splunk client is already registered.</exception>
    public static IHttpClientBuilder AddSplunkClient(
        this IServiceCollection services,
        SplunkClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        return services.AddSplunkClient(_ => options);
    }

    /// <summary>
    /// Registers a named <see cref="SplunkClient"/> and related SDK clients with dependency injection.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="name">Logical client name used as the keyed-service key.</param>
    /// <param name="options">Validated Splunk SDK options.</param>
    /// <returns>The underlying HTTP client builder for handler/policy customization.</returns>
    /// <remarks>
    /// Resolve the named client and its interfaces as keyed services, for
    /// example with <c>GetRequiredKeyedService&lt;SplunkClient&gt;(name)</c> or
    /// <c>[FromKeyedServices(name)]</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The logical client name is already registered.</exception>
    public static IHttpClientBuilder AddSplunkClient(
        this IServiceCollection services,
        string name,
        SplunkClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(options);

        return services.AddSplunkClient(name, _ => options);
    }

    /// <summary>
    /// Registers the default <see cref="SplunkClient"/> and related SDK clients with dependency injection.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="optionsFactory">Factory that returns validated Splunk SDK options.</param>
    /// <returns>The underlying HTTP client builder for handler/policy customization.</returns>
    /// <remarks>
    /// Use this overload when options need services from the container, such as
    /// configuration, secret providers, or token rotation services. A custom
    /// <c>ISplunkTokenProvider</c> supplied here is the supported token
    /// rotation path; configuration-based overloads snapshot the token once.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The default Splunk client is already registered.</exception>
    public static IHttpClientBuilder AddSplunkClient(
        this IServiceCollection services,
        Func<IServiceProvider, SplunkClientOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        return AddSplunkClientCore(services, Options.DefaultName, optionsFactory);
    }

    /// <summary>
    /// Registers a named <see cref="SplunkClient"/> and related SDK clients with dependency injection.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="name">Logical client name used as the keyed-service key.</param>
    /// <param name="optionsFactory">Factory that returns validated Splunk SDK options.</param>
    /// <returns>The underlying HTTP client builder for handler/policy customization.</returns>
    /// <remarks>
    /// Resolve the named client and its interfaces as keyed services, for
    /// example with <c>GetRequiredKeyedService&lt;SplunkClient&gt;(name)</c> or
    /// <c>[FromKeyedServices(name)]</c>. A custom <c>ISplunkTokenProvider</c>
    /// supplied here is the supported token rotation path.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The logical client name is already registered.</exception>
    public static IHttpClientBuilder AddSplunkClient(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, SplunkClientOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        return AddSplunkClientCore(services, name, optionsFactory);
    }

    private static IHttpClientBuilder AddSplunkClientFromSettings(
        IServiceCollection services,
        string name,
        IConfigurationSection configurationSection)
    {
        EnsureSingleRegistration(services, name);

        services
            .AddOptions<SplunkClientSettings>(name)
            .Bind(configurationSection)
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<SplunkClientSettings>, SplunkClientSettingsValidator>());

        var httpClientBuilder = AddSplunkClientCore(services, name, provider =>
            provider.GetRequiredService<IOptionsMonitor<SplunkClientSettings>>().Get(name).ToClientOptions());

        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(provider =>
            CreatePrimaryHttpMessageHandler(provider, name));

        return httpClientBuilder;
    }

    private static IHttpClientBuilder AddSplunkClientCore(
        IServiceCollection services,
        string name,
        Func<IServiceProvider, SplunkClientOptions> optionsFactory)
    {
        EnsureSingleRegistration(services, name);
        services.AddSingleton(new SplunkClientRegistration(name));

        var httpClientName = GetHttpClientName(name);
        var httpClientBuilder = services.AddHttpClient(httpClientName);

        // SplunkClientOptions.Timeout only applies to SDK-owned HttpClient
        // instances, so the registration mirrors it onto the named client it
        // creates. Hosts can still override it through the returned builder.
        httpClientBuilder.ConfigureHttpClient((provider, httpClient) =>
        {
            var options = name.Length == 0
                ? provider.GetRequiredService<SplunkClientOptions>()
                : provider.GetRequiredKeyedService<SplunkClientOptions>(name);

            if (options.Timeout is { } timeout)
            {
                httpClient.Timeout = timeout;
            }
        });

        if (name.Length == 0)
        {
            services.AddSingleton<SplunkClientOptions>(optionsFactory);

            services.AddTransient<SplunkClient>(provider =>
            {
                var options = provider.GetRequiredService<SplunkClientOptions>();
                var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                return new SplunkClient(httpClientFactory.CreateClient(httpClientName), options);
            });

            services.AddTransient<ISplunkSearchClient>(provider => provider.GetRequiredService<SplunkClient>().Search);
            services.AddTransient<ISplunkAnalyticsClient>(provider => provider.GetRequiredService<SplunkClient>().Analytics);
            services.AddTransient<ISplunkSavedSearchClient>(provider => provider.GetRequiredService<SplunkClient>().SavedSearches);
            services.AddTransient<ISplunkAlertClient>(provider => provider.GetRequiredService<SplunkClient>().Alerts);
        }
        else
        {
            services.AddKeyedSingleton<SplunkClientOptions>(name, (provider, _) => optionsFactory(provider));

            services.AddKeyedTransient<SplunkClient>(name, (provider, key) =>
            {
                var options = provider.GetRequiredKeyedService<SplunkClientOptions>(key);
                var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                return new SplunkClient(httpClientFactory.CreateClient(httpClientName), options);
            });

            services.AddKeyedTransient<ISplunkSearchClient>(name, (provider, key) =>
                provider.GetRequiredKeyedService<SplunkClient>(key).Search);
            services.AddKeyedTransient<ISplunkAnalyticsClient>(name, (provider, key) =>
                provider.GetRequiredKeyedService<SplunkClient>(key).Analytics);
            services.AddKeyedTransient<ISplunkSavedSearchClient>(name, (provider, key) =>
                provider.GetRequiredKeyedService<SplunkClient>(key).SavedSearches);
            services.AddKeyedTransient<ISplunkAlertClient>(name, (provider, key) =>
                provider.GetRequiredKeyedService<SplunkClient>(key).Alerts);
        }

        return httpClientBuilder;
    }

    private static void EnsureSingleRegistration(IServiceCollection services, string name)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(SplunkClientRegistration)
                && !descriptor.IsKeyedService
                && descriptor.ImplementationInstance is SplunkClientRegistration registration
                && string.Equals(registration.Name, name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(name.Length == 0
                    ? "AddSplunkClient has already been called for the default Splunk client. " +
                      "Register the default client once, and use the named AddSplunkClient overloads for additional Splunk deployments."
                    : $"AddSplunkClient has already been called for the Splunk client name '{name}'. Register each logical client name once.");
            }
        }
    }

    private static HttpMessageHandler CreatePrimaryHttpMessageHandler(IServiceProvider provider, string name)
    {
        var settings = provider.GetRequiredService<IOptionsMonitor<SplunkClientSettings>>().Get(name);
        var handler = new HttpClientHandler();

        if (settings.AllowUntrustedCertificates
            && settings.ManagementUri is { } managementUri
            && managementUri.IsAbsoluteUri
            && managementUri.IsLoopback)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return handler;
    }
}
