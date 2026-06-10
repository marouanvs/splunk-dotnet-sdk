using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Marouanvs.Splunk.Analytics;
using Marouanvs.Splunk.Authentication;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.DependencyInjection;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Search;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task DependencyInjectionRegistersSplunkClients()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"12"}}
        """);

        var services = new ServiceCollection();
        services
            .AddSplunkClient(new SplunkClientOptions
            {
                ManagementUri = new Uri("https://splunk.example.com:8089"),
                TokenProvider = new StaticSplunkTokenProvider("di-token"),
                Retry = new SplunkRetryOptions { MaxRetries = 0 }
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<SplunkClient>();
        var searchClient = provider.GetRequiredService<ISplunkSearchClient>();

        var rows = await client.Search.ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count")).ToListAsync();

        Assert.Equal(12, rows[0].GetInt64("error_count"));
        Assert.True(searchClient is not null, "Expected ISplunkSearchClient to be registered.");
        Assert.Equal("Bearer", handler.Requests[0].Authorization?.Scheme);
        Assert.Equal("di-token", handler.Requests[0].Authorization?.Parameter);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task DependencyInjectionBindsSplunkClientFromConfigurationSection()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"17"}}
        """);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Splunk:ManagementUri"] = "https://splunk.example.com:8089",
                ["Splunk:Token"] = " config-token ",
                ["Splunk:AuthorizationScheme"] = "Splunk",
                ["Splunk:SearchApiVersion"] = "V1",
                ["Splunk:DefaultNamespace:Owner"] = "nobody",
                ["Splunk:DefaultNamespace:App"] = "search",
                ["Splunk:Retry:MaxRetries"] = "0",
                ["Splunk:UserAgent"] = "ConfigHost/2.0"
            })
            .Build();

        var services = new ServiceCollection();
        services
            .AddSplunkClient(configuration.GetSection("Splunk"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();

        var searchClient = provider.GetRequiredService<ISplunkSearchClient>();
        var rows = await searchClient
            .ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count"))
            .ToListAsync();

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(17, rows[0].GetInt64("error_count"));
        Assert.Equal(
            "https://splunk.example.com:8089/servicesNS/nobody/search/search/jobs/export",
            sent.Uri.ToString());
        Assert.Equal("Splunk", sent.Authorization?.Scheme);
        Assert.Equal("config-token", sent.Authorization?.Parameter);
        Assert.Equal("ConfigHost/2.0", sent.UserAgent);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task DependencyInjectionBindsSplunkClientFromDefaultConfigurationSection()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"8"}}
        """);

        var environmentVariableName = $"MAROUANVS_SPLUNK_TEST_TOKEN_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(environmentVariableName, "env-token");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Splunk:ManagementUri"] = "https://splunk.example.com:8089",
                    ["Splunk:TokenEnvironmentVariable"] = environmentVariableName,
                    ["Splunk:Retry:MaxRetries"] = "0"
                })
                .Build();

            var services = new ServiceCollection();
            services
                .AddSplunkClient(configuration)
                .ConfigurePrimaryHttpMessageHandler(() => handler);

            using var provider = services.BuildServiceProvider();

            var analytics = provider.GetRequiredService<ISplunkAnalyticsClient>();
            var errors = await analytics.CountErrorsAsync(new ErrorCountQuery("team"));

            Assert.Equal(8, errors);
            Assert.Equal("Bearer", handler.Requests[0].Authorization?.Scheme);
            Assert.Equal("env-token", handler.Requests[0].Authorization?.Parameter);
            Assert.Equal(
                "https://splunk.example.com:8089/services/search/v2/jobs/export",
                handler.Requests[0].Uri.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariableName, null);
        }

        handler.AssertNoPendingResponses();
    }

    [Fact]
    public void DependencyInjectionRejectsAmbiguousConfiguredTokens()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Splunk:ManagementUri"] = "https://splunk.example.com:8089",
                ["Splunk:Token"] = "inline-token",
                ["Splunk:TokenEnvironmentVariable"] = "SPLUNK_TOKEN"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSplunkClient(configuration);

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<SplunkClient>());
        Assert.Contains("Configure either Splunk:Token or Splunk:TokenEnvironmentVariable", exception.Message);
    }

    [Fact]
    public async Task DependencyInjectionRegistersNamedSplunkClientsAsKeyedServices()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"error_count":"21"}}
        """);

        var services = new ServiceCollection();
        services
            .AddSplunkClient("ops", new SplunkClientOptions
            {
                ManagementUri = new Uri("https://splunk.example.com:8089"),
                TokenProvider = new StaticSplunkTokenProvider("ops-token"),
                Retry = new SplunkRetryOptions { MaxRetries = 0 }
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        Assert.Equal("Marouanvs.Splunk:ops", SplunkServiceCollectionExtensions.GetHttpClientName("ops"));

        using var provider = services.BuildServiceProvider();

        // Named registrations are keyed-only; the default (non-keyed) client
        // surface must stay unregistered.
        Assert.Null(provider.GetService<SplunkClient>());

        var searchClient = provider.GetRequiredKeyedService<ISplunkSearchClient>("ops");
        var rows = await searchClient
            .ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count"))
            .ToListAsync();

        Assert.Equal(21, rows[0].GetInt64("error_count"));
        Assert.Equal("ops-token", Assert.Single(handler.Requests).Authorization?.Parameter);
        Assert.NotNull(provider.GetKeyedService<SplunkClient>("ops"));
        Assert.NotNull(provider.GetKeyedService<ISplunkAnalyticsClient>("ops"));
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public void DependencyInjectionFailsLoudlyOnDuplicateLogicalClientNames()
    {
        var options = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("dup-token")
        };

        var services = new ServiceCollection();
        services.AddSplunkClient(options);
        Assert.Throws<InvalidOperationException>(() => services.AddSplunkClient(options));

        var namedServices = new ServiceCollection();
        namedServices.AddSplunkClient("ops", options);
        Assert.Throws<InvalidOperationException>(() => namedServices.AddSplunkClient("ops", options));
    }

    [Fact]
    public void DependencyInjectionRejectsUntrustedCertificateBypassForNonLoopbackHosts()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Splunk:ManagementUri"] = "https://splunk.example.com:8089",
                ["Splunk:Token"] = "lab-token",
                ["Splunk:AllowUntrustedCertificates"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSplunkClient(configuration);

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<SplunkClient>());
        Assert.Contains(
            "Splunk:AllowUntrustedCertificates is honored only for loopback management URIs",
            exception.Message,
            StringComparison.Ordinal);
    }
}
