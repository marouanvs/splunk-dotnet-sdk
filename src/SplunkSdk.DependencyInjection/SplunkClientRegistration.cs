namespace Marouanvs.Splunk.DependencyInjection;

/// <summary>
/// Marker service that records a logical Splunk client name registered through
/// <see cref="SplunkServiceCollectionExtensions"/>.
/// </summary>
/// <remarks>
/// Used to fail loudly when <c>AddSplunkClient</c> is called twice for the
/// same logical name, and to limit <see cref="SplunkClientSettingsValidator"/>
/// to options names registered by the SDK.
/// </remarks>
internal sealed class SplunkClientRegistration
{
    public SplunkClientRegistration(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>
    /// Gets the logical client name. Empty for the default registration.
    /// </summary>
    public string Name { get; }
}
