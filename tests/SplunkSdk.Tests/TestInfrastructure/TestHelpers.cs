using System.Reflection;
using Marouanvs.Splunk.Authentication;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.Models;

namespace Marouanvs.Splunk.Tests.TestInfrastructure;

internal static class TestHelpers
{
    internal static SplunkClient CreateClient(QueueHttpMessageHandler handler, int maxRetries = 0)
    {
        var retry = maxRetries > 0
            ? new SplunkRetryOptions
            {
                MaxRetries = maxRetries,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(1)
            }
            : new SplunkRetryOptions
            {
                MaxRetries = 0,
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero
            };

        return CreateClient(handler, retry);
    }

    internal static SplunkClient CreateClient(QueueHttpMessageHandler handler, SplunkRetryOptions retry)
    {
        var httpClient = new HttpClient(handler);
        var options = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            Retry = retry
        };

        return new SplunkClient(httpClient, options);
    }

    // Decodes an application/x-www-form-urlencoded payload. '+' is a space in
    // this encoding, so it is translated before percent-decoding; an encoded
    // %2B therefore round-trips to a literal '+'.
    internal static Dictionary<string, string> ParseForm(string body)
    {
        return body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => DecodeFormToken(pair[0]),
                pair => DecodeFormToken(pair.ElementAtOrDefault(1) ?? string.Empty),
                StringComparer.Ordinal);
    }

    internal static SplunkSearchResult CreateRow(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var fields = document.RootElement.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.Clone(),
                StringComparer.Ordinal);

        return new SplunkSearchResult(fields);
    }

    internal static string SdkInformationalVersion()
    {
        var version = typeof(SplunkClientOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            version = typeof(SplunkClientOptions).Assembly.GetName().Version?.ToString();
        }

        return string.IsNullOrWhiteSpace(version)
            ? "0.0.0"
            : version.Split('+', 2)[0];
    }

    internal static string SavedSearchFeed(string name, string search) =>
    $$"""
    {
      "entry": [
        {
          "name": "{{name}}",
          "content": {
            "search": "{{JsonEscape(search)}}",
            "description": "Checkout search",
            "is_scheduled": "1",
            "cron_schedule": "*/5 * * * *",
            "disabled": "0",
            "alert_type": "number of events",
            "alert_comparator": "greater than",
            "alert_threshold": "0",
            "alert.severity": "4",
            "alert.track": "1",
            "alert.digest_mode": "1",
            "actions": "email"
          }
        }
      ]
    }
    """;

    internal static string AdvancedSavedSearchFeed(string name, string search) =>
    $$"""
    {
      "entry": [
        {
          "name": "{{name}}",
          "content": {
            "search": "{{JsonEscape(search)}}",
            "description": "Checkout search",
            "is_scheduled": "1",
            "cron_schedule": "*/5 * * * *",
            "disabled": "0",
            "dispatch.buckets": "25",
            "dispatch.max_count": "1000",
            "dispatch.lookups": "1",
            "dispatch.time_format": "%s",
            "alert_type": "number of events",
            "alert_comparator": "greater than",
            "alert_threshold": "0",
            "alert.severity": "4",
            "alert.expires": "2h",
            "alert.track": "1",
            "alert.digest_mode": "1",
            "alert.suppress": "1",
            "alert.suppress.period": "30m",
            "alert.suppress.fields": "service,host",
            "actions": "email,summary_index",
            "action.email.to": "checkout-oncall@example.com,checkout-backup@example.com",
            "action.email.subject": "Checkout alert",
            "action.email.message.alert": "Checkout errors detected",
            "action.summary_index._name": "summary_errors"
          }
        }
      ]
    }
    """;

    private static string DecodeFormToken(string token) =>
        Uri.UnescapeDataString(token.Replace('+', ' '));

    private static string JsonEscape(string value) =>
    value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
}
