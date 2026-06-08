using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SplunkSdk.Models;

namespace SplunkSdk.Mapping;

/// <summary>
/// Maps Splunk search result rows to typed DTOs.
/// </summary>
/// <remarks>
/// Mapping uses public writable properties. Field names are resolved from
/// <see cref="SplunkFieldAttribute"/>, then <see cref="JsonPropertyNameAttribute"/>,
/// then the property name.
/// </remarks>
public static class SplunkSearchResultMappingExtensions
{
    private static readonly ConcurrentDictionary<Type, PropertyMap[]> PropertyCache = new();

    /// <summary>
    /// Maps a Splunk result row to a new DTO instance.
    /// </summary>
    /// <typeparam name="T">DTO type with a public parameterless constructor and public setters.</typeparam>
    /// <param name="result">Splunk result row.</param>
    /// <returns>A mapped DTO instance.</returns>
    public static T ToObject<T>(this SplunkSearchResult result)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(result);

        var instance = new T();
        foreach (var property in GetPropertyMaps(typeof(T)))
        {
            if (!result.Fields.TryGetValue(property.FieldName, out var fieldValue))
            {
                continue;
            }

            var converted = ConvertValue(fieldValue, property.Property.PropertyType, property.FieldName, property.Property.Name);
            property.Property.SetValue(instance, converted);
        }

