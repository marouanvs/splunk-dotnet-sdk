using System.Globalization;
using System.Text.Json;

namespace Marouanvs.Splunk.Models;

/// <summary>
/// Represents one row returned by a Splunk search result stream.
/// </summary>
/// <remarks>
/// Field values are cloned from the response JSON, so the row remains usable
/// after the HTTP response stream is disposed.
/// </remarks>
public sealed class SplunkSearchResult
{
    /// <summary>
    /// Initializes a result row.
    /// </summary>
    /// <param name="fields">Field values keyed by Splunk field name.</param>
    /// <param name="preview">Whether this row came from preview results.</param>
    /// <param name="offset">Optional Splunk result offset.</param>
    /// <param name="lastRow">Whether Splunk marked this row/frame as final.</param>
    public SplunkSearchResult(
        IReadOnlyDictionary<string, JsonElement> fields,
        bool preview = false,
        long? offset = null,
        bool lastRow = false)
    {
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        Preview = preview;
        Offset = offset;
        LastRow = lastRow;
    }

    /// <summary>
    /// Gets the row fields.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Fields { get; }

    /// <summary>
    /// Gets whether the row came from a preview result set.
    /// </summary>
    public bool Preview { get; }

    /// <summary>
    /// Gets the Splunk result offset, when supplied.
    /// </summary>
    public long? Offset { get; }

    /// <summary>
    /// Gets whether Splunk marked the row as the last row in a JSON stream.
    /// </summary>
    public bool LastRow { get; }

    /// <summary>
    /// Gets a field as a string.
    /// </summary>
    /// <param name="fieldName">Splunk field name.</param>
    /// <returns>The field value as text, or <c>null</c> when the field is missing or null.</returns>
    public string? GetString(string fieldName)
    {
        if (!Fields.TryGetValue(fieldName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            _ => value.ToString()
        };
    }

    /// <summary>
    /// Gets a field as a 64-bit integer when possible.
    /// </summary>
    /// <param name="fieldName">Splunk field name.</param>
    /// <returns>The parsed integer, or <c>null</c> when the field is missing, null, or not an integer.</returns>
    public long? GetInt64(string fieldName)
    {
        var value = GetString(fieldName);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Gets a field as a double when possible.
    /// </summary>
    /// <param name="fieldName">Splunk field name.</param>
    /// <returns>The parsed floating-point value, or <c>null</c> when the field is missing, null, or not numeric.</returns>
    public double? GetDouble(string fieldName)
    {
        var value = GetString(fieldName);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
