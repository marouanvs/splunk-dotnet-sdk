using System.Net;
using System.Xml;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class SavedSearchResponseParsingTests
{
    /// <summary>
    /// An XXE payload: the DOCTYPE declares an external entity pointing at a local
    /// file and references it from the entry title. The hardened parser settings
    /// must reject the DTD outright instead of resolving the entity.
    /// </summary>
    private const string ExternalEntityFeedBody = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE feed [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <title>&xxe;</title>
          </entry>
        </feed>
        """;

    [Fact]
    public async Task SavedSearchBodyThatIsNeitherJsonNorXmlRaisesResponseFormatException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "Service Temporarily Unavailable", "text/plain");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
            await client.SavedSearches.ListAsync());

        Assert.Equal("Splunk returned a saved search response that is neither JSON nor XML.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("Unparseable response", exception.ReasonPhrase);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task HtmlErrorPageWithDoctypeRaisesResponseFormatExceptionWithXmlInnerException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            "<!DOCTYPE html><html><head><title>Login required</title></head><body>Please sign in.</body></html>",
            "text/html");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
            await client.SavedSearches.ListAsync());

        Assert.Equal("Splunk returned malformed XML for a saved search response.", exception.Message);
        Assert.IsAssignableFrom<XmlException>(exception.InnerException);
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task WellFormedHtmlBodyOnCreateRaisesResponseFormatExceptionInsteadOfAStub()
    {
        // DOCTYPE-less HTML parses as well-formed XML but contains no Atom entries.
        // The single-entry create path must fail loudly rather than fabricate a stub.
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "<html><body>Login required</body></html>", "text/html");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
            await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest("checkout_errors", "search index=\"team\" ERROR")));

        Assert.Equal(
            "Splunk returned a successful saved search response without a parseable saved search entry.",
            exception.Message);
        Assert.DoesNotContain("Login required", exception.Message, StringComparison.Ordinal);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ExternalEntityPayloadInSavedSearchBodyIsRejectedAsMalformedXml()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, ExternalEntityFeedBody, "application/xml");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
            await client.SavedSearches.ListAsync());

        Assert.Equal("Splunk returned malformed XML for a saved search response.", exception.Message);
        Assert.IsAssignableFrom<XmlException>(exception.InnerException);
        Assert.DoesNotContain("passwd", exception.Message, StringComparison.Ordinal);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ExternalEntityPayloadInErrorBodyProducesNoParsedMessages()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, ExternalEntityFeedBody, "application/xml");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.SavedSearches.ListAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Empty(exception.Messages);
        Assert.Equal("Splunk API request failed with 503 ServiceUnavailable.", exception.Message);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task NestedAclDictionaryDoesNotPolluteTopLevelSavedSearchContentKeys()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:s="http://dev.splunk.com/ns/rest">
              <title>savedsearch</title>
              <entry>
                <title>checkout_errors</title>
                <content type="text/xml">
                  <s:dict>
                    <s:key name="search">search index="team" ERROR</s:key>
                    <s:key name="is_scheduled">1</s:key>
                    <s:key name="eai:acl">
                      <s:dict>
                        <s:key name="app">search</s:key>
                        <s:key name="owner">admin</s:key>
                        <s:key name="perms">
                          <s:dict>
                            <s:key name="read">
                              <s:list>
                                <s:item>*</s:item>
                              </s:list>
                            </s:key>
                          </s:dict>
                        </s:key>
                      </s:dict>
                    </s:key>
                  </s:dict>
                </content>
              </entry>
            </feed>
            """, "application/xml");

        using var client = CreateClient(handler);

        var savedSearch = Assert.Single(await client.SavedSearches.ListAsync());

        Assert.Equal("checkout_errors", savedSearch.Name);
        Assert.Equal("search index=\"team\" ERROR", savedSearch.Search);
        Assert.True(savedSearch.IsScheduled);

        // Only the direct children of the entry's content dict are top-level keys;
        // the nested eai:acl structure must not leak its keys to the top level.
        Assert.Equal(3, savedSearch.Content.Count);
        Assert.Contains("search", savedSearch.Content);
        Assert.Contains("is_scheduled", savedSearch.Content);
        Assert.Contains("eai:acl", savedSearch.Content);
        Assert.DoesNotContain("app", savedSearch.Content);
        Assert.DoesNotContain("owner", savedSearch.Content);
        Assert.DoesNotContain("perms", savedSearch.Content);
        Assert.DoesNotContain("read", savedSearch.Content);

        // The nested structure stays available, serialized under its own key.
        Assert.Contains("admin", savedSearch.Content["eai:acl"], StringComparison.Ordinal);
        handler.AssertNoPendingResponses();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("999")]
    public async Task SavedSearchSeverityOutsideDocumentedScaleIsDroppedFromJsonResponses(string severity)
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, $$"""
            {
              "entry": [
                {
                  "name": "checkout_alert",
                  "content": {
                    "alert_type": "number of events",
                    "alert.severity": "{{severity}}"
                  }
                }
              ]
            }
            """);

        using var client = CreateClient(handler);

        var savedSearch = Assert.Single(await client.SavedSearches.ListAsync());

        Assert.NotNull(savedSearch.Alert);
        Assert.Equal(SplunkAlertType.NumberOfEvents, savedSearch.Alert!.AlertType);
        Assert.Null(savedSearch.Alert.Severity);
        handler.AssertNoPendingResponses();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    public async Task SavedSearchSeverityOutsideDocumentedScaleIsDroppedFromXmlResponses(string severity)
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:s="http://dev.splunk.com/ns/rest">
              <entry>
                <title>checkout_alert</title>
                <content type="text/xml">
                  <s:dict>
                    <s:key name="alert_type">number of events</s:key>
                    <s:key name="alert.severity">{{severity}}</s:key>
                  </s:dict>
                </content>
              </entry>
            </feed>
            """, "application/xml");

        using var client = CreateClient(handler);

        var savedSearch = Assert.Single(await client.SavedSearches.ListAsync());

        Assert.NotNull(savedSearch.Alert);
        Assert.Equal(SplunkAlertType.NumberOfEvents, savedSearch.Alert!.AlertType);
        Assert.Null(savedSearch.Alert.Severity);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task CreateSavedSearchWithZeroParseableEntriesRaisesResponseFormatException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"entry":[]}""");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
            await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest("checkout_errors", "search index=\"team\" ERROR")));

        Assert.Equal(
            "Splunk returned a successful saved search response without a parseable saved search entry.",
            exception.Message);
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("Unparseable response", exception.ReasonPhrase);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task UpdateSavedSearchWithNamelessEntryRaisesResponseFormatExceptionInsteadOfAStub()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"entry":[{"content":{"search":"search index=\"team\" ERROR"}}]}""");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
            await client.SavedSearches.UpdateAsync("checkout_errors", new UpdateSavedSearchRequest
            {
                Search = "search index=\"team\" WARN"
            }));

        Assert.Equal(
            "Splunk returned a successful saved search response without a parseable saved search entry.",
            exception.Message);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task GetSavedSearchWithZeroParseableEntriesReturnsNullWithoutFabricatingAStub()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"entry":[]}""");

        using var client = CreateClient(handler);

        var savedSearch = await client.SavedSearches.GetAsync("checkout_errors");

        Assert.Null(savedSearch);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task NumericErrorMessageShapesDegradeGracefullyIntoTheApiException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, """{"messages":[{"type":1,"text":2}]}""");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.SavedSearches.ListAsync());

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        var message = Assert.Single(exception.Messages);
        Assert.Equal("1", message.Type);
        Assert.Equal("2", message.Text);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task MixedNonStringErrorMessageShapesDegradeGracefullyIntoTheApiException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, """{"messages":["throttled",{"type":null},{"text":"limit reached"}]}""");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkApiException>(async () =>
            await client.SavedSearches.ListAsync());

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(3, exception.Messages.Count);
        Assert.Equal(new SplunkMessage(string.Empty, "throttled"), exception.Messages[0]);
        Assert.Equal(new SplunkMessage(string.Empty, """{"type":null}"""), exception.Messages[1]);
        Assert.Equal(new SplunkMessage(string.Empty, "limit reached"), exception.Messages[2]);
        handler.AssertNoPendingResponses();
    }
}