        return instance;
    }

    /// <summary>
    /// Maps result rows to DTO instances.
    /// </summary>
    /// <typeparam name="T">DTO type with a public parameterless constructor and public setters.</typeparam>
    /// <param name="results">Rows to map.</param>
    /// <returns>Mapped DTO instances.</returns>
    public static IReadOnlyList<T> ToObjects<T>(this IEnumerable<SplunkSearchResult> results)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(results);
        return results.Select(ToObject<T>).ToArray();
    }

    /// <summary>
    /// Maps an asynchronous result stream to DTO instances.
    /// </summary>
    /// <typeparam name="T">DTO type with a public parameterless constructor and public setters.</typeparam>
    /// <param name="results">Asynchronous result stream to map.</param>
    /// <param name="cancellationToken">Cancellation token applied while enumerating the stream.</param>
    /// <returns>An asynchronous stream of mapped DTO instances.</returns>
    public static async IAsyncEnumerable<T> ToObjectsAsync<T>(
        this IAsyncEnumerable<SplunkSearchResult> results,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(results);

        await foreach (var result in results.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return result.ToObject<T>();
        }
    }

    private static PropertyMap[] GetPropertyMaps(Type type) =>
        PropertyCache.GetOrAdd(
            type,
            static mappedType => mappedType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanWrite && property.SetMethod?.IsPublic == true)
                .Select(property => new PropertyMap(property, GetFieldName(property)))
                .ToArray());

    private static string GetFieldName(PropertyInfo property) =>
        property.GetCustomAttribute<SplunkFieldAttribute>()?.Name ??
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ??
        property.Name;

    private static object? ConvertValue(JsonElement value, Type targetType, string fieldName, string propertyName)
    {
        if (targetType == typeof(JsonElement))
        {
            return value.Clone();
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return ConvertArrayValue(value, targetType, fieldName, propertyName);
        }

        if (TryGetSupportedCollectionElementType(targetType, out _))
        {
            throw new SplunkMappingException(
                $"Could not map scalar Splunk field '{fieldName}' to collection property '{propertyName}' ({FriendlyName(targetType)}).");
        }

        return ConvertScalarValue(value, targetType, fieldName, propertyName);
    }

    private static object? ConvertScalarValue(JsonElement value, Type targetType, string fieldName, string propertyName)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        var effectiveType = underlyingType ?? targetType;
        var text = ReadScalar(value, fieldName, propertyName, targetType);

        if (text is null)
        {
            if (underlyingType is null && targetType.IsValueType)
            {
                throw new SplunkMappingException(
                    $"Could not map null Splunk field '{fieldName}' to non-nullable property '{propertyName}' ({FriendlyName(targetType)}).");
            }

            return null;
        }

        if (text.Length == 0 && underlyingType is not null)
        {
            return null;
        }

        try
        {
            if (effectiveType == typeof(string))
            {
                return text;
            }

            if (effectiveType == typeof(bool))
            {
                return text switch
                {
                    "1" => true,
                    "0" => false,
                    _ => bool.Parse(text)
                };
            }

            if (effectiveType == typeof(int))
            {
                return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (effectiveType == typeof(long))
            {
                return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (effectiveType == typeof(double))
            {
                return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            if (effectiveType == typeof(decimal))
            {
                return decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
            }

            if (effectiveType == typeof(DateTimeOffset))
            {
                return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            }

            if (effectiveType == typeof(DateTime))
            {
                return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            }

            if (effectiveType == typeof(Guid))
            {
                return Guid.Parse(text);
            }

            if (effectiveType.IsEnum)
            {
                return Enum.Parse(effectiveType, text, ignoreCase: true);
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new SplunkMappingException(
                $"Could not map Splunk field '{fieldName}' to property '{propertyName}' ({FriendlyName(targetType)}).",
                ex);
        }

        throw new SplunkMappingException(
            $"Property '{propertyName}' uses unsupported mapping type '{targetType.FullName}'.");
    }

    private static object ConvertArrayValue(JsonElement value, Type targetType, string fieldName, string propertyName)
    {
        if (!TryGetSupportedCollectionElementType(targetType, out var elementType))
        {
            throw new SplunkMappingException(
                $"Could not map multi-value Splunk field '{fieldName}' to scalar property '{propertyName}' ({FriendlyName(targetType)}).");
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var element in value.EnumerateArray())
        {
            list.Add(ConvertScalarValue(element, elementType, fieldName, propertyName));
        }

        if (targetType.IsArray)
        {
            var array = Array.CreateInstance(elementType, list.Count);
            list.CopyTo(array, 0);
            return array;
        }

        if (targetType.IsAssignableFrom(list.GetType()))
        {
            return list;
        }

        throw new SplunkMappingException(
            $"Property '{propertyName}' uses unsupported collection mapping type '{targetType.FullName}'.");
    }

    private static bool TryGetSupportedCollectionElementType(Type targetType, out Type elementType)
    {
        if (targetType == typeof(string))
        {
            elementType = typeof(object);
            return false;
        }

        if (targetType.IsArray && targetType.GetElementType() is { } arrayElementType)
        {
            elementType = arrayElementType;
            return true;
        }

        if (targetType.IsGenericType && IsSupportedCollectionDefinition(targetType.GetGenericTypeDefinition()))
        {
            elementType = targetType.GetGenericArguments()[0];
            return true;
        }

        elementType = typeof(object);
        return false;
    }

    private static bool IsSupportedCollectionDefinition(Type genericTypeDefinition) =>
        genericTypeDefinition == typeof(IEnumerable<>) ||
        genericTypeDefinition == typeof(IReadOnlyCollection<>) ||
        genericTypeDefinition == typeof(IReadOnlyList<>) ||
        genericTypeDefinition == typeof(ICollection<>) ||
        genericTypeDefinition == typeof(IList<>) ||
        genericTypeDefinition == typeof(List<>);

    private static string? ReadScalar(JsonElement value, string fieldName, string propertyName, Type targetType) =>
        value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Number => value.ToString(),
            _ => throw new SplunkMappingException(
                $"Could not map non-scalar Splunk field '{fieldName}' to property '{propertyName}' ({FriendlyName(targetType)}).")
        };

    private static string FriendlyName(Type type) =>
        string.IsNullOrWhiteSpace(type.Name) ? type.FullName ?? "unknown" : type.Name;

    private sealed record PropertyMap(PropertyInfo Property, string FieldName);
}
