namespace Marouanvs.Splunk.Models;

/// <summary>
/// Represents a point-in-time snapshot of a Splunk search job's lifecycle state.
/// </summary>
/// <remarks>
/// Values are parsed defensively from the Splunk job status response: fields
/// that are missing or have unexpected JSON types are surfaced as <c>null</c>
/// or their documented defaults rather than failing the whole status call.
/// </remarks>
public sealed record SplunkSearchJobStatus(string Sid)
{
    /// <summary>
    /// Gets the Splunk search ID.
    /// </summary>
    public string Sid { get; } = string.IsNullOrWhiteSpace(Sid)
        ? throw new ArgumentException("A Splunk search ID is required.", nameof(Sid))
        : Sid;

    /// <summary>
    /// Gets whether the job has finished. Defaults to <see langword="false"/>
    /// when Splunk omits the flag.
    /// </summary>
    public bool IsDone { get; init; }

    /// <summary>
    /// Gets whether the job reached a failed state. Defaults to
    /// <see langword="false"/> when Splunk omits the flag.
    /// </summary>
    public bool IsFailed { get; init; }

    /// <summary>
    /// Gets the Splunk dispatch state, such as <c>QUEUED</c>, <c>RUNNING</c>,
    /// <c>DONE</c>, or <c>FAILED</c>, when supplied.
    /// </summary>
    public string? DispatchState { get; init; }

    /// <summary>
    /// Gets the job completion progress between <c>0</c> and <c>1</c>, when supplied.
    /// </summary>
    public double? DoneProgress { get; init; }

    /// <summary>
    /// Gets the number of events matched so far, when supplied.
    /// </summary>
    public long? EventCount { get; init; }

    /// <summary>
    /// Gets the number of result rows produced so far, when supplied.
    /// </summary>
    public long? ResultCount { get; init; }
}
