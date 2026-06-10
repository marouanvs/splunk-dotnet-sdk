using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Marouanvs.Splunk.Diagnostics;
using Marouanvs.Splunk.Models;

namespace Marouanvs.Splunk.Search;

internal static class SplunkSearchResultReader
{
    public static async IAsyncEnumerable<SplunkSearchResult> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var documents = JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(
                stream,
                options: null,
                topLevelValues: true,
                cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                JsonElement root;
                try
                {
                    if (!await documents.MoveNextAsync().ConfigureAwait(false))
                    {
                        yield break;
                    }

                    root = documents.Current;
                }
                catch (JsonException ex)
                {
                    throw CreateMalformedResultStreamException(ex);
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException)
                {
                    throw CreateInterruptedResultStreamException(ex);
                }

                foreach (var result in ReadElement(root))
                {
                    yield return result;
                }
            }
        }
        finally
        {
            await documents.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static IEnumerable<SplunkSearchResult> ReadElement(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("messages", out var messages) &&
                messages.ValueKind == JsonValueKind.Array)
            {
                ThrowIfFatalMessages(messages);
            }

            if (root.TryGetProperty("results", out var results) &&
                results.ValueKind == JsonValueKind.Array)
            {
                var bodyPreview = ReadBooleanProperty(root, "preview");
                foreach (var result in results.EnumerateArray())
                {
                    if (result.ValueKind == JsonValueKind.Object)
                    {
                        yield return new SplunkSearchResult(CloneFields(result), bodyPreview);
                    }
                }

                yield break;
            }

            if (root.TryGetProperty("result", out var wrappedResult) &&
                wrappedResult.ValueKind == JsonValueKind.Object)
            {
                var preview = ReadBooleanProperty(root, "preview");
                var lastRow = ReadBooleanProperty(root, "lastrow");
                var offset = root.TryGetProperty("offset", out var offsetElement) &&
                    offsetElement.ValueKind == JsonValueKind.Number &&
                    offsetElement.TryGetInt64(out var parsedOffset)
                    ? parsedOffset
                    : (long?)null;

                yield return new SplunkSearchResult(CloneFields(wrappedResult), preview, offset, lastRow);
                yield break;
            }
        }

        // Splunk export streams can include status/message frames. Only wrapped
        // result payloads should be exposed as data rows.
    }

    private static bool ReadBooleanProperty(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) &&
        element.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        element.GetBoolean();

    private static void ThrowIfFatalMessages(JsonElement messages)
    {
        var parsedMessages = ReadMessages(messages);
        if (parsedMessages.Any(IsFatalMessage))
        {
            var exception = new SplunkApiException(System.Net.HttpStatusCode.OK, "OK", parsedMessages);
            SplunkDiagnostics.SetException(System.Diagnostics.Activity.Current, exception);
            throw exception;
        }
    }

    private static IReadOnlyList<SplunkMessage> ReadMessages(JsonElement messages)
    {
        var parsed = new List<SplunkMessage>();
        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind == JsonValueKind.Object)
            {
                var type = message.TryGetProperty("type", out var typeElement) ? ReadElementAsString(typeElement) : string.Empty;
                var text = message.TryGetProperty("text", out var textElement) ? ReadElementAsString(textElement) : message.ToString();
                parsed.Add(new SplunkMessage(type, text));
            }
            else
            {
                parsed.Add(new SplunkMessage(string.Empty, message.ToString()));
            }
        }

        return parsed;
    }

    private static bool IsFatalMessage(SplunkMessage message) =>
        string.Equals(message.Type, "ERROR", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(message.Type, "FATAL", StringComparison.OrdinalIgnoreCase);

    private static string ReadElementAsString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => element.ToString()
        };

    private static SplunkResponseFormatException CreateMalformedResultStreamException(JsonException innerException)
    {
        // The inner JsonException carries parser positions, never payload text.
        var exception = new SplunkResponseFormatException(
            "Splunk returned malformed JSON in the search result stream.",
            innerException);
        SplunkDiagnostics.SetException(System.Diagnostics.Activity.Current, exception);
        return exception;
    }

    private static SplunkResponseFormatException CreateInterruptedResultStreamException(Exception innerException)
    {
        var exception = new SplunkResponseFormatException(
            "The Splunk search export stream was interrupted before completion.",
            innerException);
        SplunkDiagnostics.SetException(System.Diagnostics.Activity.Current, exception);
        return exception;
    }

    private static IReadOnlyDictionary<string, JsonElement> CloneFields(JsonElement result)
    {
        var fields = ImmutableDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in result.EnumerateObject())
        {
            fields[property.Name] = property.Value.Clone();
        }

        return fields.ToImmutable();
    }
}
