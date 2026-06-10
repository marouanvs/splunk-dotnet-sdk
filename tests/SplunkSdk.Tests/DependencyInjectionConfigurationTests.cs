using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Marouanvs.Splunk.Authentication;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.DependencyInjection;
using Marouanvs.Splunk.SavedSearches;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class DependencyInjectionConfigurationTests
{
    [Fact]
    public void TimeoutSettingFlowsToTheNamedHttpClient()
    {
        var services = new ServiceCollection();
        services.AddSplunkClient(new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("default-token"),
            Timeout = TimeSpan.FromSeconds(90)
        });
        services.AddSplunkClient("ops", new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("ops-token"),
            Timeout = TimeSpan.FromSeconds(45)
        });
        services.AddSplunkClient("metrics", new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("metrics-token")
        });

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var defaultHttpClient = factory.CreateClient(SplunkServiceCollectionExtensions.HttpClientName);
        using var opsHttpClient = factory.CreateClient(SplunkServiceCollectionExtensions.GetHttpClientName("ops"));
        using var metricsHttpClient = factory.CreateClient(SplunkServiceCollectionExtensions.GetHttpClientName("metrics"));

        Assert.Equal(TimeSpan.FromSeconds(90), defaultHttpClient.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(45), opsHttpClient.Timeout);

        // When Timeout is unset, the HttpClient default of 100 seconds applies.
        Assert.Equal(TimeSpan.FromSeconds(100), metricsHttpClient.Timeout);
    }

    [Fact]
    public void RetryMaxDelaySmallerThanBaseDelayFailsStartupValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Splunk:ManagementUri"] = "https://splunk.example.com:8089",
                ["Splunk:Token"] = "retry-token",
                ["Splunk:Retry:BaseDelay"] = "00:00:02",
                ["Splunk:Retry:MaxDelay"] = "00:00:01"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSplunkClient(configuration.GetSection("Splunk"));

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains(
            "MaxDelay must be greater than or equal to BaseDelay.",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowInsecureHttpFromConfigurationPermitsHttpManagementUrisEndToEnd()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Splunk:ManagementUri"] = "http://splunk.lab.example:8089",
                ["Splunk:Token"] = "lab-token",
                ["Splunk:AllowInsecureHttp"] = "true",
                ["Splunk:Retry:MaxRetries"] = "0"
            })
            .Build();

        var services = new ServiceCollection();
        services
            .AddSplunkClient(configuration.GetSection("Splunk"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();

        var savedSearches = provider.GetRequiredService<ISplunkSavedSearchClient>();
        var searches = await savedSearches.ListAsync();

        Assert.Single(searches);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(
            "http://splunk.lab.example:8089/services/saved/searches?output_mode=json",
            sent.Uri.AbsoluteUri);
        Assert.Equal("Bearer", sent.Authorization?.Scheme);
        Assert.Equal("lab-token", sent.Authorization?.Parameter);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public void LoopbackHostsWithAllowUntrustedCertificatesPassValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Splunk:ManagementUri"] = "https://127.0.0.1:8089",
                ["Splunk:Token"] = "lab-token",
                ["Splunk:AllowUntrustedCertificates"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSplunkClient(configuration.GetSection("Splunk"));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.NotNull(provider.GetRequiredService<SplunkClient>());
    }

    [Fact]
    public void SettingsValidatorIsRegisteredOnceAcrossMultipleRegistrations()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Default:ManagementUri"] = "https://splunk.example.com:8089",
                ["Default:Token"] = "default-token",
                ["Ops:ManagementUri"] = "https://splunk-ops.example.com:8089",
                ["Ops:Token"] = "ops-token"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSplunkClient(configuration.GetSection("Default"));
        services.AddSplunkClient("ops", configuration.GetSection("Ops"));

        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IValidateOptions<SplunkClientSettings>));

        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IValidateOptions<SplunkClientSettings>>());

        // The single validator still validates both registered names at startup.
        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void DuplicateLogicalClientNamesFailLoudlyAcrossRegistrationOverloads()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Splunk:ManagementUri"] = "https://splunk.example.com:8089",
                ["Splunk:Token"] = "dup-token"
            })
            .Build();
        var options = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("dup-token")
        };

        var services = new ServiceCollection();
        services.AddSplunkClient(configuration.GetSection("Splunk"));
        var defaultException = Assert.Throws<InvalidOperationException>(() =>
            services.AddSplunkClient(options));
        Assert.Contains(
            "already been called for the default Splunk client",
            defaultException.Message,
            StringComparison.Ordinal);

        var namedServices = new ServiceCollection();
        namedServices.AddSplunkClient("ops", options);
        var namedException = Assert.Throws<InvalidOperationException>(() =>
            namedServices.AddSplunkClient("ops", configuration.GetSection("Splunk")));
        Assert.Contains("'ops'", namedException.Message, StringComparison.Ordinal);
    }
}
