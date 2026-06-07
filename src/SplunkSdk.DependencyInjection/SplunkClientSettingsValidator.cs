using Microsoft.Extensions.Options;
using SplunkSdk.Configuration;

namespace SplunkSdk.DependencyInjection;

internal sealed class SplunkClientSettingsValidator : IValidateOptions<SplunkClientSettings>
{
    public ValidateOptionsResult Validate(string? name, SplunkClientSettings options)
    {
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
