using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using SplunkSdk.Diagnostics;
using SplunkSdk.Models;

namespace SplunkSdk.Search;

/// <summary>
/// Default implementation of Splunk search operations.
/// </summary>
public sealed class SplunkSearchClient : ISplunkSearchClient
{
    private readonly SplunkRestClient _restClient;
    private readonly SplunkEndpointBuilder _endpointBuilder;

    internal SplunkSearchClient(SplunkRestClient restClient, SplunkEndpointBuilder endpointBuilder)
    {
        _restClient = restClient;
        _endpointBuilder = endpointBuilder;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SplunkSearchResult> ExportAsync(
        SplunkSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        const string operation = "search.export";
        using var activity = SplunkDiagnostics.ActivitySource.StartActivity("Splunk search export", ActivityKind.Client);
        activity?.SetTag("splunk.operation", operation);

        var stopwatch = Stopwatch.StartNew();
        var rowCount = 0;
        var completed = false;

        try
        {
            var endpoint = _endpointBuilder.SearchEndpoint("jobs/export", request.Namespace);
            var parameters = request.ToFormParameters(SplunkOutputMode.Json).ToArray();

            using var response = await _restClient.PostFormAsync(endpoint, parameters, cancellationToken).ConfigureAwait(false);
            await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await foreach (var result in SplunkSearchResultReader.ReadAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                rowCount++;
                yield return result;
            }

            completed = true;
        }
        finally
        {
            activity?.SetTag("splunk.result.row_count", rowCount);
            activity?.SetTag("splunk.completed", completed);
            SplunkDiagnostics.RecordSearchRows(operation, rowCount);
            SplunkDiagnostics.RecordSearchOperationDuration(stopwatch.Elapsed, operation, completed, rowCount);
        }
    }

    /// <inheritdoc />
    public async Task<SplunkSearchJob> StartAsync(
        SplunkSearchRequest request,
        SplunkExecutionMode executionMode = SplunkExecutionMode.Normal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        const string operation = "search.start";
        using var activity = SplunkDiagnostics.ActivitySource.StartActivity("Splunk search start", ActivityKind.Client);
        activity?.SetTag("splunk.operation", operation);
        activity?.SetTag("splunk.execution_mode", executionMode.ToSplunkValue());

        var stopwatch = Stopwatch.StartNew();
        var completed = false;

        try
        {
            var endpoint = _endpointBuilder.SearchEndpoint("jobs", request.Namespace);
            var parameters = request.ToFormParameters(SplunkOutputMode.Json, includeResultOptions: false)
                .Append(new KeyValuePair<string, string>("exec_mode", executionMode.ToSplunkValue()))
                .ToArray();

            using var response = await _restClient.PostFormAsync(endpoint, parameters, cancellationToken).ConfigureAwait(false);
            await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var job = new SplunkSearchJob(ParseSearchId(body));
            completed = true;
            return job;
        }
        catch (Exception ex)
        {
            SplunkDiagnostics.SetException(activity, ex);
            throw;
        }
        finally
        {
            activity?.SetTag("splunk.completed", completed);
            SplunkDiagnostics.RecordSearchOperationDuration(stopwatch.Elapsed, operation, completed);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SplunkSearchResult>> GetResultsAsync(
        string searchId,
        SplunkResultRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchId))
        {
            throw new ArgumentException("A Splunk search ID is required.", nameof(searchId));
        }

        const string operation = "search.results";
        using var activity = SplunkDiagnostics.ActivitySource.StartActivity("Splunk search results", ActivityKind.Client);
        activity?.SetTag("splunk.operation", operation);

        var stopwatch = Stopwatch.StartNew();
        var completed = false;
        var results = new List<SplunkSearchResult>();

        try
        {
            request ??= new SplunkResultRequest();
            var endpoint = _endpointBuilder.SearchEndpoint($"jobs/{Uri.EscapeDataString(searchId)}/results", request.Namespace);

            using var response = await SendResultsRequestAsync(endpoint, request, cancellationToken).ConfigureAwait(false);
            await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await foreach (var result in SplunkSearchResultReader.ReadAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                results.Add(result);
            }

            completed = true;
            return results;
        }
        catch (Exception ex)
        {
            SplunkDiagnostics.SetException(activity, ex);
            throw;
        }
        finally
        {
            activity?.SetTag("splunk.result.row_count", results.Count);
            activity?.SetTag("splunk.completed", completed);
            SplunkDiagnostics.RecordSearchRows(operation, results.Count);
            SplunkDiagnostics.RecordSearchOperationDuration(stopwatch.Elapsed, operation, completed, results.Count);
        }
    }

    private Task<HttpResponseMessage> SendResultsRequestAsync(
        Uri endpoint,
        SplunkResultRequest request,
        CancellationToken cancellationToken)
    {
        var parameters = request.ToQueryParameters(SplunkOutputMode.Json).ToArray();
        if (!string.IsNullOrWhiteSpace(request.PostProcessSearch))
        {
            return _restClient.PostFormAsync(endpoint, parameters, cancellationToken);
        }

        endpoint = _endpointBuilder.AppendQuery(endpoint, parameters);
        return _restClient.GetAsync(endpoint, cancellationToken);
    }

    private static string ParseSearchId(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new SplunkApiException(System.Net.HttpStatusCode.OK, "OK", string.Empty, [new SplunkMessage("ERROR", "Splunk did not return a search ID.")]);
        }

        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("sid", out var sidElement) &&
                    sidElement.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(sidElement.GetString()))
                {
                    return sidElement.GetString()!;
                }

                if (document.RootElement.TryGetProperty("entry", out var entries) && entries.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in entries.EnumerateArray())
                    {
                        if (entry.TryGetProperty("content", out var content) &&
                            content.TryGetProperty("sid", out sidElement) &&
                            sidElement.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(sidElement.GetString()))
                        {
                            return sidElement.GetString()!;
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                throw CreateMalformedSearchIdResponseException("JSON", ex);
            }
        }

        if (trimmed.StartsWith('<'))
        {
            try
            {
                var document = XDocument.Parse(body);
                var sid = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "sid")?.Value;
                if (!string.IsNullOrWhiteSpace(sid))
                {
                    return sid;
                }
            }
            catch (XmlException ex)
            {
                throw CreateMalformedSearchIdResponseException("XML", ex);
            }
        }

        throw new SplunkApiException(System.Net.HttpStatusCode.OK, "OK", string.Empty, [new SplunkMessage("ERROR", "Splunk did not return a search ID.")]);
    }

    private static SplunkApiException CreateMalformedSearchIdResponseException(string format, Exception innerException)
    {
        _ = innerException;
        return new SplunkApiException(
            System.Net.HttpStatusCode.OK,
            "OK",
            string.Empty,
            [new SplunkMessage("ERROR", $"Splunk returned malformed {format} for a search job response.")]);
    }
}
