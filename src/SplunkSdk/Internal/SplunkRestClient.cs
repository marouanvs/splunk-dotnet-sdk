using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.Diagnostics;

namespace Marouanvs.Splunk;

internal sealed class SplunkRestClient
{
    /// <summary>
    /// Maximum number of error-response body bytes read for message parsing, so a
    /// pathological error body is never buffered unbounded.
    /// </summary>
    private const int MaxErrorBodyBytes = 64 * 1024;

    private readonly HttpClient _httpClient;
    private readonly SplunkClientOptions _options;
    private readonly ProductInfoHeaderValue[] _userAgentValues;

    public SplunkRestClient(HttpClient httpClient, SplunkClientOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _userAgentValues = SplunkClientOptions.ParseUserAgentValues(_options.UserAgent);
    }

    public async Task<HttpResponseMessage> PostFormAsync(
        Uri uri,
        IReadOnlyList<KeyValuePair<string, string>> parameters,
        CancellationToken cancellationToken)
    {
        return await SendWithRetryAsync(
            HttpMethod.Post,
            uri,
            () => new FormUrlEncodedContent(parameters),
            cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        return await SendWithRetryAsync(HttpMethod.Get, uri, () => null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> DeleteAsync(Uri uri, CancellationToken cancellationToken)
    {
        return await SendWithRetryAsync(HttpMethod.Delete, uri, () => null, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await ReadErrorBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
        var messages = SplunkMessageParser.Parse(body);
        SplunkDiagnostics.RecordRestError((int)response.StatusCode, messages.Count, messages.FirstOrDefault()?.Type);
        throw new SplunkApiException(response.StatusCode, response.ReasonPhrase, messages);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpMethod method,
        Uri uri,
        Func<HttpContent?> contentFactory,
        CancellationToken cancellationToken)
    {
        var endpoint = ClassifyEndpoint(uri);
        using var activity = SplunkDiagnostics.ActivitySource.StartActivity("Splunk REST request", ActivityKind.Client);
        activity?.SetTag("http.request.method", method.Method);
        activity?.SetTag("splunk.endpoint", endpoint);
        activity?.SetTag("splunk.search_api_version", ClassifySearchApiVersion(uri));

        var stopwatch = Stopwatch.StartNew();
        var retryCount = 0;
        int? statusCode;

        for (var attempt = 0; ; attempt++)
        {
            // Reset the captured status per attempt so duration metrics and the
            // request span never pair a stale status code from an earlier attempt
            // with a later exception. SetTag with null removes the activity tag
            // and is a no-op when the tag was never set.
            statusCode = null;
            activity?.SetTag("http.response.status_code", null);

            try
            {
                using var request = await CreateRequestAsync(method, uri, contentFactory(), cancellationToken).ConfigureAwait(false);
                var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                    .ConfigureAwait(false);

                statusCode = (int)response.StatusCode;
                activity?.SetTag("http.response.status_code", statusCode.Value);

                TimeSpan? delay = null;
                if (CanRetry(method) && ShouldRetry(response.StatusCode) && attempt < _options.Retry.MaxRetries)
                {
                    // A null delay means the server requested a wait beyond MaxServerDelay,
                    // so the response error is surfaced immediately instead of retried.
                    delay = GetDelay(response, attempt);
                }

                if (delay is null)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        // Mark the SDK's own request span (still alive here) as failed.
                        // Error tags must never land on ambient application activities.
                        activity?.SetStatus(ActivityStatusCode.Error, response.ReasonPhrase ?? response.StatusCode.ToString());
                        activity?.SetTag("error.type", nameof(SplunkApiException));
                    }

                    activity?.SetTag("splunk.retry_count", retryCount);
                    SplunkDiagnostics.RecordRestRequestDuration(
                        stopwatch.Elapsed,
                        method.Method,
                        endpoint,
                        statusCode,
                        retryCount);
                    return response;
                }

                response.Dispose();
                retryCount++;
                SplunkDiagnostics.RecordRestRetry(method.Method, endpoint, statusCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await DelayAsync(delay.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (IsTlsFailure(ex))
            {
                var configurationException = new SplunkConfigurationException(
                    "TLS certificate validation failed for the Splunk management endpoint. Ensure the certificate chain is trusted by the host, the certificate is not expired, and the ManagementUri host matches the certificate subject or SAN. For local labs only, supply a caller-owned HttpClient with an explicit certificate validation policy.",
                    ex);

                SplunkDiagnostics.SetException(activity, configurationException);
                activity?.SetTag("splunk.retry_count", retryCount);
                SplunkDiagnostics.RecordRestRequestDuration(
                    stopwatch.Elapsed,
                    method.Method,
                    endpoint,
                    statusCode,
                    retryCount,
                    nameof(SplunkConfigurationException));
                throw configurationException;
            }
            catch (HttpRequestException) when (CanRetry(method) && attempt < _options.Retry.MaxRetries)
            {
                retryCount++;
                SplunkDiagnostics.RecordRestRetry(method.Method, endpoint, nameof(HttpRequestException));
                await DelayAsync(ExponentialDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (CanRetry(method) && !cancellationToken.IsCancellationRequested && attempt < _options.Retry.MaxRetries)
            {
                retryCount++;
                SplunkDiagnostics.RecordRestRetry(method.Method, endpoint, nameof(TaskCanceledException));
                await DelayAsync(ExponentialDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                SplunkDiagnostics.SetException(activity, ex);
                activity?.SetTag("splunk.retry_count", retryCount);
                SplunkDiagnostics.RecordRestRequestDuration(
                    stopwatch.Elapsed,
                    method.Method,
                    endpoint,
                    statusCode,
                    retryCount,
                    ex.GetType().Name);
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                SplunkDiagnostics.SetException(activity, ex);
                activity?.SetTag("splunk.retry_count", retryCount);
                SplunkDiagnostics.RecordRestRequestDuration(
                    stopwatch.Elapsed,
                    method.Method,
                    endpoint,
                    statusCode,
                    retryCount,
                    ex.GetType().Name);
                throw;
            }
        }
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        Uri uri,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var token = await _options.TokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new SplunkConfigurationException("The Splunk token provider returned an empty token.");
        }

        var request = new HttpRequestMessage(method, uri)
        {
            Content = content
        };

        request.Headers.Authorization = new AuthenticationHeaderValue(_options.AuthorizationScheme.ToHeaderValue(), token);
        foreach (var userAgentValue in _userAgentValues)
        {
            request.Headers.UserAgent.Add(userAgentValue);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>
    /// Computes the wait before the next retry attempt, or <see langword="null"/> when the
    /// server requested a delay beyond <see cref="SplunkRetryOptions.MaxServerDelay"/> and
    /// the SDK should fail fast with the response error instead of retrying.
    /// </summary>
    private TimeSpan? GetDelay(HttpResponseMessage response, int attempt)
    {
        TimeSpan? serverDelay = null;
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            serverDelay = delta;
        }
        else if (response.Headers.RetryAfter?.Date is { } date)
        {
            serverDelay = date - DateTimeOffset.UtcNow;
        }

        if (serverDelay is { } requested)
        {
            if (requested <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            // Server-requested delays are honored above MaxDelay, up to MaxServerDelay.
            return requested > _options.Retry.MaxServerDelay ? null : requested;
        }

        return ExponentialDelay(attempt);
    }

    private static bool ShouldRetry(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static bool CanRetry(HttpMethod method) => method == HttpMethod.Get || method == HttpMethod.Delete;

    private static bool IsTlsFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes a full-jitter exponential backoff: a uniformly random duration between zero
    /// and <c>BaseDelay * 2^attempt</c>, with the bound capped at <c>MaxDelay</c>, so
    /// concurrent clients hitting the same throttled endpoint do not retry in lockstep.
    /// </summary>
    private TimeSpan ExponentialDelay(int attempt)
    {
        var baseMilliseconds = _options.Retry.BaseDelay.TotalMilliseconds;
        var maxMilliseconds = _options.Retry.MaxDelay.TotalMilliseconds;

        if (baseMilliseconds <= 0)
        {
            return TimeSpan.Zero;
        }

        var multiplier = attempt >= 30 ? double.PositiveInfinity : Math.Pow(2, attempt);
        var ceilingMilliseconds = baseMilliseconds * multiplier;
        if (double.IsNaN(ceilingMilliseconds) ||
            double.IsInfinity(ceilingMilliseconds) ||
            (maxMilliseconds > 0 && ceilingMilliseconds > maxMilliseconds))
        {
            ceilingMilliseconds = maxMilliseconds > 0 ? maxMilliseconds : baseMilliseconds;
        }

        return TimeSpan.FromMilliseconds(ceilingMilliseconds * Random.Shared.NextDouble());
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);

    private static async Task<string> ReadErrorBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[MaxErrorBodyBytes];
            var totalRead = 0;
            while (totalRead < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            return Encoding.UTF8.GetString(buffer, 0, totalRead);
        }
    }

    private static string ClassifyEndpoint(Uri uri)
    {
        var path = uri.AbsolutePath;

        if (path.EndsWith("/search/v2/jobs/export", StringComparison.Ordinal) ||
            path.EndsWith("/search/jobs/export", StringComparison.Ordinal))
        {
            return "search.jobs.export";
        }

        if (path.Contains("/search/v2/jobs/", StringComparison.Ordinal) &&
            path.EndsWith("/results", StringComparison.Ordinal))
        {
            return "search.jobs.results";
        }

        if (path.Contains("/search/jobs/", StringComparison.Ordinal) &&
            path.EndsWith("/results", StringComparison.Ordinal))
        {
            return "search.jobs.results";
        }

        if (path.EndsWith("/search/v2/jobs", StringComparison.Ordinal) ||
            path.EndsWith("/search/jobs", StringComparison.Ordinal))
        {
            return "search.jobs";
        }

        if (path.Contains("/search/v2/jobs/", StringComparison.Ordinal) ||
            path.Contains("/search/jobs/", StringComparison.Ordinal))
        {
            // Job status reads and job deletes target jobs/{sid}; the sid itself is
            // never recorded to keep the endpoint dimension low-cardinality.
            return "search.jobs.detail";
        }

        if (path.Contains("/alerts/fired_alerts", StringComparison.Ordinal))
        {
            return "alerts.fired_alerts";
        }

        if (path.Contains("/saved/searches", StringComparison.Ordinal))
        {
            if (path.EndsWith("/dispatch", StringComparison.Ordinal))
            {
                return "saved_searches_dispatch";
            }

            if (path.EndsWith("/acknowledge", StringComparison.Ordinal))
            {
                return "saved_searches_acknowledge";
            }

            if (path.EndsWith("/suppress", StringComparison.Ordinal))
            {
                return "saved_searches_suppress";
            }

            return "saved_searches";
        }

        return "unknown";
    }

    private static string ClassifySearchApiVersion(Uri uri)
    {
        var path = uri.AbsolutePath;

        if (path.Contains("/search/v2/", StringComparison.Ordinal))
        {
            return "v2";
        }

        if (path.Contains("/search/", StringComparison.Ordinal))
        {
            return "v1";
        }

        return "unknown";
    }
}
