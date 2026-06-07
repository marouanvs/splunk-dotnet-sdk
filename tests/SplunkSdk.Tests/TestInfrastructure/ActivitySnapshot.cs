using System.Diagnostics;

namespace Marouanvs.Splunk.Tests.TestInfrastructure;

internal sealed record ActivitySnapshot(
    string Name,
    ActivityStatusCode Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static ActivitySnapshot FromActivity(Activity activity) =>
        new(
            activity.DisplayName,
            activity.Status,
            activity.TagObjects.ToDictionary(
                tag => tag.Key,
                tag => tag.Value?.ToString() ?? string.Empty,
                StringComparer.Ordinal));
}
