using Marouanvs.Splunk.Models;

namespace Marouanvs.Splunk.Search;

/// <summary>
/// Fluent builder for common generated SPL searches.
/// </summary>
/// <remarks>
/// This builder is intended for user- or configuration-driven inputs that need
/// safe SPL assembly. It scopes every generated search to one literal index and
/// validates field names, aggregate aliases, and timechart spans before emitting
/// SPL. Use <see cref="SplunkSearchRequest"/> directly for full trusted SPL.
/// </remarks>
public sealed class SplunkQueryBuilder
{
    private static readonly char[] WildcardCharacters = ['*', '?'];

    private readonly List<string> _predicates = [];
    private readonly List<string> _commands = [];

    private SplunkQueryBuilder(string index)
    {
        _predicates.Add($"index={SplunkSearchSyntax.QuoteValue(SplunkSearchSyntax.ValidateIndexName(index, nameof(index)))}");
    }

    /// <summary>
    /// Starts a search scoped to one index.
    /// </summary>
    /// <param name="index">Single literal Splunk index name. Wildcards are rejected.</param>
    /// <returns>A new query builder.</returns>
    public static SplunkQueryBuilder FromIndex(string index) => new(index);

    /// <summary>
    /// Adds a quoted free-text predicate.
    /// </summary>
    /// <param name="text">Text to search as a quoted SPL term.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// The text is quoted with embedded quotes and backslashes escaped, but the
    /// SPL <c>search</c> command still applies wildcard semantics inside quoted
    /// terms: <c>*</c> matches any characters and <c>?</c> matches one
    /// character, and neither can be escaped in this context. Free-text search
    /// legitimately uses wildcards, so they are intentionally not rejected
    /// here. Use <see cref="FieldEquals"/> for literal field comparisons.
    /// </remarks>
    public SplunkQueryBuilder SearchText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Search text must not be empty.", nameof(text));
        }

        _predicates.Add(SplunkSearchSyntax.QuoteValue(text));
        return this;
    }

    /// <summary>
    /// Adds a literal field equality predicate.
    /// </summary>
    /// <param name="field">Safe unquoted Splunk field name.</param>
    /// <param name="value">Literal value to compare using quoted SPL syntax.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// Values containing the SPL wildcard characters <c>*</c> or <c>?</c> are
    /// rejected because wildcards inside quoted values still match in the SPL
    /// <c>search</c> command and cannot be escaped, which would break the
    /// literal-equality contract of this method. Use
    /// <see cref="FieldMatchesWildcard"/> for intentional wildcard matching.
    /// </remarks>
    public SplunkQueryBuilder FieldEquals(string field, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.IndexOfAny(WildcardCharacters) >= 0)
        {
            throw new ArgumentException(
                "Field equality values are literal and must not contain the SPL wildcard characters '*' or '?'. Use FieldMatchesWildcard for intentional wildcard matching.",
                nameof(value));
        }

        _predicates.Add($"{SplunkSearchSyntax.ValidateFieldName(field, nameof(field))}={SplunkSearchSyntax.QuoteValue(value)}");
        return this;
    }

    /// <summary>
    /// Adds a field predicate that intentionally uses SPL wildcard matching.
    /// </summary>
    /// <param name="field">Safe unquoted Splunk field name.</param>
    /// <param name="pattern">Quoted SPL pattern where <c>*</c> matches any characters and <c>?</c> matches one character.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// The pattern is quoted with embedded quotes and backslashes escaped, but
    /// wildcard characters keep their SPL matching semantics. Only use this
    /// method when wildcard matching is intended; use <see cref="FieldEquals"/>
    /// for literal comparisons of user-provided values.
    /// </remarks>
    public SplunkQueryBuilder FieldMatchesWildcard(string field, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("A wildcard pattern must not be empty.", nameof(pattern));
        }

        _predicates.Add($"{SplunkSearchSyntax.ValidateFieldName(field, nameof(field))}={SplunkSearchSyntax.QuoteValue(pattern)}");
        return this;
    }

    /// <summary>
    /// Adds trusted raw SPL before the first pipe.
    /// </summary>
    /// <param name="rawPredicate">Trusted predicate fragment inserted after the index predicate.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// Use only with SPL owned by the application or a trusted team. The SDK cannot sanitize raw SPL.
    /// </remarks>
    public SplunkQueryBuilder RawPredicate(string rawPredicate)
    {
        if (string.IsNullOrWhiteSpace(rawPredicate))
        {
            throw new ArgumentException("Raw predicates must not be empty.", nameof(rawPredicate));
        }

        _predicates.Add(rawPredicate);
        return this;
    }

    /// <summary>
    /// Appends <c>| stats count AS alias</c>.
    /// </summary>
    /// <param name="alias">Safe unquoted output field alias.</param>
    /// <returns>The same builder for chaining.</returns>
    public SplunkQueryBuilder StatsCount(string alias)
    {
        _commands.Add($"stats count AS {SplunkSearchSyntax.ValidateFieldName(alias, nameof(alias))}");
        return this;
    }

    /// <summary>
    /// Appends <c>| stats avg(field) AS alias</c>.
    /// </summary>
    /// <param name="field">Safe unquoted numeric field name to aggregate.</param>
    /// <param name="alias">Safe unquoted output field alias.</param>
    /// <returns>The same builder for chaining.</returns>
    public SplunkQueryBuilder StatsAverage(string field, string alias)
    {
        _commands.Add($"stats avg({SplunkSearchSyntax.ValidateFieldName(field, nameof(field))}) AS {SplunkSearchSyntax.ValidateFieldName(alias, nameof(alias))}");
        return this;
    }

    /// <summary>
    /// Appends <c>| timechart span=... avg(field) AS alias</c>.
    /// </summary>
    /// <param name="span">Timechart span such as <c>30s</c>, <c>5m</c>, or <c>1h</c>.</param>
    /// <param name="field">Safe unquoted numeric field name to aggregate.</param>
    /// <param name="alias">Safe unquoted output field alias.</param>
    /// <returns>The same builder for chaining.</returns>
    public SplunkQueryBuilder TimechartAverage(string span, string field, string alias)
    {
        _commands.Add($"timechart span={SplunkSearchSyntax.ValidateSpan(span, nameof(span))} avg({SplunkSearchSyntax.ValidateFieldName(field, nameof(field))}) AS {SplunkSearchSyntax.ValidateFieldName(alias, nameof(alias))}");
        return this;
    }

    /// <summary>
    /// Builds the SPL search string.
    /// </summary>
    /// <returns>A complete SPL search beginning with <c>search index="..."</c>.</returns>
    public string Build()
    {
        var search = "search " + string.Join(' ', _predicates);

        foreach (var command in _commands)
        {
            search += " | " + command;
        }

        return search;
    }
}
