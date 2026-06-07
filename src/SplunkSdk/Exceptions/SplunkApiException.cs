using System.Net;

namespace SplunkSdk;

/// <summary>
/// Represents an unsuccessful response from the Splunk REST API.
/// </summary>
public class SplunkApiException : Exception
{
    /// <summary>
    /// Initializes a new Splunk API exception.
    /// </summary>
    /// <param name="statusCode">HTTP status code associated with the failure.</param>
    /// <param name="reasonPhrase">HTTP reason phrase, when available.</param>
    /// <param name="responseSnippet">Legacy response snippet parameter. Raw response snippets are not exposed.</param>
    /// <param name="messages">Parsed Splunk messages returned by the API or export stream.</param>
    public SplunkApiException(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        string responseSnippet,
        IReadOnlyList<SplunkMessage> messages)
        : base(CreateMessage(statusCode, reasonPhrase, messages))
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        ResponseSnippet = string.Empty;
        Messages = messages;
    }

    /// <summary>
    /// Gets the HTTP status code returned by Splunk.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the HTTP reason phrase returned by Splunk, when present.
    /// </summary>
    public string? ReasonPhrase { get; }

    /// <summary>
    /// Gets a response body snippet.
    /// </summary>
    /// <remarks>
    /// The SDK intentionally leaves this empty so exception messages and common
    /// exception logging do not persist raw SPL or event payloads.
    /// </remarks>
    public string ResponseSnippet { get; }

    /// <summary>
    /// Gets parsed Splunk response messages.
    /// </summary>
    public IReadOnlyList<SplunkMessage> Messages { get; }

    private static string CreateMessage(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        IReadOnlyList<SplunkMessage> messages)
    {
        var status = $"{(int)statusCode} {reasonPhrase ?? statusCode.ToString()}";
        if (messages.Count > 0)
        {
            var messageTypes = messages
                .Select(message => string.IsNullOrWhiteSpace(message.Type) ? "unknown" : message.Type)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return $"Splunk API request failed with {status}. Splunk returned {messages.Count} message(s) of type {string.Join(", ", messageTypes)}.";
        }

        return $"Splunk API request failed with {status}.";
    }
}
