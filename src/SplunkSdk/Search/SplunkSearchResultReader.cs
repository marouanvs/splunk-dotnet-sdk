using System.Collections.Immutable;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SplunkSdk.Diagnostics;
using SplunkSdk.Models;

namespace SplunkSdk.Search;

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
                foreach (var result in results.EnumerateArray())
                {
                    if (result.ValueKind == JsonValueKind.Object)
                    {
                        yield return new SplunkSearchResult(CloneFields(result));
                    }
                }

                yield break;
            }

            if (root.TryGetProperty("result", out var wrappedResult) &&
                wrappedResult.ValueKind == JsonValueKind.Object)
            {
                var preview = root.TryGetProperty("preview", out var previewElement) &&
                    previewElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                    previewElement.GetBoolean();
                var lastRow = root.TryGetProperty("lastrow", out var lastRowElement) &&
                    lastRowElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                    lastRowElement.GetBoolean();
                var offset = root.TryGetProperty("offset", out var offsetElement) && offsetElement.TryGetInt64(out var parsedOffset)
                    ? parsedOffset
                    : (long?)null;

                yield return new SplunkSearchResult(CloneFields(wrappedResult), preview, offset, lastRow);
                yield break;
            }
        }

        // Splunk export streams can include status/message frames. Only wrapped
        // result payloads should be exposed as data rows.
    }

    private static void ThrowIfFatalMessages(JsonElement messages)
    {
        var parsedMessages = ReadMessages(messages);
        if (parsedMessages.Any(IsFatalMessage))
        {
            var exception = new SplunkApiException(HttpStatusCode.OK, "OK", string.Empty, parsedMessages);
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

    private static bool IsFatalMessage(SplunkMessage message) =>
        string.Equals(message.Type, "ERROR", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(message.Type, "FATAL", StringComparison.OrdinalIgnoreCase);

    private static SplunkApiException CreateMalformedResultStreamException(JsonException innerException)
    {
        _ = innerException;
        var exception = new SplunkApiException(
            HttpStatusCode.OK,
            "OK",
            string.Empty,
            [new SplunkMessage("ERROR", "Splunk returned malformed JSON in the search result stream.")]);
        SplunkDiagnostics.SetException(System.Diagnostics.Activity.Current, exception);
        return exception;
    }

    private static IReadOnlyDictionary<string, JsonElement> CloneFields(JsonElement result)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in result.EnumerateObject())
        {
            fields[property.Name] = property.Value.Clone();
        }

        return fields.ToImmutableDictionary(StringComparer.Ordinal);
    }
}
