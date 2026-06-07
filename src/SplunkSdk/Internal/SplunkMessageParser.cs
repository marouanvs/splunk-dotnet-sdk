using System.Text.Json;
using System.Xml.Linq;

namespace SplunkSdk;

internal static class SplunkMessageParser
{
    public static IReadOnlyList<SplunkMessage> Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<SplunkMessage>();
        }

        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            var jsonMessages = ParseJson(body);
            if (jsonMessages.Count > 0)
            {
                return jsonMessages;
            }
        }

        if (trimmed.StartsWith('<'))
        {
            var xmlMessages = ParseXml(body);
            if (xmlMessages.Count > 0)
            {
                return xmlMessages;
            }
        }

        return Array.Empty<SplunkMessage>();
    }

    private static IReadOnlyList<SplunkMessage> ParseJson(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("messages", out var messages) ||
                messages.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SplunkMessage>();
            }

            var parsed = new List<SplunkMessage>();
            foreach (var message in messages.EnumerateArray())
            {
                if (message.ValueKind == JsonValueKind.Object)
                {
                    var type = message.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty;
                    var text = message.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : message.ToString();
                    parsed.Add(new SplunkMessage(type, text));
                }
                else
                {
                    parsed.Add(new SplunkMessage(string.Empty, message.ToString()));
                }
            }

            return parsed;
        }
        catch (JsonException)
        {
            return Array.Empty<SplunkMessage>();
        }
    }

    private static IReadOnlyList<SplunkMessage> ParseXml(string body)
    {
        try
        {
            var document = XDocument.Parse(body);
            return document.Descendants()
                .Where(element => element.Name.LocalName == "msg")
                .Select(element => new SplunkMessage(
                    element.Attribute("type")?.Value ?? string.Empty,
                    element.Value))
                .ToArray();
        }
        catch (System.Xml.XmlException)
        {
            return Array.Empty<SplunkMessage>();
        }
    }
}
