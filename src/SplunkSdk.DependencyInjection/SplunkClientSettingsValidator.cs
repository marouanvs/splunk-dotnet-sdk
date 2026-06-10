using Microsoft.Extensions.Options;
using Marouanvs.Splunk.Configuration;

namespace Marouanvs.Splunk.DependencyInjection;

/// <summary>
/// Startup validator for <see cref="SplunkClientSettings"/> instances registered
/// through <see cref="SplunkServiceCollectionExtensions"/>.
/// </summary>
/// <remarks>
/// Validation reuses the same <c>ToClientOptions</c> mapping used at runtime so
/// startup validation and client construction cannot drift. Only options names
/// registered by <c>AddSplunkClient</c> (the default name and explicit logical
/// names) are validated; any other named <see cref="SplunkClientSettings"/>
/// instance created by the host is skipped.
/// </remarks>
internal sealed class SplunkClientSettingsValidator : IValidateOptions<SplunkClientSettings>
{
    private readonly HashSet<string> _registeredNames;

    public SplunkClientSettingsValidator(IEnumerable<SplunkClientRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _registeredNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            _registeredNames.Add(registration.Name);
        }
    }

    public ValidateOptionsResult Validate(string? name, SplunkClientSettings options)
    {
        if (!_registeredNames.Contains(name ?? Options.DefaultName))
        {
            return ValidateOptionsResult.Skip;
        }

        try
        {
            _ = options.ToClientOptions();
            return ValidateOptionsResult.Success;
        }
        catch (SplunkConfigurationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}
