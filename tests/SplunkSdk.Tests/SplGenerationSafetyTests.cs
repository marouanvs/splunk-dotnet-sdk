using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Search;
using Xunit;

namespace Marouanvs.Splunk.Tests;

public sealed class SplGenerationSafetyTests
{
    [Theory]
    [InlineData("earliest")]
    [InlineData("EARLIEST")]
    [InlineData("latest")]
    [InlineData("Latest")]
    [InlineData("index")]
    [InlineData("INDEX")]
    [InlineData("splunk_server")]
    [InlineData("SPLUNK_SERVER")]
    [InlineData("splunk_server_group")]
    [InlineData("Splunk_Server_Group")]
    public Task ReservedScopingTokensAreRejectedCaseInsensitivelyAsFieldNames(string fieldName)
    {
        var exception = Assert.Throws<ArgumentException>(() => SplunkSearchSyntax.ValidateFieldName(fieldName));

        Assert.Equal("fieldName", exception.ParamName);
        Assert.StartsWith(
            $"'{fieldName}' is a reserved SPL scoping token and cannot be used as a generated SPL field name because it would override search time bounds or index scoping.",
            exception.Message,
            StringComparison.Ordinal);

        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("OR")]
    [InlineData("AND")]
    [InlineData("NOT")]
    public Task UppercaseBooleanOperatorsAreRejectedAsFieldNames(string fieldName)
    {
        var exception = Assert.Throws<ArgumentException>(() => SplunkSearchSyntax.ValidateFieldName(fieldName));

        Assert.Equal("fieldName", exception.ParamName);
        Assert.StartsWith(
            $"'{fieldName}' is a reserved SPL boolean operator and cannot be used as a generated SPL field name because it would change boolean logic.",
            exception.Message,
            StringComparison.Ordinal);

        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("source")]
    [InlineData("sourcetype")]
    [InlineData("host")]
    [InlineData("duration_ms")]
    [InlineData("_time")]
    [InlineData("event.outcome")]
    [InlineData("vendor:product:field")]
    [InlineData("or")]
    [InlineData("and")]
    [InlineData("not")]
    public Task LegitimateFieldNamesRemainAccepted(string fieldName)
    {
        // Only the uppercase forms are SPL boolean operators, so lowercase
        // "or"/"and"/"not" stay valid field names by contract.
        Assert.Equal(fieldName, SplunkSearchSyntax.ValidateFieldName(fieldName));
        return Task.CompletedTask;
    }

    [Fact]
    public Task ReservedTokensAreRejectedAcrossQueryBuilderSurfaces()
    {
        var builder = SplunkQueryBuilder.FromIndex("team");

        Assert.Equal("field", Assert.Throws<ArgumentException>(() => builder.FieldEquals("earliest", "-1h")).ParamName);
        Assert.Equal("field", Assert.Throws<ArgumentException>(() => builder.FieldMatchesWildcard("latest", "now*")).ParamName);
        Assert.Equal("alias", Assert.Throws<ArgumentException>(() => builder.StatsCount("INDEX")).ParamName);
        Assert.Equal("field", Assert.Throws<ArgumentException>(() => builder.StatsAverage("splunk_server", "average_value")).ParamName);
        Assert.Equal("alias", Assert.Throws<ArgumentException>(() => builder.TimechartAverage("5m", "duration_ms", "NOT")).ParamName);

        // Failed validation must not leak partial predicates or commands into the output.
        Assert.Equal("search index=\"team\"", builder.Build());
        return Task.CompletedTask;
    }

    [Fact]
    public Task TrailingNewlineValuesAreRejectedByEveryValidator()
    {
        var field = Assert.Throws<ArgumentException>(() => SplunkSearchSyntax.ValidateFieldName("field\n"));
        Assert.Equal("fieldName", field.ParamName);
        Assert.StartsWith("'field\n' is not a safe unquoted SPL field name.", field.Message, StringComparison.Ordinal);

        var index = Assert.Throws<ArgumentException>(() => SplunkSearchSyntax.ValidateIndexName("index\n"));
        Assert.Equal("indexName", index.ParamName);
        Assert.StartsWith("'index\n' is not a safe single Splunk index name.", index.Message, StringComparison.Ordinal);

        var span = Assert.Throws<ArgumentException>(() => SplunkSearchSyntax.ValidateSpan("5m\n"));
        Assert.Equal("span", span.ParamName);
        Assert.StartsWith("'5m\n' is not a supported timechart span.", span.Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => SplunkQueryBuilder.FromIndex("main\n"));
        Assert.Throws<ArgumentException>(() =>
            SplunkQueryBuilder.FromIndex("main").FieldEquals("duration_ms\n", "10"));
        Assert.Throws<ArgumentException>(() =>
            SplunkQueryBuilder.FromIndex("main").TimechartAverage("5m\n", "duration_ms", "average_value"));

        // The same values without the trailing newline stay valid.
        Assert.Equal("field", SplunkSearchSyntax.ValidateFieldName("field"));
        Assert.Equal("main", SplunkSearchSyntax.ValidateIndexName("main"));
        Assert.Equal("5m", SplunkSearchSyntax.ValidateSpan("5m"));

        return Task.CompletedTask;
    }

