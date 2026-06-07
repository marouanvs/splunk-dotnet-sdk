using Marouanvs.Splunk.Authentication;

namespace Marouanvs.Splunk.Tests.TestInfrastructure;

internal sealed class EmptySplunkTokenProvider : ISplunkTokenProvider
{
    public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(string.Empty);
    }
}
