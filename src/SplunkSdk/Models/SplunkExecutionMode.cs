namespace Marouanvs.Splunk.Models;

/// <summary>
/// Search dispatch mode for the Splunk <c>exec_mode</c> parameter.
/// </summary>
public enum SplunkExecutionMode
{
    /// <summary>
    /// Dispatches the job and returns the search ID immediately.
    /// </summary>
    Normal,

    /// <summary>
    /// Blocks until the job is complete and then returns the search ID.
    /// </summary>
    Blocking,

    /// <summary>
    /// Runs the search synchronously and returns the final results directly in
    /// the dispatch response instead of a search ID.
    /// </summary>
    /// <remarks>
    /// Use <c>ISplunkSearchClient.OneshotSearchAsync</c> for this mode. It is
    /// rejected by <c>StartAsync</c> because a oneshot dispatch response
    /// contains results rather than a search ID.
    /// </remarks>
    Oneshot
}
