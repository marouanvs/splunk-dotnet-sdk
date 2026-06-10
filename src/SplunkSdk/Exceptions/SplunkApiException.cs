using System.Net;

namespace Marouanvs.Splunk;

/// <summary>
/// Represents an unsuccessful response from the Splunk REST API.
/// </summary>
/// <remarks>
/// The exception message includes the structured <c>messages</c> entries returned by the
/// Splunk server, which typically carry missing-capability and authentication reasons, so
/// standard <see cref="Exception.Message"/> logging preserves the diagnostic. Raw response
/// bodies, SPL text, and event payloads are never included.
/// </remarks>
public class SplunkApiException : Exception
{
    /// <summary>
    /// Initializes a new Splunk API exception.
    /// </summary>
    /// <param name="statusCode">HTTP status code associated with the failure.</param>
    /// <param name="reasonPhrase">HTTP reason phrase, when available.</param>
    /// <param name="messages">Parsed Splunk messages returned by the API or export stream.</param>
    public SplunkApiException(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        IReadOnlyList<SplunkMessage> messages)
        : base(CreateMessage(statusCode, reasonPhrase, messages))
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        Messages = messages;
    }

    /// <summary>
    /// Initializes a new Splunk API exception with a caller-provided message.
    /// </summary>
    /// <param name="message">Exception message; must not contain raw response payloads.</param>
    /// <param name="innerException">Underlying exception, when available.</param>
    /// <param name="statusCode">HTTP status code associated with the failure.</param>
    /// <param name="reasonPhrase">HTTP reason phrase, when available.</param>
    /// <param name="messages">Parsed Splunk messages returned by the API or export stream.</param>
    private protected SplunkApiException(
        string message,
        Exception? innerException,
        HttpStatusCode statusCode,
        string? reasonPhrase,
        IReadOnlyList<SplunkMessage> messages)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
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
    /// Gets parsed Splunk response messages.
    /// </summary>
    public IReadOnlyList<SplunkMessage> Messages { get; }

    private static string CreateMessage(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        IReadOnlyList<SplunkMessage> messages)
    {
        var status = $"{(int)statusCode} {reasonPhrase ?? statusCode.ToString()}";
        if (messages.Count == 0)
        {
            return $"Splunk API request failed with {status}.";
        }

        var details = string.Join("; ", messages.Select(message => message.ToString()));
        return $"Splunk API request failed with {status}. Splunk messages: {details}";
    }
}
