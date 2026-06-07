using System.Net;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class AlertCreateValidationTests
{
    [Fact]
    public async Task AlertCreateRejectsNullRequests()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await client.Alerts.CreateAsync(null!));

        Assert.Equal("request", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TypedAlertParameterCollisionsAreDetectedCaseInsensitively()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        // The default CreateSplunkAlertRequest.Alert emits the typed
        // alert.severity field, so an additional parameter differing only in
        // case must fail loudly instead of silently overriding it.
        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                AdditionalParameters = new Dictionary<string, string>
                {
                    ["ALERT.SEVERITY"] = "6"
                }
            }));

        Assert.Equal("request", exception.ParamName);
        Assert.Contains(
            "Additional alert parameter 'alert.severity' is controlled by a typed alert setting.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EmailRecipientsMayBeSuppliedThroughAdditionalParameters()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_alert", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        var alert = await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
            "checkout_alert",
            "search index=\"team\" ERROR",
            "*/5 * * * *")
        {
            Alert = new SplunkAlertSettings
            {
                AlertType = SplunkAlertType.Always,
                Actions = ["email"]
            },
            AdditionalParameters = new Dictionary<string, string>
            {
                ["action.email.to"] = "checkout-oncall@example.com"
            }
        });

        Assert.Equal("checkout_alert", alert.Name);

        var form = ParseForm(Assert.Single(handler.Requests).Body);
        Assert.Equal("always", form["alert_type"]);
        Assert.Equal("email", form["actions"]);
        Assert.Equal("1", form["action.email"]);
        Assert.Equal("checkout-oncall@example.com", form["action.email.to"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task EmailActionsEnabledOnlyThroughAdditionalParametersStillRequireRecipients()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings(),
                AdditionalParameters = new Dictionary<string, string>
                {
                    ["actions"] = "webhook, EMAIL"
                }
            }));

        Assert.Equal("alert", exception.ParamName);
        Assert.Contains(
            "Email alert actions require at least one recipient",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CustomConditionsAreAcceptedWithCustomOrUnsetAlertType()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_alert", "search index=\"team\" ERROR"));
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_alert", "search index=\"team\" ERROR"));

        using var client = CreateClient(handler);

        await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
            "checkout_alert",
            "search index=\"team\" ERROR",
            "*/5 * * * *")
        {
            Alert = new SplunkAlertSettings
            {
                AlertType = SplunkAlertType.Custom,
                Condition = "search error_count > 5"
            }
        });
        await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
            "checkout_alert",
            "search index=\"team\" ERROR",
            "*/5 * * * *")
        {
            Alert = new SplunkAlertSettings
            {
                Condition = "search error_count > 5"
            }
        });

        var customForm = ParseForm(handler.Requests[0].Body);
        Assert.Equal("custom", customForm["alert_type"]);
        Assert.Equal("search error_count > 5", customForm["alert_condition"]);
        Assert.False(customForm.ContainsKey("alert_comparator"), "Expected no comparator for a custom condition alert.");

        var unsetForm = ParseForm(handler.Requests[1].Body);
        Assert.False(unsetForm.ContainsKey("alert_type"), "Expected alert_type to be omitted when AlertType is unset.");
        Assert.Equal("search error_count > 5", unsetForm["alert_condition"]);
        handler.AssertNoPendingResponses();
    }
}
