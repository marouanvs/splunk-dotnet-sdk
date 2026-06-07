namespace SplunkSdk.Models;

/// <summary>
/// Represents a dispatched Splunk search job.
/// </summary>
public sealed record SplunkSearchJob(string Sid)
{
    /// <summary>
    /// Gets the Splunk search ID.
    /// </summary>
    public string Sid { get; } = string.IsNullOrWhiteSpace(Sid)
        ? throw new ArgumentException("A Splunk search ID is required.", nameof(Sid))
        : Sid;
}
