using System.Net;
using System.Text.Json;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class AlertSuppressionParsingTests
{
    [Theory]
    [InlineData("\"1\"", "\"600\"", true, 600)]
    [InlineData("\"0\"", "0", false, 0)]
    [InlineData("true", "600", true, 600)]
    [InlineData("false", "\"0\"", false, 0)]
    [InlineData("\"true\"", "42", true, 42)]
    [InlineData("\"false\"", "0", false, 0)]
    public async Task GetSuppressionParsesRealisticSuppressedAndExpirationShapes(
        string suppressedJson,
        string expirationJson,
        bool expectedSuppressed,
        int expectedSeconds)
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            """{"entry":[{"name":"checkout_alert","content":{"suppressed":""" +
            suppressedJson +
            ""","expiration":""" +
            expirationJson +
            """}}]}""");

        using var client = CreateClient(handler);

        var suppression = await client.Alerts.GetSuppressionAsync("checkout_alert");

        Assert.Equal(expectedSuppressed, suppression.Suppressed);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), suppression.Expiration);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task GetSuppressionDefaultsMissingExpirationToZero()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"entry":[{"name":"checkout_alert","content":{"suppressed":true}}]}
        """);

        using var client = CreateClient(handler);

        var suppression = await client.Alerts.GetSuppressionAsync("checkout_alert");

        Assert.True(suppression.Suppressed);
        Assert.Equal(TimeSpan.Zero, suppression.Expiration);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task GetSuppressionClampsNegativeExpirationsToZero()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"entry":[{"name":"checkout_alert","content":{"suppressed":1,"expiration":-30}}]}
        """);

        using var client = CreateClient(handler);

        var suppression = await client.Alerts.GetSuppressionAsync("checkout_alert");

        Assert.True(suppression.Suppressed);
        Assert.Equal(TimeSpan.Zero, suppression.Expiration);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task GetSuppressionSkipsEntriesWithoutAParseableSuppressedFlag()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"entry":[
            {"name":"no_content"},
            {"name":"content_not_object","content":"oops"},
            {"name":"no_flag","content":{"expiration":900}},
            {"name":"checkout_alert","content":{"suppressed":"1","expiration":"300"}}
        ]}
        """);

        using var client = CreateClient(handler);

        var suppression = await client.Alerts.GetSuppressionAsync("checkout_alert");

        Assert.True(suppression.Suppressed);
        Assert.Equal(TimeSpan.FromSeconds(300), suppression.Expiration);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task GetSuppressionWithoutAParseableEntryRaisesSanitizedResponseFormatException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"entry":[{"name":"checkout_alert","content":{"expiration":600}}]}
        """);

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
            await client.Alerts.GetSuppressionAsync("checkout_alert"));

        Assert.Equal(
            "Splunk returned an alert suppression response without a parseable suppression entry.",
            exception.Message);
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.DoesNotContain("checkout_alert", exception.Message, StringComparison.Ordinal);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task GetSuppressionMalformedOrEmptyBodiesRaiseSanitizedResponseFormatExceptions()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"entry":[""");
        handler.Enqueue(HttpStatusCode.OK, string.Empty);

        using var client = CreateClient(handler);

        var malformed = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
            await client.Alerts.GetSuppressionAsync("checkout_alert"));
        var empty = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
            await client.Alerts.GetSuppressionAsync("checkout_alert"));

        Assert.IsAssignableFrom<JsonException>(malformed.InnerException);
        Assert.Contains("alert suppression", malformed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("entry", malformed.Message, StringComparison.Ordinal);
        Assert.Equal("Splunk returned an empty alert suppression response.", empty.Message);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task AcknowledgePostsAnEmptyFormToTheNamespacedAcknowledgeEndpoint()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"messages":[{"type":"INFO","text":"acknowledged"}]}""");

        using var client = CreateClient(handler);

        await client.Alerts.AcknowledgeAsync(
            "checkout alert",
            SplunkNamespace.Create("nobody", "search"));

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(
            "https://splunk.example.com:8089/servicesNS/nobody/search/saved/searches/checkout%20alert/acknowledge",
            sent.Uri.AbsoluteUri);
        Assert.Equal("Bearer", sent.Authorization?.Scheme);
        Assert.Equal(string.Empty, sent.Body);
        handler.AssertNoPendingResponses();
    }
}
