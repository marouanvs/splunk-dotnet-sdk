using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Marouanvs.Splunk;

internal static class SplunkMessageParser
{
    /// <summary>
    /// Hardened XML reader settings: DTD processing is prohibited and no resolver is
    /// used, so external-entity (XXE) payloads in error bodies fail to parse.
    /// </summary>
    private static readonly XmlReaderSettings SecureXmlReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };

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
                    var type = message.TryGetProperty("type", out var typeElement)
                        ? ReadElementAsString(typeElement)
                        : string.Empty;
                    var text = message.TryGetProperty("text", out var textElement)
                        ? ReadElementAsString(textElement)
                        : message.ToString();
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
            using var stringReader = new StringReader(body);
            using var xmlReader = XmlReader.Create(stringReader, SecureXmlReaderSettings);
            var document = XDocument.Load(xmlReader);
            return document.Descendants()
                .Where(element => element.Name.LocalName == "msg")
                .Select(element => new SplunkMessage(
                    element.Attribute("type")?.Value ?? string.Empty,
                    element.Value))
                .ToArray();
        }
        catch (XmlException)
        {
            return Array.Empty<SplunkMessage>();
        }
    }

    private static string ReadElementAsString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => element.ToString()
        };
}
