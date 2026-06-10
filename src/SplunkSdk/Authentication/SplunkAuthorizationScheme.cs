namespace Marouanvs.Splunk.Authentication;

/// <summary>
/// Authentication header schemes supported by Splunk token authentication.
/// </summary>
public enum SplunkAuthorizationScheme
{
    /// <summary>
    /// Uses <c>Authorization: Bearer &lt;token&gt;</c>. This is the default for Splunk JWT tokens.
    /// </summary>
    Bearer,

    /// <summary>
    /// Uses <c>Authorization: Splunk &lt;token&gt;</c>. Some Splunk app endpoints require this scheme.
    /// </summary>
    Splunk
}
