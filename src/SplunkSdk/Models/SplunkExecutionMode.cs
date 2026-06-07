namespace SplunkSdk.Models;

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
    Blocking
}
