using System.Net;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class SplunkNamespaceTests
{
    [Fact]
    public void CreateAcceptsValidOwnerAndAppSegments()
    {
        var splunkNamespace = SplunkNamespace.Create("nobody", "search");

        Assert.Equal("nobody", splunkNamespace.Owner);
        Assert.Equal("search", splunkNamespace.App);
        Assert.Equal(SplunkNamespace.Create("nobody", "search"), splunkNamespace);
    }

    [Fact]
    public async Task WildcardDashSegmentsAreAcceptedAndAddressServicesNsPaths()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        var searches = await client.SavedSearches.ListAsync(new SplunkSavedSearchListRequest
        {
            Namespace = SplunkNamespace.Create("-", "-")
        });

        Assert.Single(searches);
        Assert.Equal(
            "https://splunk.example.com:8089/servicesNS/-/-/saved/searches?output_mode=json",
            Assert.Single(handler.Requests).Uri.AbsoluteUri);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task OwnerAndAppSegmentsAreEscapedIntoRequestPaths()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        await client.SavedSearches.ListAsync(new SplunkSavedSearchListRequest
        {
            Namespace = SplunkNamespace.Create("user name", "my app")
        });

        Assert.Equal(
            "https://splunk.example.com:8089/servicesNS/user%20name/my%20app/saved/searches?output_mode=json",
            Assert.Single(handler.Requests).Uri.AbsoluteUri);
        handler.AssertNoPendingResponses();
    }

    [Theory]
    [InlineData("", "search", "owner")]
    [InlineData("   ", "search", "owner")]
    [InlineData("nobody", "", "app")]
    [InlineData("nobody", "\t", "app")]
    public void EmptyOrWhitespaceSegmentsAreRejected(string owner, string app, string expectedParamName)
    {
        var exception = Assert.Throws<ArgumentException>(() => SplunkNamespace.Create(owner, app));

        Assert.Equal(expectedParamName, exception.ParamName);
        Assert.Contains(
            "Splunk namespace segments must not be empty.",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("no/body", "search", "owner")]
    [InlineData("back\\slash", "search", "owner")]
    [InlineData("nobody", "sea/rch", "app")]
    public void PathSeparatorSegmentsAreRejected(string owner, string app, string expectedParamName)
    {
        var exception = Assert.Throws<ArgumentException>(() => SplunkNamespace.Create(owner, app));

        Assert.Equal(expectedParamName, exception.ParamName);
        Assert.Contains(
            "Splunk namespace segments must not contain path separators.",
            exception.Message,
            StringComparison.Ordinal);
    }
}
