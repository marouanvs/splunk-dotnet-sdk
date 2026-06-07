namespace SplunkSdk.Configuration;

/// <summary>
/// Retry settings for transient HTTP and transport failures.
/// </summary>
/// <remarks>
/// Retries apply to HTTP <c>429</c>, selected <c>5xx</c> responses,
/// <see cref="HttpRequestException"/>, and client-side timeouts that are not
/// caused by caller cancellation.
/// </remarks>
public sealed class SplunkRetryOptions
{
    /// <summary>
    /// Gets or sets the number of retry attempts after the first failed request.
    /// </summary>
    /// <remarks>
    /// A value of <c>0</c> disables SDK retries and is recommended when retries
    /// are handled by the hosting application.
    /// </remarks>
    public int MaxRetries { get; init; } = 2;

    /// <summary>
    /// Gets or sets the first backoff delay.
    /// </summary>
    /// <remarks>
    /// Must be greater than zero when <see cref="MaxRetries"/> is greater than zero.
    /// </remarks>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets the maximum computed backoff delay.
    /// </summary>
    /// <remarks>
    /// Must be greater than zero when <see cref="MaxRetries"/> is greater than zero.
    /// </remarks>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(2);

    internal void Validate()
    {
        if (MaxRetries < 0)
        {
            throw new SplunkConfigurationException("MaxRetries must be zero or greater.");
        }

        if (BaseDelay < TimeSpan.Zero)
        {
            throw new SplunkConfigurationException("BaseDelay must be zero or greater.");
        }

        if (MaxDelay < TimeSpan.Zero)
        {
            throw new SplunkConfigurationException("MaxDelay must be zero or greater.");
        }

        if (MaxRetries > 0 && BaseDelay <= TimeSpan.Zero)
        {
            throw new SplunkConfigurationException("BaseDelay must be greater than zero when retries are enabled.");
        }

        if (MaxRetries > 0 && MaxDelay <= TimeSpan.Zero)
        {
            throw new SplunkConfigurationException("MaxDelay must be greater than zero when retries are enabled.");
        }
    }
}
