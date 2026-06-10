using System.Net;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class SavedSearchUpdateContractTests
{
    [Fact]
    public async Task UpdateSendsOnlyExplicitlyChangedFields()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("weekly errors", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        var updated = await client.SavedSearches.UpdateAsync("weekly errors", new UpdateSavedSearchRequest
        {
            Search = "search index=\"team\" ERROR"
        });

        Assert.Equal("weekly errors", updated.Name);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(
            "https://splunk.example.com:8089/services/saved/searches/weekly%20errors",
            sent.Uri.AbsoluteUri);

        // Updates must not resend unchanged fields: only the SDK-owned
        // output_mode plus the explicitly supplied replacement SPL go out,
        // and the immutable name never appears in the update form.
        var form = ParseForm(sent.Body);
        Assert.Equal(2, form.Count);
        Assert.Equal("json", form["output_mode"]);
        Assert.Equal("search index=\"team\" ERROR", form["search"]);
        Assert.False(form.ContainsKey("name"), "Expected the saved search name to stay out of update forms.");
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task UpdateWithNoChangedFieldsSendsOnlyOutputMode()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        await client.SavedSearches.UpdateAsync("checkout_errors", new UpdateSavedSearchRequest());

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        var onlyField = Assert.Single(form);
        Assert.Equal("output_mode", onlyField.Key);
        Assert.Equal("json", onlyField.Value);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task UpdateOmitsWhitespaceOnlySearchAndCronSchedule()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        await client.SavedSearches.UpdateAsync("checkout_errors", new UpdateSavedSearchRequest
        {
            Search = "   ",
            CronSchedule = " ",
            Description = "Tracked weekly"
        });

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal(2, form.Count);
        Assert.Equal("json", form["output_mode"]);
        Assert.Equal("Tracked weekly", form["description"]);
        Assert.False(form.ContainsKey("search"), "Expected whitespace-only replacement SPL to be omitted.");
        Assert.False(form.ContainsKey("cron_schedule"), "Expected whitespace-only cron schedules to be omitted.");
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task UpdateValidatesSavedSearchNamesBeforeSendingRequests()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);
        var request = new UpdateSavedSearchRequest { Description = "ignored" };

        var missingName = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SavedSearches.UpdateAsync("   ", request));
        var slashName = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SavedSearches.UpdateAsync("nested/name", request));
        var backslashName = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SavedSearches.UpdateAsync("nested\\name", request));

        Assert.Equal("name", missingName.ParamName);
        Assert.Contains("A saved search name is required.", missingName.Message, StringComparison.Ordinal);
        Assert.Contains("must not contain path separators", slashName.Message, StringComparison.Ordinal);
        Assert.Contains("must not contain path separators", backslashName.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }
}
