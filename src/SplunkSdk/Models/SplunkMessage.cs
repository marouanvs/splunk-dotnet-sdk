namespace SplunkSdk;

/// <summary>
/// Message returned by the Splunk REST API.
/// </summary>
public sealed record SplunkMessage(string Type, string Text)
{
    /// <inheritdoc />
    public override string ToString() => string.IsNullOrWhiteSpace(Type) ? Text : $"{Type}: {Text}";
}
