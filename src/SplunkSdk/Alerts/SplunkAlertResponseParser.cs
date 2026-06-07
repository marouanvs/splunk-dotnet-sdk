using System.Globalization;
using System.Text.Json;
using Marouanvs.Splunk.Models;

namespace Marouanvs.Splunk.Alerts;

/// <summary>
/// Parses JSON responses from the saved-search suppress and fired-alert endpoints.
/// </summary>
internal static class SplunkAlertResponseParser
{
    // DateTimeOffset.FromUnixTimeSeconds bounds (0001-01-01 to 9999-12-31 UTC).
    private const long MinUnixSeconds = -62_135_596_800L;
    private const long MaxUnixSeconds = 253_402_300_799L;

    public static SplunkAlertSuppression ParseSuppression(string body)
    {
        using var document = ParseDocument(body, "alert suppression");
        foreach (var entry in EnumerateEntries(document.RootElement, "alert suppression"))
        {
            if (!entry.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var suppressed = ReadBool(content, "suppressed");
            if (suppressed is null)
            {
                continue;
            }

            var expirationSeconds = ReadInt64(content, "expiration") ?? 0;
            return new SplunkAlertSuppression
            {
                Suppressed = suppressed.Value,
                Expiration = TimeSpan.FromSeconds(Math.Max(0L, expirationSeconds))
            };
        }

        throw new SplunkResponseFormatException(
            "Splunk returned an alert suppression response without a parseable suppression entry.");
    }

    public static IReadOnlyList<SplunkFiredAlertGroup> ParseFiredAlertGroups(string body)
    {
        using var document = ParseDocument(body, "fired alerts");
        var groups = new List<SplunkFiredAlertGroup>();
        foreach (var entry in EnumerateEntries(document.RootElement, "fired alerts"))
        {
            var name = ReadString(entry, "name") ?? ReadString(entry, "title");
            if (name is null)
            {
                continue;
            }

            int? triggeredAlertCount = null;
            if (entry.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object)
            {
                triggeredAlertCount = ToInt32(ReadInt64(content, "triggered_alert_count"));
            }

            groups.Add(new SplunkFiredAlertGroup
            {
                Name = name,
                TriggeredAlertCount = triggeredAlertCount
            });
        }

        return groups;
    }

    public static IReadOnlyList<SplunkFiredAlert> ParseFiredAlerts(string body)
    {
        using var document = ParseDocument(body, "fired alerts");
        var alerts = new List<SplunkFiredAlert>();
        foreach (var entry in EnumerateEntries(document.RootElement, "fired alerts"))
        {
            var name = ReadString(entry, "name") ?? ReadString(entry, "title");
            if (name is null)
            {
                continue;
            }

            string? savedSearchName = null;
            string? searchId = null;
            string? alertType = null;
            SplunkAlertSeverity? severity = null;
            DateTimeOffset? triggerTime = null;
            int? triggeredAlertCount = null;
            IReadOnlyList<string> actions = Array.Empty<string>();

            if (entry.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object)
            {
                savedSearchName = ReadString(content, "savedsearch_name");
                searchId = ReadString(content, "sid");
                alertType = ReadString(content, "alert_type");
                severity = ToSeverity(ReadInt64(content, "severity"));
                triggerTime = ToTriggerTime(ReadInt64(content, "trigger_time"));
                triggeredAlertCount = ToInt32(ReadInt64(content, "triggered_alerts"));
                actions = ReadStringList(content, "actions");
            }

            alerts.Add(new SplunkFiredAlert
            {
                Name = name,
                SavedSearchName = savedSearchName,
                SearchId = searchId,
                AlertType = alertType,
                Severity = severity,
                TriggerTime = triggerTime,
                TriggeredAlertCount = triggeredAlertCount,
                Actions = actions
            });
        }

        return alerts;
    }

    private static JsonDocument ParseDocument(string body, string subject)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new SplunkResponseFormatException($"Splunk returned an empty {subject} response.");
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new SplunkResponseFormatException($"Splunk returned malformed JSON for a {subject} response.", ex);
        }
    }

    private static IEnumerable<JsonElement> EnumerateEntries(JsonElement root, string subject)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new SplunkResponseFormatException($"Splunk returned an unexpected JSON shape for a {subject} response.");
        }

        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object)
            {
                yield return entry;
            }
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static long? ReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when property.TryGetInt64(out var number) => number != 0,
            JsonValueKind.String => ParseBoolString(property.GetString()),
            _ => null
        };
    }

    private static bool? ParseBoolString(string? value) =>
        value switch
        {
            "1" => true,
            "0" => false,
            _ when bool.TryParse(value, out var parsed) => parsed,
            _ => null
        };

    private static IReadOnlyList<string> ReadStringList(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return Array.Empty<string>();
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            var values = new List<string>();
            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value.Trim());
                }
            }

            return values;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return (property.GetString() ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return Array.Empty<string>();
    }

    private static int? ToInt32(long? value) =>
        value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;

    private static SplunkAlertSeverity? ToSeverity(long? value) =>
        value is >= 1 and <= 6 ? (SplunkAlertSeverity)value.Value : null;

    private static DateTimeOffset? ToTriggerTime(long? epochSeconds) =>
        epochSeconds is >= MinUnixSeconds and <= MaxUnixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value)
            : null;
}
