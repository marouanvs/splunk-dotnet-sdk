namespace SplunkSdk.Mapping;

/// <summary>
/// Represents a failure while mapping a Splunk result row to a .NET object.
/// </summary>
public sealed class SplunkMappingException : Exception
{
    /// <summary>
    /// Initializes a new mapping exception.
    /// </summary>
    /// <param name="message">Mapping error message.</param>
    public SplunkMappingException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new mapping exception.
    /// </summary>
    /// <param name="message">Mapping error message.</param>
    /// <param name="innerException">Underlying conversion exception.</param>
    public SplunkMappingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
