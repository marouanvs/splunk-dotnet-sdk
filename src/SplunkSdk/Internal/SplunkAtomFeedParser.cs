using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using System.Xml;
using SplunkSdk.Models;

namespace SplunkSdk;

internal static class SplunkAtomFeedParser
{
    public static IReadOnlyList<SplunkSavedSearch> ParseSavedSearches(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<SplunkSavedSearch>();
        }

        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return ParseJsonSavedSearches(body);
        }

        if (trimmed.StartsWith('<'))
        {
            return ParseXmlSavedSearches(body);
        }

        return Array.Empty<SplunkSavedSearch>();
    }

    private static IReadOnlyList<SplunkSavedSearch> ParseJsonSavedSearches(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("entry", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SplunkSavedSearch>();
            }

            var searches = new List<SplunkSavedSearch>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = TryReadString(entry, "name") ?? TryReadString(entry, "title");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var content = new Dictionary<string, string>(StringComparer.Ordinal);
                if (entry.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in contentElement.EnumerateObject())
                    {
                        content[property.Name] = ReadElementAsString(property.Value);
                    }
                }

                searches.Add(ToSavedSearch(name, content));
            }

            return searches;
        }
        catch (JsonException ex)
        {
            throw CreateMalformedSavedSearchResponseException("JSON", ex);
        }
    }

    private static IReadOnlyList<SplunkSavedSearch> ParseXmlSavedSearches(string body)
    {
        try
        {
            var document = XDocument.Parse(body);
            var searches = new List<SplunkSavedSearch>();
            foreach (var entry in document.Descendants().Where(element => element.Name.LocalName == "entry"))
            {
                var name = entry.Elements().FirstOrDefault(element => element.Name.LocalName == "title")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var content = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var key in entry.Descendants().Where(element => element.Name.LocalName == "key"))
                {
                    var keyName = key.Attribute("name")?.Value;
                    if (!string.IsNullOrWhiteSpace(keyName) && !content.ContainsKey(keyName))
                    {
                        content[keyName] = key.Value;
                    }
                }

                searches.Add(ToSavedSearch(name, content));
            }

            return searches;
        }
        catch (XmlException ex)
        {
            throw CreateMalformedSavedSearchResponseException("XML", ex);
        }
    }

    private static SplunkApiException CreateMalformedSavedSearchResponseException(string format, Exception innerException)
    {
        _ = innerException;
        return new SplunkApiException(
            HttpStatusCode.OK,
            "OK",
            string.Empty,
            [new SplunkMessage("ERROR", $"Splunk returned malformed {format} for a saved search response.")]);
    }

    private static SplunkSavedSearch ToSavedSearch(string name, IReadOnlyDictionary<string, string> content)
    {
        var alert = ToAlertSettings(content);

        return new SplunkSavedSearch
        {
            Name = name,
            Search = GetValue(content, "search"),
            Description = GetValue(content, "description"),
            IsScheduled = GetBool(content, "is_scheduled") ?? false,
            CronSchedule = GetValue(content, "cron_schedule"),
            Disabled = GetBool(content, "disabled") ?? false,
            Dispatch = ToDispatchSettings(content),
            Alert = alert,
            Content = new Dictionary<string, string>(content, StringComparer.Ordinal)
        };
    }

    private static SplunkSavedSearchDispatchSettings? ToDispatchSettings(IReadOnlyDictionary<string, string> content)
    {
        var buckets = GetInt(content, "dispatch.buckets");
        var maxCount = GetInt(content, "dispatch.max_count");
        var lookups = GetBool(content, "dispatch.lookups");
        var timeFormat = GetValue(content, "dispatch.time_format");

        if (buckets is null &&
            maxCount is null &&
            lookups is null &&
            string.IsNullOrWhiteSpace(timeFormat))
        {
            return null;
        }

        return new SplunkSavedSearchDispatchSettings
        {
            Buckets = buckets,
            MaxCount = maxCount,
            Lookups = lookups,
            TimeFormat = timeFormat
        };
    }

    private static SplunkAlertSettings? ToAlertSettings(IReadOnlyDictionary<string, string> content)
    {
        var alertType = GetValue(content, "alert_type");
        var comparator = GetValue(content, "alert_comparator");
        var threshold = GetValue(content, "alert_threshold");
        var condition = GetValue(content, "alert_condition");
        var severity = GetValue(content, "alert.severity");
        var expires = GetValue(content, "alert.expires");
        var actions = GetValue(content, "actions");
        var suppression = ToSuppressionSettings(content);
        var email = ToEmailSettings(content);
        var summaryIndex = ToSummaryIndexSettings(content);

        if (string.IsNullOrWhiteSpace(alertType) &&
            string.IsNullOrWhiteSpace(comparator) &&
            string.IsNullOrWhiteSpace(threshold) &&
            string.IsNullOrWhiteSpace(condition) &&
            string.IsNullOrWhiteSpace(severity) &&
            string.IsNullOrWhiteSpace(expires) &&
            string.IsNullOrWhiteSpace(actions) &&
            suppression is null &&
            email is null &&
            summaryIndex is null)
        {
            return null;
        }

        return new SplunkAlertSettings
        {
            AlertType = SplunkEnumExtensions.TryParseAlertType(alertType),
            Comparator = SplunkEnumExtensions.TryParseAlertComparator(comparator),
            Threshold = threshold,
            Condition = condition,
            Severity = int.TryParse(severity, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSeverity)
                ? (SplunkAlertSeverity?)parsedSeverity
                : null,
            Expires = expires,
            Track = GetBool(content, "alert.track"),
            DigestMode = GetBool(content, "alert.digest_mode"),
            Suppression = suppression,
            Email = email,
            SummaryIndex = summaryIndex,
            Actions = string.IsNullOrWhiteSpace(actions)
                ? Array.Empty<string>()
                : actions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        };
    }

    private static SplunkAlertSuppressionSettings? ToSuppressionSettings(IReadOnlyDictionary<string, string> content)
    {
        var enabled = GetBool(content, "alert.suppress");
        var period = GetValue(content, "alert.suppress.period");
        var fields = SplitCsv(GetValue(content, "alert.suppress.fields"));

        if (enabled is null && string.IsNullOrWhiteSpace(period) && fields.Count == 0)
        {
            return null;
        }

        return new SplunkAlertSuppressionSettings
        {
            Enabled = enabled,
            Period = period,
            Fields = fields
        };
    }

    private static SplunkEmailAlertActionSettings? ToEmailSettings(IReadOnlyDictionary<string, string> content)
    {
        var to = SplitCsv(GetValue(content, "action.email.to"));
        var cc = SplitCsv(GetValue(content, "action.email.cc"));
        var bcc = SplitCsv(GetValue(content, "action.email.bcc"));
        var subject = GetValue(content, "action.email.subject");
        var message = GetValue(content, "action.email.message.alert");
        var authUsername = GetValue(content, "action.email.auth_username");
        var pdfView = GetValue(content, "action.email.pdfview");

        if (to.Count == 0 &&
            cc.Count == 0 &&
            bcc.Count == 0 &&
            string.IsNullOrWhiteSpace(subject) &&
            string.IsNullOrWhiteSpace(message) &&
            string.IsNullOrWhiteSpace(authUsername) &&
            string.IsNullOrWhiteSpace(pdfView))
        {
            return null;
        }

        return new SplunkEmailAlertActionSettings
        {
            To = to,
            Cc = cc,
            Bcc = bcc,
            Subject = subject,
            Message = message,
            AuthUsername = authUsername,
            PdfView = pdfView
        };
    }

    private static SplunkSummaryIndexAlertActionSettings? ToSummaryIndexSettings(IReadOnlyDictionary<string, string> content)
    {
        var name = GetValue(content, "action.summary_index._name");
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new SplunkSummaryIndexAlertActionSettings { Name = name };
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> content, string key) =>
        content.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool? GetBool(IReadOnlyDictionary<string, string> content, string key)
    {
        var value = GetValue(content, key);
        return value switch
        {
            null => null,
            "1" => true,
            "0" => false,
            _ when bool.TryParse(value, out var parsed) => parsed,
            _ => null
        };
    }

    private static int? GetInt(IReadOnlyDictionary<string, string> content, string key)
    {
        var value = GetValue(content, key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<string> SplitCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? TryReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) ? ReadElementAsString(property) : null;

    private static string ReadElementAsString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            _ => element.ToString()
        };
}
