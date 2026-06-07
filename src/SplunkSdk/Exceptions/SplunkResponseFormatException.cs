using System.Net;

namespace Marouanvs.Splunk;

/// <summary>
/// Represents a successful (2xx) Splunk REST response whose payload the SDK could not parse.
/// </summary>
/// <remarks>
/// This exception indicates a client-side parse failure — for example malformed JSON, an
/// unexpected payload shape, or a missing required field — not an HTTP-level error returned
/// by Splunk. Because the HTTP exchange itself succeeded,
/// <see cref="SplunkApiException.StatusCode"/> reports <see cref="HttpStatusCode.OK"/> with
/// the dedicated reason phrase <c>"Unparseable response"</c>. Inner exceptions carry parser
/// positions only, never response payloads.
/// </remarks>
public sealed class SplunkResponseFormatException : SplunkApiException
{
    /// <summary>
    /// Initializes a new response format exception.
    /// </summary>
    /// <param name="message">Description of the parse failure; must not contain raw response payloads.</param>
    public SplunkResponseFormatException(string message)
        : this(message, null)
    {
    }

    /// <summary>
    /// Initializes a new response format exception with the underlying parse failure.
    /// </summary>
    /// <param name="message">Description of the parse failure; must not contain raw response payloads.</param>
    /// <param name="innerException">Underlying parse exception, which carries positions but never payloads.</param>
    public SplunkResponseFormatException(string message, Exception? innerException)
        : base(message, innerException, HttpStatusCode.OK, "Unparseable response", Array.Empty<SplunkMessage>())
    {
    }
}
