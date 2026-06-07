using System.Net;
using Marouanvs.Splunk.Mapping;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class MappingTests
{
    [Fact]
    public Task TypedMaterializationMapsSplunkRows()
    {
        var row = CreateRow("""
        {
          "service": "checkout",
          "error_count": "42",
          "average_value": "123.45",
          "is_enabled": "1",
          "observed_at": "2026-06-04T21:30:00Z"
        }
        """);

        var mapped = row.ToObject<MetricRow>();

        Assert.Equal("checkout", mapped.Service);
        Assert.Equal(42L, mapped.ErrorCount);
        Assert.Equal(123.45, mapped.Average);
        Assert.True(mapped.IsEnabled, "Expected numeric booleans to map to bool.");
        Assert.Equal(DateTimeOffset.Parse("2026-06-04T21:30:00Z", System.Globalization.CultureInfo.InvariantCulture), mapped.ObservedAt);

        Assert.Throws<SplunkMappingException>(() =>
            CreateRow("""{"error_count":"not-a-number"}""").ToObject<MetricRow>());

        return Task.CompletedTask;
    }

    [Fact]
    public Task TypedMaterializationRejectsNullForNonNullableValueTypes()
    {
        var exception = Assert.Throws<SplunkMappingException>(() =>
            CreateRow("""{"error_count":null}""").ToObject<MetricRow>());

        Assert.Contains("non-nullable", exception.Message, StringComparison.Ordinal);

        var mapped = CreateRow("""{"average_value":null}""").ToObject<MetricRow>();

        Assert.Null(mapped.Average);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TypedMaterializationMapsMultiValueFieldsToCollections()
    {
        var row = CreateRow("""
        {
          "service": "checkout",
          "users": ["alice", "bob"],
          "durations": ["10", "20"]
        }
        """);

        var mapped = row.ToObject<MultiValueMetricRow>();

        Assert.Equal(new[] { "alice", "bob" }, mapped.Users);
        Assert.Equal(new[] { 10L, 20L }, mapped.Durations);
        return Task.CompletedTask;
    }

    [Fact]
    public Task MappingExceptionsDoNotEchoRawFieldValues()
    {
        var exception = Assert.Throws<SplunkMappingException>(() =>
            CreateRow("""{"error_count":"not-a-number"}""").ToObject<MetricRow>());

        Assert.DoesNotContain("not-a-number", exception.Message, StringComparison.Ordinal);

        var multiValueException = Assert.Throws<SplunkMappingException>(() =>
            CreateRow("""{"service":["alice","bob"]}""").ToObject<MetricRow>());

        Assert.DoesNotContain("alice", multiValueException.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("bob", multiValueException.Message, StringComparison.Ordinal);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AsyncTypedMaterializationMapsExportRows()
    {
        var handler = new QueueHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, """
        {"preview":false,"offset":0,"result":{"service":"checkout","error_count":"3","average_value":"10.5","is_enabled":"true"}}
        {"preview":false,"offset":1,"result":{"service":"billing","error_count":"4","average_value":"11.5","is_enabled":"false"}}
        """);

        using var client = CreateClient(handler);

        var rows = await client.Search
            .ExportAsync(new SplunkSearchRequest("search index=\"team\" | stats count AS error_count"))
            .ToObjectsAsync<MetricRow>()
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal("checkout", rows[0].Service);
        Assert.Equal(4L, rows[1].ErrorCount);
        Assert.False(rows[1].IsEnabled);
        handler.AssertNoPendingResponses();
    }
}
