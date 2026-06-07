using SplunkSdk.Models;

namespace SplunkSdk.Search;

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
    /// Adds a literal text predicate.
    /// </summary>
    /// <param name="text">Text to search as a quoted SPL literal.</param>
    /// <returns>The same builder for chaining.</returns>
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
    /// Adds a safe field equality predicate.
    /// </summary>
    /// <param name="field">Safe unquoted Splunk field name.</param>
    /// <param name="value">Value to compare using quoted SPL literal syntax.</param>
    /// <returns>The same builder for chaining.</returns>
    public SplunkQueryBuilder FieldEquals(string field, string value)
    {
        _predicates.Add($"{SplunkSearchSyntax.ValidateFieldName(field, nameof(field))}={SplunkSearchSyntax.QuoteValue(value)}");
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
