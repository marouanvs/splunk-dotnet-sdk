namespace SplunkSdk;

/// <summary>
/// Represents invalid SDK configuration.
/// </summary>
public sealed class SplunkConfigurationException : Exception
{
    /// <summary>
    /// Initializes a new configuration exception.
    /// </summary>
    /// <param name="message">Configuration error message safe to return to the caller.</param>
    public SplunkConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new configuration exception with the underlying cause.
    /// </summary>
    /// <param name="message">Configuration error message safe to return to the caller.</param>
    /// <param name="innerException">Underlying exception raised by the platform or HTTP stack.</param>
    public SplunkConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
