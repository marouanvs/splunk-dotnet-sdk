namespace Marouanvs.Splunk.Tests.TestInfrastructure;

internal sealed record MeasurementSnapshot(
    string Name,
    double Value,
    IReadOnlyDictionary<string, string> Tags)
{
    public static MeasurementSnapshot FromMeasurement<T>(
        string name,
        T value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct
    {
        var copiedTags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            copiedTags[tag.Key] = tag.Value?.ToString() ?? string.Empty;
        }

        return new MeasurementSnapshot(name, Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), copiedTags);
    }
}
