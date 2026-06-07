namespace Marouanvs.Splunk.Configuration;

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
    /// Gets or sets the upper bound of the first backoff delay.
    /// </summary>
    /// <remarks>
    /// The SDK applies full jitter to computed backoff: each wait is a uniformly
    /// random duration between zero and the exponentially increasing bound
    /// (<see cref="BaseDelay"/> doubled per attempt, capped at <see cref="MaxDelay"/>).
    /// Must be greater than zero when <see cref="MaxRetries"/> is greater than zero.
    /// </remarks>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets or sets the maximum computed backoff bound.
    /// </summary>
    /// <remarks>
    /// Must be greater than zero when <see cref="MaxRetries"/> is greater than zero,
    /// and must not be less than <see cref="BaseDelay"/>. Server-provided
    /// <c>Retry-After</c> delays may exceed this value up to <see cref="MaxServerDelay"/>.
    /// </remarks>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets the maximum server-requested retry delay the SDK honors.
    /// Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// When a retryable response carries a <c>Retry-After</c> header, the SDK honors the
    /// requested delay even when it exceeds <see cref="MaxDelay"/>, up to this limit.
    /// When the server requests a delay longer than this value, the SDK does not retry
    /// and surfaces the response error immediately.
    /// </remarks>
    public TimeSpan MaxServerDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Validates the retry settings.
    /// </summary>
    /// <exception cref="SplunkConfigurationException">A retry setting is invalid.</exception>
    public void Validate()
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

        if (MaxServerDelay < TimeSpan.Zero)
        {
            throw new SplunkConfigurationException("MaxServerDelay must be zero or greater.");
        }

        if (MaxRetries > 0 && BaseDelay <= TimeSpan.Zero)
        {
            throw new SplunkConfigurationException("BaseDelay must be greater than zero when retries are enabled.");
        }

        if (MaxRetries > 0 && MaxDelay <= TimeSpan.Zero)
        {
            throw new SplunkConfigurationException("MaxDelay must be greater than zero when retries are enabled.");
        }

        if (MaxDelay < BaseDelay)
        {
            throw new SplunkConfigurationException("MaxDelay must be greater than or equal to BaseDelay.");
        }
    }
}
