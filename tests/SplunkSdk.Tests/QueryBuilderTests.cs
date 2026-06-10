using Marouanvs.Splunk.Search;
using Xunit;

namespace Marouanvs.Splunk.Tests;

public sealed class QueryBuilderTests
{
    [Fact]
    public Task QueryBuilderRejectsUnsafeFieldNames()
    {
        Assert.Throws<ArgumentException>(() =>
            SplunkQueryBuilder.FromIndex("team").FieldEquals("host | delete", "api-01"));

        var search = SplunkQueryBuilder.FromIndex("team")
            .SearchText("quoted \"value\"")
            .FieldEquals("service", "billing")
            .StatsAverage("duration_ms", "avg_duration")
            .Build();

        Assert.Equal(
            "search index=\"team\" \"quoted \\\"value\\\"\" service=\"billing\" | stats avg(duration_ms) AS avg_duration",
            search);

        return Task.CompletedTask;
    }

    [Fact]
    public Task QueryBuilderRejectsWildcardIndexScopes()
    {
        Assert.Throws<ArgumentException>(() => SplunkQueryBuilder.FromIndex("*"));
        Assert.Throws<ArgumentException>(() => SplunkQueryBuilder.FromIndex("team_*"));
        Assert.Throws<ArgumentException>(() => SplunkQueryBuilder.FromIndex("team?"));

        var search = SplunkQueryBuilder.FromIndex("team-prod_01")
            .StatsCount("event_count")
            .Build();

        Assert.Equal("search index=\"team-prod_01\" | stats count AS event_count", search);
        return Task.CompletedTask;
    }

    [Fact]
    public Task QueryBuilderRejectsUnsafeAggregateIdentifiers()
    {
        Assert.Throws<ArgumentException>(() =>
            SplunkQueryBuilder.FromIndex("team").StatsAverage("duration-ms", "avg_duration"));
        Assert.Throws<ArgumentException>(() =>
            SplunkQueryBuilder.FromIndex("team").StatsAverage("duration_ms", "avg-duration"));
        Assert.Throws<ArgumentException>(() =>
            SplunkQueryBuilder.FromIndex("team").TimechartAverage("5m", "duration-ms", "avg_duration"));

        return Task.CompletedTask;
    }
}
