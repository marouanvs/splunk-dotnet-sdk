namespace Marouanvs.Splunk.Models;

/// <summary>
/// Search REST API generation used by the SDK.
/// </summary>
public enum SplunkSearchApiVersion
{
    /// <summary>
    /// Uses legacy v1-compatible endpoints such as <c>/services/search/jobs/export</c>.
    /// </summary>
    V1,

    /// <summary>
    /// Uses semantic v2 endpoints such as <c>/services/search/v2/jobs/export</c>.
    /// </summary>
    V2
}
