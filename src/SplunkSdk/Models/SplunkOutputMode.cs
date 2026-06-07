namespace Marouanvs.Splunk.Models;

/// <summary>
/// Output modes supported by Splunk search result endpoints.
/// </summary>
public enum SplunkOutputMode
{
    /// <summary>
    /// JSON output. This is the SDK default.
    /// </summary>
    Json,

    /// <summary>
    /// Column-oriented JSON output.
    /// </summary>
    JsonColumns,

    /// <summary>
    /// Row-oriented JSON output.
    /// </summary>
    JsonRows,

    /// <summary>
    /// CSV output.
    /// </summary>
    Csv,

    /// <summary>
    /// Raw events.
    /// </summary>
    Raw,

    /// <summary>
    /// XML output.
    /// </summary>
    Xml
}