    [Fact]
    public Task FieldEqualsRejectsWildcardAndNullValues()
    {
        var builder = SplunkQueryBuilder.FromIndex("team");

        foreach (var value in new[] { "billing*", "billing?", "*", "?" })
        {
            var exception = Assert.Throws<ArgumentException>(() => builder.FieldEquals("service", value));
            Assert.Equal("value", exception.ParamName);
            Assert.StartsWith(
                "Field equality values are literal and must not contain the SPL wildcard characters '*' or '?'. Use FieldMatchesWildcard for intentional wildcard matching.",
                exception.Message,
                StringComparison.Ordinal);
        }

        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => builder.FieldEquals("service", null!)).ParamName);

        // Rejected values must not leak partial predicates into the output.
        Assert.Equal("search index=\"team\"", builder.Build());
        return Task.CompletedTask;
    }

    [Fact]
    public Task FieldMatchesWildcardEmitsValidatedFieldWithQuotedPattern()
    {
        var search = SplunkQueryBuilder.FromIndex("team")
            .FieldMatchesWildcard("service", "billing-*")
            .FieldMatchesWildcard("host", "web-?")
            .Build();

        Assert.Equal("search index=\"team\" service=\"billing-*\" host=\"web-?\"", search);

        var emptyPattern = Assert.Throws<ArgumentException>(() =>
            SplunkQueryBuilder.FromIndex("team").FieldMatchesWildcard("service", " "));
        Assert.Equal("pattern", emptyPattern.ParamName);
        Assert.StartsWith("A wildcard pattern must not be empty.", emptyPattern.Message, StringComparison.Ordinal);

        var unsafeField = Assert.Throws<ArgumentException>(() =>
            SplunkQueryBuilder.FromIndex("team").FieldMatchesWildcard("service | delete", "billing-*"));
        Assert.Equal("field", unsafeField.ParamName);

        return Task.CompletedTask;
    }

    [Fact]
    public Task QuoteValueEscapesQuotesAndBackslashesKeepingInjectionPayloadsInert()
    {
        Assert.Equal("\"a\\\" | delete index=x\"", SplunkSearchSyntax.QuoteValue("a\" | delete index=x"));
        Assert.Equal("\"path\\\\to\\\\file\"", SplunkSearchSyntax.QuoteValue("path\\to\\file"));
        Assert.Equal("\"\"", SplunkSearchSyntax.QuoteValue(string.Empty));
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => SplunkSearchSyntax.QuoteValue(null!)).ParamName);

        // The injection payload stays inside the quoted value: the embedded quote is
        // escaped, so the pipe and index override never become SPL syntax.
        var search = SplunkQueryBuilder.FromIndex("team")
            .FieldEquals("service", "a\" | delete index=x")
            .Build();

        Assert.Equal("search index=\"team\" service=\"a\\\" | delete index=x\"", search);
        return Task.CompletedTask;
    }

    [Fact]
    public Task IndexNameValidationRejectsWildcardsAndUnsafePatterns()
    {
        foreach (var indexName in new[] { "*", "team_*", "team?", "team index", "team|index", "team\"index" })
        {
            var exception = Assert.Throws<ArgumentException>(() => SplunkSearchSyntax.ValidateIndexName(indexName));
            Assert.Equal("indexName", exception.ParamName);
            Assert.StartsWith(
                $"'{indexName}' is not a safe single Splunk index name. Use raw SPL only for trusted advanced index patterns.",
                exception.Message,
                StringComparison.Ordinal);
        }

        Assert.Equal("team-prod_01", SplunkSearchSyntax.ValidateIndexName("team-prod_01"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task RawPredicateIsEmittedVerbatimAndRejectsEmptyInput()
    {
        var search = SplunkQueryBuilder.FromIndex("team")
            .RawPredicate("sourcetype=access_* OR sourcetype=proxy")
            .StatsCount("error_count")
            .Build();

        Assert.Equal(
            "search index=\"team\" sourcetype=access_* OR sourcetype=proxy | stats count AS error_count",
            search);

        var empty = Assert.Throws<ArgumentException>(() => SplunkQueryBuilder.FromIndex("team").RawPredicate(" "));
        Assert.Equal("rawPredicate", empty.ParamName);
        Assert.StartsWith("Raw predicates must not be empty.", empty.Message, StringComparison.Ordinal);

        return Task.CompletedTask;
    }
}
