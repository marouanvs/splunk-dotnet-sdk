using Marouanvs.Splunk.Mapping;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class ResultMapperEdgeCaseTests
{
    [Fact]
    public void DateTimeValuesMapToUtcKindPreservingTheInstant()
    {
        var withOffset = CreateRow("""{"observed_at":"2026-06-04T21:30:00+02:00"}""").ToObject<MapperEdgeDateTimeRow>();

        Assert.Equal(DateTimeKind.Utc, withOffset.ObservedAt.Kind);
        Assert.Equal(new DateTime(2026, 6, 4, 19, 30, 0, DateTimeKind.Utc), withOffset.ObservedAt);

        var withZuluSuffix = CreateRow("""{"observed_at":"2026-06-04T21:30:00Z"}""").ToObject<MapperEdgeDateTimeRow>();

        Assert.Equal(DateTimeKind.Utc, withZuluSuffix.ObservedAt.Kind);
        Assert.Equal(new DateTime(2026, 6, 4, 21, 30, 0, DateTimeKind.Utc), withZuluSuffix.ObservedAt);

        // Bare timestamps carry no offset, so they are assumed to already be UTC.
        var bare = CreateRow("""{"observed_at":"2026-06-04 21:30:00"}""").ToObject<MapperEdgeDateTimeRow>();

        Assert.Equal(DateTimeKind.Utc, bare.ObservedAt.Kind);
        Assert.Equal(new DateTime(2026, 6, 4, 21, 30, 0, DateTimeKind.Utc), bare.ObservedAt);
    }

    [Fact]
    public void UndefinedNumericEnumValueRaisesMappingExceptionWithoutEchoingTheValue()
    {
        var exception = Assert.Throws<SplunkMappingException>(() =>
            CreateRow("""{"level":"999"}""").ToObject<MapperEdgeEnumRow>());

        Assert.Equal(
            "Could not map Splunk field 'level' to property 'Level' (MapperEdgeAuditLevel) because the value is not a defined 'MapperEdgeAuditLevel' value.",
            exception.Message);
        Assert.DoesNotContain("999", exception.Message, StringComparison.Ordinal);

        // Defined names (case-insensitive) and defined numeric values still map.
        Assert.Equal(MapperEdgeAuditLevel.High, CreateRow("""{"level":"high"}""").ToObject<MapperEdgeEnumRow>().Level);
        Assert.Equal(MapperEdgeAuditLevel.High, CreateRow("""{"level":"2"}""").ToObject<MapperEdgeEnumRow>().Level);
    }

    [Fact]
    public void WideScalarTypesRoundTripWithInvariantCulture()
    {
        var mapped = CreateRow("""
            {
              "float_value": "123.25",
              "short_value": "-321",
              "byte_value": "200",
              "ushort_value": "65001",
              "uint_value": "4000000001",
              "ulong_value": "18446744073709551615",
              "char_value": "A",
              "timespan_value": "1.02:03:04",
              "dateonly_value": "2026-06-04",
              "timeonly_value": "21:30:15"
            }
            """).ToObject<MapperEdgeScalarRow>();

        Assert.Equal(123.25f, mapped.FloatValue);
        Assert.Equal((short)-321, mapped.ShortValue);
        Assert.Equal((byte)200, mapped.ByteValue);
        Assert.Equal((ushort)65001, mapped.UnsignedShortValue);
        Assert.Equal(4000000001u, mapped.UnsignedIntValue);
        Assert.Equal(18446744073709551615ul, mapped.UnsignedLongValue);
        Assert.Equal('A', mapped.CharValue);
        Assert.Equal(new TimeSpan(1, 2, 3, 4), mapped.TimeSpanValue);
        Assert.Equal(new DateOnly(2026, 6, 4), mapped.DateOnlyValue);
        Assert.Equal(new TimeOnly(21, 30, 15), mapped.TimeOnlyValue);
    }

    [Fact]
    public void IndexerPropertiesAreIgnoredWhenMappingRows()
    {
        // The row deliberately carries an "Item" field, which matches the CLR name
        // of the indexer property; mapping must skip indexers instead of failing
        // with TargetParameterCountException.
        var mapped = CreateRow("""{"service":"checkout","Item":"ignored"}""").ToObject<MapperEdgeIndexerRow>();

        Assert.Equal("checkout", mapped.Service);
    }

    [Fact]
    public void MultiValueFieldsMapToStringArrayAndGenericList()
    {
        var mapped = CreateRow("""{"tags":["payments","checkout"],"hosts":["web-1","web-2"]}""")
            .ToObject<MapperEdgeCollectionsRow>();

        Assert.Equal(new[] { "payments", "checkout" }, mapped.Tags);
        Assert.Equal(new List<string> { "web-1", "web-2" }, mapped.Hosts);
    }

    [Fact]
    public void UnsupportedCollectionShapesGetCollectionSpecificErrorMessages()
    {
        var row = CreateRow("""{"users":["alice","bob"]}""");

        var collectionException = Assert.Throws<SplunkMappingException>(() =>
            row.ToObject<MapperEdgeUnsupportedCollectionRow>());

        Assert.Equal(
            "Could not map multi-value Splunk field 'users' to property 'Users' because 'HashSet`1' is an unsupported collection type.",
            collectionException.Message);

        var scalarException = Assert.Throws<SplunkMappingException>(() =>
            row.ToObject<MapperEdgeScalarTargetRow>());

        Assert.Equal(
            "Could not map multi-value Splunk field 'users' to scalar property 'Users' (Int32).",
            scalarException.Message);

        Assert.DoesNotContain("alice", collectionException.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", scalarException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScalarFieldIntoCollectionPropertyGetsCollectionMismatchMessage()
    {
        var exception = Assert.Throws<SplunkMappingException>(() =>
            CreateRow("""{"tags":"payments"}""").ToObject<MapperEdgeCollectionsRow>());

        Assert.Equal(
            "Could not map scalar Splunk field 'tags' to collection property 'Tags' (String[]).",
            exception.Message);
        Assert.DoesNotContain("payments", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScalarConversionFailuresDoNotEchoFieldValues()
    {
        var exception = Assert.Throws<SplunkMappingException>(() =>
            CreateRow("""{"char_value":"not-a-char"}""").ToObject<MapperEdgeScalarRow>());

        Assert.Equal("Could not map Splunk field 'char_value' to property 'CharValue' (Char).", exception.Message);
        Assert.IsType<FormatException>(exception.InnerException);
        Assert.DoesNotContain("not-a-char", exception.Message, StringComparison.Ordinal);
    }
}

internal enum MapperEdgeAuditLevel
{
    Low = 1,
    High = 2
}

internal sealed class MapperEdgeDateTimeRow
{
    [SplunkField("observed_at")]
    public DateTime ObservedAt { get; set; }
}

internal sealed class MapperEdgeEnumRow
{
    [SplunkField("level")]
    public MapperEdgeAuditLevel Level { get; set; }
}

internal sealed class MapperEdgeScalarRow
{
    [SplunkField("float_value")]
    public float FloatValue { get; set; }

    [SplunkField("short_value")]
    public short ShortValue { get; set; }

    [SplunkField("byte_value")]
    public byte ByteValue { get; set; }

    [SplunkField("ushort_value")]
    public ushort UnsignedShortValue { get; set; }

    [SplunkField("uint_value")]
    public uint UnsignedIntValue { get; set; }

    [SplunkField("ulong_value")]
    public ulong UnsignedLongValue { get; set; }

    [SplunkField("char_value")]
    public char CharValue { get; set; }

    [SplunkField("timespan_value")]
    public TimeSpan TimeSpanValue { get; set; }

    [SplunkField("dateonly_value")]
    public DateOnly DateOnlyValue { get; set; }

    [SplunkField("timeonly_value")]
    public TimeOnly TimeOnlyValue { get; set; }
}

internal sealed class MapperEdgeIndexerRow
{
    private readonly Dictionary<int, string> _values = new();

    [SplunkField("service")]
    public string? Service { get; set; }

    public string this[int index]
    {
        get => _values.TryGetValue(index, out var value) ? value : string.Empty;
        set => _values[index] = value;
    }
}

internal sealed class MapperEdgeCollectionsRow
{
    [SplunkField("tags")]
    public string[]? Tags { get; set; }

    [SplunkField("hosts")]
    public List<string>? Hosts { get; set; }
}

internal sealed class MapperEdgeUnsupportedCollectionRow
{
    [SplunkField("users")]
    public HashSet<string>? Users { get; set; }
}

internal sealed class MapperEdgeScalarTargetRow
{
    [SplunkField("users")]
    public int Users { get; set; }
}
