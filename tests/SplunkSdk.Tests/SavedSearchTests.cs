using System.Net;
using System.Text.Json;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class SavedSearchTests
{
    [Fact]
    public async Task SavedSearchesCallSplunkEndpoints()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" WARN"));
        handler.Enqueue(HttpStatusCode.OK, """{"sid":"1710000000.10"}""");
        handler.Enqueue(HttpStatusCode.OK, """{"messages":[{"type":"INFO","text":"deleted"}]}""");

        using var client = CreateClient(handler);

        var savedSearches = await client.SavedSearches.ListAsync(new SplunkSavedSearchListRequest { Count = 10 });
        var created = await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest("checkout_errors", "search index=\"team\" ERROR")
        {
            Description = "Checkout error count",
            IsScheduled = true,
            CronSchedule = "*/5 * * * *",
            TimeRange = SplunkTimeRange.Relative("-15m", "now"),
            Dispatch = new SplunkSavedSearchDispatchSettings
            {
                Buckets = 10,
                MaxCount = 5000,
                Lookups = true,
                TimeFormat = "%s"
            }
        });
        var updated = await client.SavedSearches.UpdateAsync("checkout_errors", new UpdateSavedSearchRequest
        {
            Search = "search index=\"team\" WARN",
            Disabled = true,
            Dispatch = new SplunkSavedSearchDispatchSettings
            {
                Lookups = false
            }
        });
        var job = await client.SavedSearches.DispatchAsync("checkout_errors", new SplunkDispatchSavedSearchRequest
        {
            Parameters = new Dictionary<string, string> { ["dispatch.now"] = "1" }
        });
        await client.SavedSearches.DeleteAsync("checkout_errors");

        Assert.Single(savedSearches);
        Assert.Equal("checkout_errors", created.Name);
        Assert.Equal(SplunkAlertType.NumberOfEvents, created.Alert?.AlertType);
        Assert.Equal(SplunkAlertSeverity.Error, created.Alert?.Severity);
        Assert.Equal("search index=\"team\" WARN", updated.Search);
        Assert.Equal("1710000000.10", job.Sid);

        Assert.Equal("https://splunk.example.com:8089/services/saved/searches?output_mode=json&count=10", handler.Requests[0].Uri.ToString());

        var createForm = ParseForm(handler.Requests[1].Body);
        Assert.Equal("checkout_errors", createForm["name"]);
        Assert.Equal("search index=\"team\" ERROR", createForm["search"]);
        Assert.Equal("1", createForm["is_scheduled"]);
        Assert.Equal("*/5 * * * *", createForm["cron_schedule"]);
        Assert.Equal("-15m", createForm["dispatch.earliest_time"]);
        Assert.Equal("now", createForm["dispatch.latest_time"]);
        Assert.Equal("10", createForm["dispatch.buckets"]);
        Assert.Equal("5000", createForm["dispatch.max_count"]);
        Assert.Equal("1", createForm["dispatch.lookups"]);
        Assert.Equal("%s", createForm["dispatch.time_format"]);

        var updateForm = ParseForm(handler.Requests[2].Body);
        Assert.Equal("search index=\"team\" WARN", updateForm["search"]);
        Assert.Equal("1", updateForm["disabled"]);
        Assert.Equal("0", updateForm["dispatch.lookups"]);

        Assert.Equal("https://splunk.example.com:8089/services/saved/searches/checkout_errors/dispatch", handler.Requests[3].Uri.ToString());
        Assert.Equal("1", ParseForm(handler.Requests[3].Body)["dispatch.now"]);
        Assert.Equal(HttpMethod.Delete, handler.Requests[4].Method);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task SavedSearchesParseTypedDispatchAndAlertActionFields()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, AdvancedSavedSearchFeed("checkout_alert", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        var savedSearch = Assert.Single(await client.SavedSearches.ListAsync());

        Assert.Equal(25, savedSearch.Dispatch?.Buckets);
        Assert.Equal(1000, savedSearch.Dispatch?.MaxCount);
        Assert.True(savedSearch.Dispatch?.Lookups);
        Assert.Equal("%s", savedSearch.Dispatch?.TimeFormat);
        Assert.Equal("2h", savedSearch.Alert?.Expires);
        Assert.True(savedSearch.Alert?.Suppression?.Enabled);
        Assert.Equal("30m", savedSearch.Alert?.Suppression?.Period);
        Assert.Equal(["service", "host"], savedSearch.Alert?.Suppression?.Fields);
        Assert.Equal(["checkout-oncall@example.com", "checkout-backup@example.com"], savedSearch.Alert?.Email?.To);
        Assert.Equal("Checkout alert", savedSearch.Alert?.Email?.Subject);
        Assert.Equal("Checkout errors detected", savedSearch.Alert?.Email?.Message);
        Assert.Equal("summary_errors", savedSearch.Alert?.SummaryIndex?.Name);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task SavedSearchGetReturnsNullForNotFound()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NotFound, """{"messages":[{"type":"ERROR","text":"Not found"}]}""");

        using var client = CreateClient(handler);

        var savedSearch = await client.SavedSearches.GetAsync("missing_search");

        Assert.Null(savedSearch);
        Assert.Equal(
            "https://splunk.example.com:8089/services/saved/searches/missing_search?output_mode=json",
            Assert.Single(handler.Requests).Uri.ToString());
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task ScheduledSavedSearchCreateRequiresCronSchedule()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest("checkout_errors", "search index=\"team\" ERROR")
            {
                IsScheduled = true
            }));

        Assert.Equal("CronSchedule", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SavedSearchUpdateSendsEmptyDescription()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        await client.SavedSearches.UpdateAsync("checkout_errors", new UpdateSavedSearchRequest
        {
            Description = string.Empty
        });

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal("json", form["output_mode"]);
        Assert.True(form.ContainsKey("description"), "Expected empty description to be sent explicitly.");
        Assert.Equal(string.Empty, form["description"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task SavedSearchesRejectAdditionalParametersThatOverrideTypedProperties()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest("checkout_errors", "search index=\"team\" ERROR")
            {
                CronSchedule = "0 6 * * 1",
                AdditionalParameters = new Dictionary<string, string>
                {
                    ["cron_schedule"] = "*/5 * * * *"
                }
            }));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest("checkout_errors", "search index=\"team\" ERROR")
            {
                Dispatch = new SplunkSavedSearchDispatchSettings { Buckets = 10 },
                AdditionalParameters = new Dictionary<string, string>
                {
                    ["dispatch.buckets"] = "25"
                }
            }));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SavedSearches.UpdateAsync("checkout_errors", new UpdateSavedSearchRequest
            {
                Disabled = true,
                AdditionalParameters = new Dictionary<string, string>
                {
                    ["disabled"] = "0"
                }
            }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SavedSearchAndAlertNullParameterCollectionsAreTreatedAsEmpty()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" ERROR"));
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_errors", "search index=\"team\" WARN"));
        handler.Enqueue(HttpStatusCode.OK, """{"sid":"1710000000.11"}""");
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_alert", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        var created = await client.SavedSearches.CreateAsync(new CreateSavedSearchRequest("checkout_errors", "search index=\"team\" ERROR")
        {
            AdditionalParameters = null!
        });
        var updated = await client.SavedSearches.UpdateAsync("checkout_errors", new UpdateSavedSearchRequest
        {
            Search = "search index=\"team\" WARN",
            AdditionalParameters = null!
        });
        var job = await client.SavedSearches.DispatchAsync("checkout_errors", new SplunkDispatchSavedSearchRequest
        {
            Parameters = null!
        });
        var alert = await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
            "checkout_alert",
            "search index=\"team\" ERROR",
            "*/5 * * * *")
        {
            AdditionalParameters = null!
        });

        Assert.Equal("checkout_errors", created.Name);
        Assert.Equal("search index=\"team\" WARN", updated.Search);
        Assert.Equal("1710000000.11", job.Sid);
        Assert.Equal("checkout_alert", alert.Name);

        Assert.Equal("search index=\"team\" ERROR", ParseForm(handler.Requests[0].Body)["search"]);
        Assert.Equal("search index=\"team\" WARN", ParseForm(handler.Requests[1].Body)["search"]);

        // Dispatch always sends the SDK-owned output_mode=json field, even when
        // the caller-supplied parameter collection is null.
        var dispatchForm = ParseForm(handler.Requests[2].Body);
        var dispatchField = Assert.Single(dispatchForm);
        Assert.Equal("output_mode", dispatchField.Key);
        Assert.Equal("json", dispatchField.Value);

        var alertForm = ParseForm(handler.Requests[3].Body);
        Assert.Equal("1", alertForm["is_scheduled"]);
        Assert.Equal("number of events", alertForm["alert_type"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task SavedSearchesMalformedJsonRaisesSanitizedSplunkException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"entry":[""");

        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
            await client.SavedSearches.ListAsync());

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.DoesNotContain("entry", exception.Message, StringComparison.Ordinal);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task SavedSearchNamesAndNamespaceSegmentsRejectDotSegments()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () => await client.SavedSearches.DeleteAsync("."));
        await Assert.ThrowsAsync<ArgumentException>(async () => await client.SavedSearches.DeleteAsync(".."));
        await Assert.ThrowsAsync<ArgumentException>(async () => await client.Alerts.AcknowledgeAsync(".."));
        Assert.Throws<ArgumentException>(() => SplunkNamespace.Create(".", "search"));
        Assert.Throws<ArgumentException>(() => SplunkNamespace.Create("nobody", ".."));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DispatchRejectsReservedAndEmptyParameterNames()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<SplunkConfigurationException>(async () =>
            await client.SavedSearches.DispatchAsync("checkout_errors", new SplunkDispatchSavedSearchRequest
            {
                Parameters = new Dictionary<string, string> { ["OUTPUT_MODE"] = "xml" }
            }));

        await Assert.ThrowsAsync<SplunkConfigurationException>(async () =>
            await client.SavedSearches.DispatchAsync("checkout_errors", new SplunkDispatchSavedSearchRequest
            {
                Parameters = new Dictionary<string, string> { [" "] = "value" }
            }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UpdateRequiresCronScheduleWhenSchedulingIsEnabled()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.SavedSearches.UpdateAsync("checkout_errors", new UpdateSavedSearchRequest
            {
                IsScheduled = true
            }));

        Assert.Equal("CronSchedule", exception.ParamName);
        Assert.Empty(handler.Requests);
    }
}
