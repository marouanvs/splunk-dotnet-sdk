using System.Net;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class AlertTests
{
    [Fact]
    public async Task AlertsCallSavedSearchAlertEndpoints()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, SavedSearchFeed("checkout_alert", "search index=\"team\" ERROR"));
        handler.Enqueue(HttpStatusCode.OK, """{"messages":[{"type":"INFO","text":"acknowledged"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """{"messages":[{"type":"INFO","text":"suppressed"}]}""");

        using var client = CreateClient(handler);

        var alert = await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
            "checkout_alert",
            "search index=\"team\" ERROR",
            "*/5 * * * *")
        {
            Alert = new SplunkAlertSettings
            {
                AlertType = SplunkAlertType.NumberOfEvents,
                Comparator = SplunkAlertComparator.GreaterThan,
                Threshold = "0",
                Severity = SplunkAlertSeverity.Error,
                Expires = "2h",
                Track = true,
                DigestMode = true,
                Actions = ["email"],
                Suppression = new SplunkAlertSuppressionSettings
                {
                    Enabled = true,
                    Period = "30m",
                    Fields = ["service", "host"]
                },
                Email = new SplunkEmailAlertActionSettings
                {
                    To = ["checkout-oncall@example.com", "checkout-backup@example.com"],
                    Cc = ["checkout-managers@example.com"],
                    Subject = "Checkout alert",
                    Message = "Checkout errors detected",
                    AuthUsername = "smtp-alerts",
                    PdfView = "search"
                },
                SummaryIndex = new SplunkSummaryIndexAlertActionSettings
                {
                    Name = "summary_errors"
                }
            }
        });
        await client.Alerts.AcknowledgeAsync("checkout_alert");
        await client.Alerts.SuppressAsync("checkout_alert", TimeSpan.FromMinutes(30));

        Assert.Equal("checkout_alert", alert.Name);

        var createForm = ParseForm(handler.Requests[0].Body);
        Assert.Equal("1", createForm["is_scheduled"]);
        Assert.Equal("number of events", createForm["alert_type"]);
        Assert.Equal("greater than", createForm["alert_comparator"]);
        Assert.Equal("0", createForm["alert_threshold"]);
        Assert.Equal("4", createForm["alert.severity"]);
        Assert.Equal("2h", createForm["alert.expires"]);
        Assert.Equal("1", createForm["alert.track"]);
        Assert.Equal("1", createForm["alert.digest_mode"]);
        Assert.Equal("1", createForm["alert.suppress"]);
        Assert.Equal("30m", createForm["alert.suppress.period"]);
        Assert.Equal("service,host", createForm["alert.suppress.fields"]);
        Assert.Equal("email,summary_index", createForm["actions"]);
        Assert.Equal("1", createForm["action.email"]);
        Assert.Equal("checkout-oncall@example.com,checkout-backup@example.com", createForm["action.email.to"]);
        Assert.Equal("checkout-managers@example.com", createForm["action.email.cc"]);
        Assert.Equal("Checkout alert", createForm["action.email.subject"]);
        Assert.Equal("Checkout errors detected", createForm["action.email.message.alert"]);
        Assert.Equal("smtp-alerts", createForm["action.email.auth_username"]);
        Assert.Equal("search", createForm["action.email.pdfview"]);
        Assert.Equal("1", createForm["action.summary_index"]);
        Assert.Equal("summary_errors", createForm["action.summary_index._name"]);

        Assert.Equal("https://splunk.example.com:8089/services/saved/searches/checkout_alert/acknowledge", handler.Requests[1].Uri.ToString());
        Assert.Equal("https://splunk.example.com:8089/services/saved/searches/checkout_alert/suppress", handler.Requests[2].Uri.ToString());
        Assert.Equal("1800", ParseForm(handler.Requests[2].Body)["expiration"]);
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task AlertsValidateDocumentedCrossFieldRules()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings
                {
                    Actions = ["email"]
                }
            }));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings
                {
                    AlertType = SplunkAlertType.NumberOfEvents,
                    Condition = "search count > 0"
                }
            }));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings
                {
                    Suppression = new SplunkAlertSuppressionSettings
                    {
                        Enabled = true
                    }
                }
            }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AlertCreateRequiresCronSchedule()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                " ")));

        Assert.Equal("CronSchedule", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AlertsRejectUnsafeActionNames()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings
                {
                    AlertType = SplunkAlertType.Always,
                    Actions = ["webhook,summary_index"]
                }
            }));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings
                {
                    AlertType = SplunkAlertType.Always,
                    Actions = ["custom/action"]
                }
            }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AlertsRejectAdditionalParametersThatOverrideTypedAlertSettings()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.Alerts.CreateAsync(new CreateSplunkAlertRequest(
                "checkout_alert",
                "search index=\"team\" ERROR",
                "*/5 * * * *")
            {
                Alert = new SplunkAlertSettings
                {
                    Actions = ["email"],
                    Email = new SplunkEmailAlertActionSettings
                    {
                        To = ["checkout-oncall@example.com"]
                    }
                },
                AdditionalParameters = new Dictionary<string, string>
                {
                    ["actions"] = "webhook"
                }
            }));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void AlertSeverityUsesSavedSearchScale()
    {
        Assert.Equal(1, (int)SplunkAlertSeverity.Debug);
        Assert.Equal(2, (int)SplunkAlertSeverity.Info);
        Assert.Equal(3, (int)SplunkAlertSeverity.Warn);
        Assert.Equal(4, (int)SplunkAlertSeverity.Error);
        Assert.Equal(5, (int)SplunkAlertSeverity.Severe);
        Assert.Equal(6, (int)SplunkAlertSeverity.Fatal);
    }

    [Fact]
    public async Task AlertSuppressionLifecycleUsesTheSuppressEndpoint()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"messages":[{"type":"INFO","text":"unsuppressed"}]}""");
        handler.Enqueue(HttpStatusCode.OK, """
        {"entry":[{"name":"checkout_alert","content":{"suppressed":1,"expiration":600}}]}
        """);

        using var client = CreateClient(handler);

        await client.Alerts.UnsuppressAsync("checkout_alert");
        var suppression = await client.Alerts.GetSuppressionAsync("checkout_alert");

        Assert.True(suppression.Suppressed);
        Assert.Equal(TimeSpan.FromMinutes(10), suppression.Expiration);

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(
            "https://splunk.example.com:8089/services/saved/searches/checkout_alert/suppress",
            handler.Requests[0].Uri.ToString());
        Assert.Equal("0", ParseForm(handler.Requests[0].Body)["expiration"]);

        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal(
            "https://splunk.example.com:8089/services/saved/searches/checkout_alert/suppress?output_mode=json",
            handler.Requests[1].Uri.ToString());
        handler.AssertNoPendingResponses();
    }

    [Fact]
    public async Task SuppressRejectsNonPositiveExpirations()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await client.Alerts.SuppressAsync("checkout_alert", TimeSpan.Zero));

        Assert.Equal("expiration", exception.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FiredAlertListingsUseReadOnlyGetEndpoints()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"entry":[{"name":"checkout_alert","content":{"triggered_alert_count":3}}]}
        """);
        handler.Enqueue(HttpStatusCode.OK, """
        {"entry":[
            {"name":"scheduler_checkout_1","content":{"savedsearch_name":"checkout_alert","sid":"1710000000.42","alert_type":"number of events","severity":4,"trigger_time":1700000000,"triggered_alerts":3,"actions":["email"]}},
            {"name":"scheduler_checkout_2","content":{"severity":9}}
        ]}
        """);

        using var client = CreateClient(handler);

        var groups = await client.Alerts.ListFiredAlertGroupsAsync();
        var alerts = await client.Alerts.ListFiredAlertsAsync("checkout_alert");

        var group = Assert.Single(groups);
        Assert.Equal("checkout_alert", group.Name);
        Assert.Equal(3, group.TriggeredAlertCount);

        Assert.Equal(2, alerts.Count);
        Assert.Equal("checkout_alert", alerts[0].SavedSearchName);
        Assert.Equal("1710000000.42", alerts[0].SearchId);
        Assert.Equal(SplunkAlertSeverity.Error, alerts[0].Severity);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), alerts[0].TriggerTime);
        Assert.Equal(3, alerts[0].TriggeredAlertCount);
        Assert.Equal("email", Assert.Single(alerts[0].Actions));

        // Severity values outside the documented 1-6 savedsearches.conf scale are
        // dropped instead of surfacing undefined enum values.
        Assert.Null(alerts[1].Severity);

        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.Equal(
            "https://splunk.example.com:8089/services/alerts/fired_alerts?output_mode=json",
            handler.Requests[0].Uri.ToString());
        Assert.Equal(
            "https://splunk.example.com:8089/services/alerts/fired_alerts/checkout_alert?output_mode=json",
            handler.Requests[1].Uri.ToString());
        handler.AssertNoPendingResponses();
    }
}
