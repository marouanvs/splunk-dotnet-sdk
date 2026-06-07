using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SplunkSdk.Alerts;
using SplunkSdk.Analytics;
using SplunkSdk.Configuration;
using SplunkSdk.SavedSearches;
using SplunkSdk.Search;

namespace SplunkSdk.DependencyInjection;

/// <summary>
/// Service registration helpers for Splunk SDK clients.
/// </summary>
/// <remarks>
/// The extension registers <see cref="SplunkClient"/> plus the search,
/// analytics, saved search, and alert interfaces. The returned
/// <see cref="IHttpClientBuilder"/> lets the host attach handlers, proxies,
/// TLS settings, and resilience policies.
/// </remarks>
public static class SplunkServiceCollectionExtensions
{
    /// <summary>
    /// Default named <see cref="HttpClient"/> used by the SDK registration.
    /// </summary>
    public const string HttpClientName = "SplunkSdk";

    /// <summary>
    /// Registers <see cref="SplunkClient"/> and related SDK clients from the default <c>Splunk</c> configuration section.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration root.</param>
    /// <returns>The underlying HTTP client builder for handler/policy customization.</returns>
    public static IHttpClientBuilder AddSplunkClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddSplunkClient(configuration.GetSection(SplunkClientSettings.SectionName));
    }

    /// <summary>
    /// Registers <see cref="SplunkClient"/> and related SDK clients from a configuration section.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configurationSection">Configuration section bound to <see cref="SplunkClientSettings"/>.</param>
    /// <returns>The underlying HTTP client builder for handler/policy customization.</returns>
    public static IHttpClientBuilder AddSplunkClient(
        this IServiceCollection services,
        IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        services
            .AddOptions<SplunkClientSettings>()
            .Bind(configurationSection)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<SplunkClientSettings>, SplunkClientSettingsValidator>();

        var httpClientBuilder = services.AddSplunkClient(provider =>
            provider.GetRequiredService<IOptions<SplunkClientSettings>>().Value.ToClientOptions());

        if (AllowsUntrustedCertificates(configurationSection))
        {
            httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        }

        return httpClientBuilder;
    }

    /// <summary>
    /// Registers <see cref="SplunkClient"/> and related SDK clients with dependency injection.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="options">Validated Splunk SDK options.</param>
    /// <returns>The underlying HTTP client builder for handler/policy customization.</returns>
    /// <remarks>
    /// Use this overload when options are already built by the host. For
    /// per-environment or secret-store construction, use the factory overload.
    /// </remarks>
    public static IHttpClientBuilder AddSplunkClient(
        this IServiceCollection services,
        SplunkClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        return services.AddSplunkClient(_ => options);
    }

    /// <summary>
    /// Registers <see cref="SplunkClient"/> and related SDK clients with dependency injection.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="optionsFactory">Factory that returns validated Splunk SDK options.</param>
    /// <returns>The underlying HTTP client builder for handler/policy customization.</returns>
    /// <remarks>
    /// Use this overload when options need services from the container, such as
    /// configuration, secret providers, or token rotation services.
    /// </remarks>
    public static IHttpClientBuilder AddSplunkClient(
        this IServiceCollection services,
        Func<IServiceProvider, SplunkClientOptions> optionsFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(optionsFactory);

        services.AddSingleton(optionsFactory);
        var httpClientBuilder = services.AddHttpClient(HttpClientName);

        services.AddTransient(provider =>
        {
            var options = provider.GetRequiredService<SplunkClientOptions>();
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            return new SplunkClient(httpClientFactory.CreateClient(HttpClientName), options);
        });

        services.AddTransient(provider => provider.GetRequiredService<SplunkClient>().Search);
        services.AddTransient(provider => provider.GetRequiredService<SplunkClient>().Analytics);
        services.AddTransient(provider => provider.GetRequiredService<SplunkClient>().SavedSearches);
        services.AddTransient(provider => provider.GetRequiredService<SplunkClient>().Alerts);

        services.AddTransient<ISplunkSearchClient>(provider => provider.GetRequiredService<SplunkClient>().Search);
        services.AddTransient<ISplunkAnalyticsClient>(provider => provider.GetRequiredService<SplunkClient>().Analytics);
        services.AddTransient<ISplunkSavedSearchClient>(provider => provider.GetRequiredService<SplunkClient>().SavedSearches);
        services.AddTransient<ISplunkAlertClient>(provider => provider.GetRequiredService<SplunkClient>().Alerts);

        return httpClientBuilder;
    }

    private static bool AllowsUntrustedCertificates(IConfiguration configuration) =>
        bool.TryParse(
            configuration[nameof(SplunkClientSettings.AllowUntrustedCertificates)],
            out var allowUntrustedCertificates)
        && allowUntrustedCertificates;
}
