using Marouanvs.Splunk.Mapping;

namespace Marouanvs.Splunk.Tests.TestInfrastructure;

internal sealed class MetricRow
{
    [SplunkField("service")]
    public string? Service { get; set; }

    [SplunkField("error_count")]
    public long ErrorCount { get; set; }

    [SplunkField("average_value")]
    public double? Average { get; set; }

    [SplunkField("is_enabled")]
    public bool IsEnabled { get; set; }

    [SplunkField("observed_at")]
    public DateTimeOffset? ObservedAt { get; set; }
}
