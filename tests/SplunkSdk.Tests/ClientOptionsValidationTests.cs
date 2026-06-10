using System.Net;
using Marouanvs.Splunk.Authentication;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;

namespace Marouanvs.Splunk.Tests;

public sealed class ClientOptionsValidationTests
{
    [Fact]
    public async Task FromTokenProducesAValidatedBearerClientWithSecureDefaults()
    {
        var options = SplunkClientOptions.FromToken(new Uri("https://splunk.example.com:8089"), "from-token-secret");

        options.Validate();

        Assert.IsType<StaticSplunkTokenProvider>(options.TokenProvider);
        Assert.Equal(new Uri("https://splunk.example.com:8089"), options.ManagementUri);
        Assert.Equal(SplunkAuthorizationScheme.Bearer, options.AuthorizationScheme);
        Assert.Equal(SplunkSearchApiVersion.V2, options.SearchApiVersion);
        Assert.False(options.AllowInsecureHttp);
        Assert.Null(options.Timeout);
        Assert.Null(options.DefaultNamespace);
        Assert.Equal(2, options.Retry.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.Retry.BaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(2), options.Retry.MaxDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Retry.MaxServerDelay);

        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"results":[{"error_count":"1"}]}""");

        using var client = new SplunkClient(new HttpClient(handler), options);
        var rows = await client.Search.GetResultsAsync("1700000013.1", new SplunkResultRequest { Count = 1 });

        Assert.Equal(1, rows[0].GetInt64("error_count"));
        var sent = Assert.Single(handler.Requests);
        var authorization = sent.Authorization;
        Assert.NotNull(authorization);
        Assert.Equal("Bearer", authorization.Scheme);
        Assert.Equal("from-token-secret", authorization.Parameter);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public void MissingManagementUriIsRejected()
    {
        var options = SplunkClientOptions.FromToken(null!, "test-token");

        var exception = Assert.Throws<SplunkConfigurationException>(() => options.Validate());

        Assert.Equal("A Splunk management URI is required.", exception.Message);
    }

    [Fact]
    public void RelativeManagementUriIsRejected()
    {
        var options = SplunkClientOptions.FromToken(new Uri("services/search", UriKind.Relative), "test-token");

        var exception = Assert.Throws<SplunkConfigurationException>(() => options.Validate());

        Assert.Equal("The Splunk management URI must be absolute.", exception.Message);
    }

    [Fact]
    public void PlainHttpManagementUrisAreRejectedWithRemediationGuidance()
    {
        var options = SplunkClientOptions.FromToken(new Uri("http://splunk.example.com:8089"), "test-token");

        var exception = Assert.Throws<SplunkConfigurationException>(() => options.Validate());

        Assert.Equal(
            "The Splunk management URI uses plain HTTP, which sends the Splunk token unencrypted. Use an https:// management URI, or set AllowInsecureHttp to true for local lab use only.",
            exception.Message);
    }

    [Fact]
    public void PlainHttpManagementUrisAreAcceptedWithExplicitInsecureOptIn()
    {
        var handler = new QueueHttpMessageHandler();
        var options = new SplunkClientOptions
        {
            ManagementUri = new Uri("http://localhost:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            AllowInsecureHttp = true
        };

        options.Validate();
        using var client = new SplunkClient(new HttpClient(handler), options);

        Assert.NotNull(client.Search);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void WhitespaceUserAgentsAreRejected()
    {
        var options = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            UserAgent = "   "
        };

        var exception = Assert.Throws<SplunkConfigurationException>(() => options.Validate());

        Assert.Equal("The user agent must not be empty.", exception.Message);
    }

    [Fact]
    public void UnparseableUserAgentsAreRejectedAtClientConstruction()
    {
        var handler = new QueueHttpMessageHandler();
        var options = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            UserAgent = "@@not a valid user agent@@"
        };

        var exception = Assert.Throws<SplunkConfigurationException>(() => new SplunkClient(new HttpClient(handler), options));

        Assert.Equal(
            "The user agent must be a valid HTTP User-Agent header value, for example \"MyApp/1.0\".",
            exception.Message);
        Assert.IsType<FormatException>(exception.InnerException);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void NonPositiveTimeoutsAreRejected(int timeoutSeconds)
    {
        var options = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        var exception = Assert.Throws<SplunkConfigurationException>(() => options.Validate());

        Assert.Equal("Timeout must be greater than zero when set.", exception.Message);
    }

    [Fact]
    public void NegativeMaxServerDelayIsRejected()
    {
        var options = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            Retry = new SplunkRetryOptions { MaxServerDelay = TimeSpan.FromSeconds(-1) }
        };

        var exception = Assert.Throws<SplunkConfigurationException>(() => options.Validate());

        Assert.Equal("MaxServerDelay must be zero or greater.", exception.Message);
    }

    [Fact]
    public void NegativeMaxRetriesIsRejected()
    {
        var retry = new SplunkRetryOptions { MaxRetries = -1 };

        var exception = Assert.Throws<SplunkConfigurationException>(() => retry.Validate());

        Assert.Equal("MaxRetries must be zero or greater.", exception.Message);
    }
}
