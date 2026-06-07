using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Marouanvs.Splunk.Diagnostics;
using Marouanvs.Splunk.Models;

namespace Marouanvs.Splunk.Search;

/// <summary>
/// Default implementation of Splunk search operations.
/// </summary>
public sealed class SplunkSearchClient : ISplunkSearchClient
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromMinutes(5);

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

        HttpResponseMessage? response = null;
        Stream? stream = null;
        IAsyncEnumerator<SplunkSearchResult>? rows = null;

        try
        {
            try
            {
                var endpoint = _endpointBuilder.SearchEndpoint("jobs/export", request.Namespace);
                var parameters = request
                    .ToFormParameters(SplunkOutputMode.Json, includeResultOptions: true, defaultPreviewToFalse: true)
                    .ToArray();

                response = await _restClient.PostFormAsync(endpoint, parameters, cancellationToken).ConfigureAwait(false);
                await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

                stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                rows = SplunkSearchResultReader.ReadAsync(stream, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex)
            {
                SplunkDiagnostics.SetException(activity, ex);
                throw;
            }

            while (true)
            {
                bool moved;
                try
                {
                    moved = await rows!.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    SplunkDiagnostics.SetException(activity, ex);
                    throw;
                }

                if (!moved)
                {
                    break;
                }

                rowCount++;
                yield return rows.Current;
            }

            completed = true;
        }
        finally
        {
            if (rows is not null)
            {
                await rows.DisposeAsync().ConfigureAwait(false);
            }

            if (stream is not null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            response?.Dispose();

            activity?.SetTag("splunk.result.row_count", rowCount);
            activity?.SetTag("splunk.completed", completed);
            SplunkDiagnostics.RecordSearchRows(operation, rowCount);
            SplunkDiagnostics.RecordSearchOperationDuration(stopwatch.Elapsed, operation, completed, rowCount);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SplunkSearchResult>> OneshotSearchAsync(
        SplunkSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        const string operation = "search.oneshot";
        using var activity = SplunkDiagnostics.ActivitySource.StartActivity("Splunk search oneshot", ActivityKind.Client);
        activity?.SetTag("splunk.operation", operation);

        var stopwatch = Stopwatch.StartNew();
        var completed = false;
        var results = new List<SplunkSearchResult>();

        try
        {
            var endpoint = _endpointBuilder.SearchEndpoint("jobs", request.Namespace);
            var parameters = request.ToFormParameters(SplunkOutputMode.Json)
                .Append(new KeyValuePair<string, string>("exec_mode", "oneshot"))
                .ToArray();

            using var response = await _restClient.PostFormAsync(endpoint, parameters, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task<SplunkSearchJob> StartAsync(
        SplunkSearchRequest request,
        SplunkExecutionMode executionMode = SplunkExecutionMode.Normal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (executionMode == SplunkExecutionMode.Oneshot)
        {
            throw new ArgumentException(
                "Oneshot execution returns results instead of a search ID. Use OneshotSearchAsync.",
                nameof(executionMode));
        }

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
    public async Task<SplunkSearchJobStatus> GetJobStatusAsync(
        string searchId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchId))
        {
            throw new ArgumentException("A Splunk search ID is required.", nameof(searchId));
        }

        const string operation = "search.status";
        using var activity = SplunkDiagnostics.ActivitySource.StartActivity("Splunk search job status", ActivityKind.Client);
        activity?.SetTag("splunk.operation", operation);

        var stopwatch = Stopwatch.StartNew();
        var completed = false;

        try
        {
            var endpoint = _endpointBuilder.SearchEndpoint($"jobs/{Uri.EscapeDataString(searchId)}", requestNamespace: null);
            endpoint = _endpointBuilder.AppendQuery(
                endpoint,
                [new KeyValuePair<string, string>("output_mode", SplunkOutputMode.Json.ToSplunkValue())]);

            using var response = await _restClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
            await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var status = ParseJobStatus(body, searchId);
            completed = true;
            return status;
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
    public async Task<SplunkSearchJobStatus> WaitForJobCompletionAsync(
        string searchId,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchId))
        {
            throw new ArgumentException("A Splunk search ID is required.", nameof(searchId));
        }

        var interval = pollInterval ?? DefaultPollInterval;
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "Poll interval must be greater than zero.");
        }

        var limit = timeout ?? DefaultWaitTimeout;
        if (limit <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
        }

        const string operation = "search.wait";
        using var activity = SplunkDiagnostics.ActivitySource.StartActivity("Splunk search job wait", ActivityKind.Client);
        activity?.SetTag("splunk.operation", operation);

        var stopwatch = Stopwatch.StartNew();
        var completed = false;
        var pollCount = 0;

        try
        {
            while (true)
            {
                var status = await GetJobStatusAsync(searchId, cancellationToken).ConfigureAwait(false);
                pollCount++;

                if (status.IsFailed)
                {
                    throw new SplunkApiException(
                        System.Net.HttpStatusCode.OK,
                        "OK",
                        [new SplunkMessage("ERROR", $"The Splunk search job reached a failed state (dispatch state: {status.DispatchState ?? "FAILED"}).")]);
                }

                if (status.IsDone)
                {
                    completed = true;
                    return status;
                }

                var remaining = limit - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException(
                        $"Timed out after {limit} waiting for the Splunk search job to complete (dispatch state: {status.DispatchState ?? "unknown"}).");
                }

                await Task.Delay(remaining < interval ? remaining : interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            SplunkDiagnostics.SetException(activity, ex);
            throw;
        }
        finally
        {
            activity?.SetTag("splunk.poll_count", pollCount);
            activity?.SetTag("splunk.completed", completed);
            SplunkDiagnostics.RecordSearchOperationDuration(stopwatch.Elapsed, operation, completed);
        }
    }

    /// <inheritdoc />
    public async Task DeleteJobAsync(
        string searchId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchId))
        {
            throw new ArgumentException("A Splunk search ID is required.", nameof(searchId));
        }

        const string operation = "search.delete";
        using var activity = SplunkDiagnostics.ActivitySource.StartActivity("Splunk search job delete", ActivityKind.Client);
        activity?.SetTag("splunk.operation", operation);

        var stopwatch = Stopwatch.StartNew();
        var completed = false;

        try
        {
            var endpoint = _endpointBuilder.SearchEndpoint($"jobs/{Uri.EscapeDataString(searchId)}", requestNamespace: null);
            using var response = await _restClient.DeleteAsync(endpoint, cancellationToken).ConfigureAwait(false);
            await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            completed = true;
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
            // The v2 results endpoint accepts the post-process 'search' parameter
            // only on POST requests; the v2 GET operation does not support it.
            // POST also keeps long post-process SPL out of URL length limits, at
            // the cost of the SDK's built-in retries for idempotent requests.
            return _restClient.PostFormAsync(endpoint, parameters, cancellationToken);
        }

        // Without a post-process search, GET keeps the SDK's built-in retries
        // for idempotent requests.
        endpoint = _endpointBuilder.AppendQuery(endpoint, parameters);
        return _restClient.GetAsync(endpoint, cancellationToken);
    }

    private static string ParseSearchId(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new SplunkResponseFormatException("Splunk did not return a search ID.");
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

        throw new SplunkResponseFormatException("Splunk did not return a search ID.");
    }

    private static SplunkResponseFormatException CreateMalformedSearchIdResponseException(string format, Exception innerException)
    {
        // The inner exception carries parser positions, never payload text.
        return new SplunkResponseFormatException(
            $"Splunk returned malformed {format} for a search job response.",
            innerException);
    }

    private static SplunkSearchJobStatus ParseJobStatus(string body, string requestedSid)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new SplunkResponseFormatException("Splunk returned an empty search job status response.");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!TryGetJobContent(document.RootElement, out var content))
            {
                throw new SplunkResponseFormatException("Splunk returned a search job status response without job content.");
            }

            var dispatchState = ReadStringValue(content, "dispatchState");
            return new SplunkSearchJobStatus(ReadStringValue(content, "sid") ?? requestedSid)
            {
                IsDone = ReadBooleanValue(content, "isDone")
                    ?? string.Equals(dispatchState, "DONE", StringComparison.OrdinalIgnoreCase),
                IsFailed = ReadBooleanValue(content, "isFailed")
                    ?? string.Equals(dispatchState, "FAILED", StringComparison.OrdinalIgnoreCase),
                DispatchState = dispatchState,
                DoneProgress = ReadDoubleValue(content, "doneProgress"),
                EventCount = ReadInt64Value(content, "eventCount"),
                ResultCount = ReadInt64Value(content, "resultCount")
            };
        }
        catch (JsonException ex)
        {
            throw new SplunkResponseFormatException("Splunk returned malformed JSON for a search job status response.", ex);
        }
    }

    private static bool TryGetJobContent(JsonElement root, out JsonElement content)
    {
        content = default;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (root.TryGetProperty("entry", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object &&
                    entry.TryGetProperty("content", out var entryContent) &&
                    entryContent.ValueKind == JsonValueKind.Object)
                {
                    content = entryContent;
                    return true;
                }
            }

            return false;
        }

        if (root.TryGetProperty("content", out var rootContent) && rootContent.ValueKind == JsonValueKind.Object)
        {
            content = rootContent;
            return true;
        }

        return false;
    }

    private static string? ReadStringValue(JsonElement content, string propertyName)
    {
        if (content.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        return null;
    }

    private static bool? ReadBooleanValue(JsonElement content, string propertyName)
    {
        if (!content.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var number) => number != 0,
            JsonValueKind.String => ParseBooleanText(element.GetString()),
            _ => null
        };
    }

    private static bool? ParseBooleanText(string? value) =>
        value?.Trim() switch
        {
            "1" => true,
            "0" => false,
            { } text when bool.TryParse(text, out var parsed) => parsed,
            _ => null
        };

    private static long? ReadInt64Value(JsonElement content, string propertyName)
    {
        if (!content.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String &&
            long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static double? ReadDoubleValue(JsonElement content, string propertyName)
    {
        if (!content.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
