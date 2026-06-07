using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using SplunkSdk.Configuration;
using SplunkSdk.Diagnostics;

namespace SplunkSdk;

internal sealed class SplunkRestClient
{
    private readonly HttpClient _httpClient;
    private readonly SplunkClientOptions _options;

    public SplunkRestClient(HttpClient httpClient, SplunkClientOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
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

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var messages = SplunkMessageParser.Parse(body);
        var activity = Activity.Current;
        activity?.SetStatus(ActivityStatusCode.Error, response.ReasonPhrase ?? response.StatusCode.ToString());
        activity?.SetTag("error.type", nameof(SplunkApiException));
        activity?.SetTag("splunk.message_count", messages.Count);

        if (messages.Count > 0)
        {
            activity?.SetTag("splunk.message_type", messages[0].Type);
        }

        SplunkDiagnostics.RecordRestError((int)response.StatusCode, messages.Count, messages.FirstOrDefault()?.Type);
        throw new SplunkApiException(response.StatusCode, response.ReasonPhrase, string.Empty, messages);
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
        int? statusCode = null;

        for (var attempt = 0; ; attempt++)
        {
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

                if (!CanRetry(method) || !ShouldRetry(response.StatusCode) || attempt >= _options.Retry.MaxRetries)
                {
                    activity?.SetTag("splunk.retry_count", retryCount);
                    SplunkDiagnostics.RecordRestRequestDuration(
                        stopwatch.Elapsed,
                        method.Method,
                        endpoint,
                        statusCode,
                        retryCount);
                    return response;
                }

                var delay = GetDelay(response, attempt);
                response.Dispose();
                retryCount++;
                SplunkDiagnostics.RecordRestRetry(method.Method, endpoint, statusCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
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
                await DelayAsync(GetDelay(null, attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (CanRetry(method) && !cancellationToken.IsCancellationRequested && attempt < _options.Retry.MaxRetries)
            {
                retryCount++;
                SplunkDiagnostics.RecordRestRetry(method.Method, endpoint, nameof(TaskCanceledException));
                await DelayAsync(GetDelay(null, attempt), cancellationToken).ConfigureAwait(false);
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
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private TimeSpan GetDelay(HttpResponseMessage? response, int attempt)
    {
        if (response?.Headers.RetryAfter?.Delta is { } delta)
        {
            return Clamp(delta);
        }

        if (response?.Headers.RetryAfter?.Date is { } date)
        {
            return Clamp(date - DateTimeOffset.UtcNow);
        }

        return ExponentialDelay(attempt);
    }

    private TimeSpan Clamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (_options.Retry.MaxDelay <= TimeSpan.Zero)
        {
            return value;
        }

        return value > _options.Retry.MaxDelay ? _options.Retry.MaxDelay : value;
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

    private TimeSpan ExponentialDelay(int attempt)
    {
        var baseMilliseconds = _options.Retry.BaseDelay.TotalMilliseconds;
        var maxMilliseconds = _options.Retry.MaxDelay.TotalMilliseconds;

        if (baseMilliseconds <= 0)
        {
            return TimeSpan.Zero;
        }

        if (maxMilliseconds > 0 && baseMilliseconds >= maxMilliseconds)
        {
            return _options.Retry.MaxDelay;
        }

        var multiplier = attempt >= 30 ? double.PositiveInfinity : Math.Pow(2, attempt);
        var delayMilliseconds = baseMilliseconds * multiplier;
        if (double.IsInfinity(delayMilliseconds) ||
            double.IsNaN(delayMilliseconds) ||
            (maxMilliseconds > 0 && delayMilliseconds >= maxMilliseconds))
        {
            return _options.Retry.MaxDelay;
        }

        return Clamp(TimeSpan.FromMilliseconds(delayMilliseconds));
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);

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
