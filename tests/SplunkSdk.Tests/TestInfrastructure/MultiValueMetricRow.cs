using Marouanvs.Splunk.Mapping;

namespace Marouanvs.Splunk.Tests.TestInfrastructure;

internal sealed class MultiValueMetricRow
{
    [SplunkField("users")]
    public IReadOnlyList<string>? Users { get; set; }

    [SplunkField("durations")]
    public long[]? Durations { get; set; }
}
