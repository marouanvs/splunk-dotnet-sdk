namespace SplunkSdk.Models;

/// <summary>
/// Identifies a Splunk REST namespace under <c>/servicesNS/{owner}/{app}</c>.
/// </summary>
public sealed record SplunkNamespace
{
    private SplunkNamespace(string owner, string app)
    {
        Owner = owner;
        App = app;
    }

    /// <summary>
    /// Gets the Splunk owner segment.
    /// </summary>
    public string Owner { get; }

    /// <summary>
    /// Gets the Splunk app segment.
    /// </summary>
    public string App { get; }

    /// <summary>
    /// Creates a namespace for an owner and app.
    /// </summary>
    /// <param name="owner">Splunk knowledge-object owner segment.</param>
    /// <param name="app">Splunk app segment.</param>
    /// <returns>A namespace used to build <c>/servicesNS/{owner}/{app}</c> paths.</returns>
    public static SplunkNamespace Create(string owner, string app)
    {
        ValidateSegment(owner, nameof(owner));
        ValidateSegment(app, nameof(app));
        return new SplunkNamespace(owner, app);
    }

    internal string ToPathPrefix() =>
        $"servicesNS/{Uri.EscapeDataString(Owner)}/{Uri.EscapeDataString(App)}";

    private static void ValidateSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Splunk namespace segments must not be empty.", parameterName);
        }

        if (value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException("Splunk namespace segments must not contain path separators.", parameterName);
        }
    }
}
