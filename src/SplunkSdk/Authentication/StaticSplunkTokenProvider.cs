namespace Marouanvs.Splunk.Authentication;

/// <summary>
/// Token provider for deployments where the caller already has a static Splunk token.
/// </summary>
public sealed class StaticSplunkTokenProvider : ISplunkTokenProvider
{
    private readonly string _token;

    /// <summary>
    /// Initializes a new static token provider.
    /// </summary>
    /// <param name="token">The full Splunk token. The value is retained in memory and never logged by the SDK.</param>
    /// <remarks>
    /// This provider is convenient for examples and simple tools. Production
    /// services should normally implement <see cref="ISplunkTokenProvider"/> so
    /// tokens can come from a secret store or rotation service.
    /// </remarks>
    public StaticSplunkTokenProvider(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("A Splunk token is required.", nameof(token));
        }

        _token = token;
    }

    /// <inheritdoc />
    public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_token);
    }
}
