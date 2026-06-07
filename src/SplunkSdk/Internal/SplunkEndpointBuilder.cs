using SplunkSdk.Configuration;
using SplunkSdk.Models;

namespace SplunkSdk;

internal sealed class SplunkEndpointBuilder
{
    private readonly Uri _managementUri;
    private readonly SplunkSearchApiVersion _searchApiVersion;
    private readonly SplunkNamespace? _defaultNamespace;

    public SplunkEndpointBuilder(SplunkClientOptions options)
    {
        _managementUri = options.NormalizedManagementUri;
        _searchApiVersion = options.SearchApiVersion;
        _defaultNamespace = options.DefaultNamespace;
    }

    public Uri SearchEndpoint(string relativeSearchPath, SplunkNamespace? requestNamespace)
    {
        var prefix = (requestNamespace ?? _defaultNamespace)?.ToPathPrefix() ?? "services";
        var versionedSearchPath = _searchApiVersion switch
        {
            SplunkSearchApiVersion.V1 => $"search/{relativeSearchPath}",
            SplunkSearchApiVersion.V2 => $"search/v2/{relativeSearchPath}",
            _ => throw new ArgumentOutOfRangeException(nameof(_searchApiVersion))
        };

        return new Uri(_managementUri, $"{prefix}/{versionedSearchPath}");
    }

    public Uri ServicesEndpoint(string relativePath, SplunkNamespace? requestNamespace)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A Splunk REST path is required.", nameof(relativePath));
        }

        var prefix = (requestNamespace ?? _defaultNamespace)?.ToPathPrefix() ?? "services";
        return new Uri(_managementUri, $"{prefix}/{relativePath.TrimStart('/')}");
    }

    public Uri AppendQuery(Uri uri, IEnumerable<KeyValuePair<string, string>> parameters)
    {
        var query = string.Join("&", parameters.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        if (string.IsNullOrEmpty(query))
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        var existingQuery = builder.Query;
        builder.Query = string.IsNullOrEmpty(existingQuery)
            ? query
            : $"{existingQuery.TrimStart('?')}&{query}";
        return builder.Uri;
    }
}
