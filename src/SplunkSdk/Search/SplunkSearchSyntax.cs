using System.Text;
using System.Text.RegularExpressions;

namespace Marouanvs.Splunk.Search;

/// <summary>
/// Helpers for generating small, safe SPL fragments.
/// </summary>
public static partial class SplunkSearchSyntax
{
    /// <summary>
    /// Quotes a Splunk field value using double-quoted SPL syntax.
    /// </summary>
    /// <param name="value">Literal value to quote.</param>
    /// <returns>The value wrapped in double quotes with embedded quotes and backslashes escaped.</returns>
    public static string QuoteValue(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            if (character is '"' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// Validates a single Splunk index name used in generated SPL.
    /// </summary>
    /// <param name="indexName">Candidate index name.</param>
    /// <param name="parameterName">Optional parameter name used in thrown exceptions.</param>
    /// <returns>The original index name when it is safe for generated SPL.</returns>
    /// <remarks>
    /// Wildcard index patterns are intentionally rejected. Use trusted raw SPL
    /// when an application intentionally needs patterns such as <c>team_*</c>.
    /// </remarks>
    public static string ValidateIndexName(string indexName, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(indexName))
        {
            throw new ArgumentException("A Splunk index name is required.", parameterName ?? nameof(indexName));
        }

        if (!IndexNameRegex().IsMatch(indexName))
        {
            throw new ArgumentException(
                $"'{indexName}' is not a safe single Splunk index name. Use raw SPL only for trusted advanced index patterns.",
                parameterName ?? nameof(indexName));
        }

        return indexName;
    }

    /// <summary>
    /// Validates a Splunk field name or alias used unquoted in generated SPL.
    /// </summary>
    /// <param name="fieldName">Candidate field name or alias.</param>
    /// <param name="parameterName">Optional parameter name used in thrown exceptions.</param>
    /// <returns>The original field name when it is safe unquoted.</returns>
    /// <remarks>
    /// Reserved SPL scoping tokens are rejected case-insensitively
    /// (<c>earliest</c>, <c>latest</c>, <c>index</c>, <c>splunk_server</c>,
    /// <c>splunk_server_group</c>) because using them as field names would
    /// override REST time bounds or index scoping in generated SPL. The
    /// uppercase boolean operators <c>OR</c>, <c>AND</c>, and <c>NOT</c> are
    /// rejected exactly because they change boolean logic when unquoted.
    /// Use raw SPL only for trusted advanced queries.
    /// </remarks>
    public static string ValidateFieldName(string fieldName, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("A Splunk field name is required.", parameterName ?? nameof(fieldName));
        }

        if (!FieldNameRegex().IsMatch(fieldName))
        {
            throw new ArgumentException(
                $"'{fieldName}' is not a safe unquoted SPL field name. Use raw SPL only for trusted advanced queries.",
                parameterName ?? nameof(fieldName));
        }

        if (ReservedScopingTokens.Contains(fieldName))
        {
            throw new ArgumentException(
                $"'{fieldName}' is a reserved SPL scoping token and cannot be used as a generated SPL field name because it would override search time bounds or index scoping. Use raw SPL only for trusted advanced queries.",
                parameterName ?? nameof(fieldName));
        }

        if (fieldName is "OR" or "AND" or "NOT")
        {
            throw new ArgumentException(
                $"'{fieldName}' is a reserved SPL boolean operator and cannot be used as a generated SPL field name because it would change boolean logic. Use raw SPL only for trusted advanced queries.",
                parameterName ?? nameof(fieldName));
        }

        return fieldName;
    }

    /// <summary>
    /// Validates a Splunk field name used as a REST result field projection value.
    /// </summary>
    /// <param name="fieldName">Candidate field name.</param>
    /// <param name="parameterName">Parameter name used in thrown exceptions.</param>
    /// <returns>The original field name when it is identifier-shaped.</returns>
    /// <remarks>
    /// Projection values are sent as repeated <c>f</c> REST query parameters and
    /// never enter generated SPL, so reserved SPL scoping tokens such as
    /// <c>index</c> and <c>splunk_server</c> are legitimate here: they are
    /// default fields present on every Splunk event row. Only the
    /// identifier-shape check applies.
    /// </remarks>
    internal static string ValidateResultFieldName(string fieldName, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("A Splunk field name is required.", parameterName);
        }

        if (!FieldNameRegex().IsMatch(fieldName))
        {
            throw new ArgumentException(
                $"'{fieldName}' is not a safe unquoted SPL field name. Use raw SPL only for trusted advanced queries.",
                parameterName);
        }

        return fieldName;
    }

    /// <summary>
    /// Validates a Splunk timechart span such as <c>30s</c>, <c>5m</c>, <c>1h</c>, or <c>1d</c>.
    /// </summary>
    /// <param name="span">Candidate timechart span.</param>
    /// <param name="parameterName">Optional parameter name used in thrown exceptions.</param>
    /// <returns>The original span when it is supported by the SDK's generated SPL helpers.</returns>
    public static string ValidateSpan(string span, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(span))
        {
            throw new ArgumentException("A Splunk timechart span is required.", parameterName ?? nameof(span));
        }

        if (!SpanRegex().IsMatch(span))
        {
            throw new ArgumentException($"'{span}' is not a supported timechart span.", parameterName ?? nameof(span));
        }

        return span;
    }

    // \A...\z anchors are intentional: unlike $, \z does not tolerate a trailing
    // newline, so values such as "main\n" cannot pass validation.
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex IndexNameRegex();

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_.:]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex FieldNameRegex();

    [GeneratedRegex(@"\A[1-9][0-9]*(s|sec|secs|m|min|mins|h|hr|hrs|d|day|days|w|week|weeks)\z", RegexOptions.CultureInvariant)]
    private static partial Regex SpanRegex();

    private static readonly HashSet<string> ReservedScopingTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "earliest",
        "latest",
        "index",
        "splunk_server",
        "splunk_server_group"
    };
}
