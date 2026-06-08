using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using SplunkSdk.Diagnostics;
using SplunkSdk.Models;

namespace SplunkSdk.SavedSearches;

/// <summary>
/// Default saved search client.
/// </summary>
public sealed class SplunkSavedSearchClient : ISplunkSavedSearchClient
{
    private static readonly HashSet<string> ReservedSavedSearchParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "search",
        "output_mode",
        "description",
        "is_scheduled",
        "cron_schedule",
        "dispatch.earliest_time",
        "dispatch.latest_time",
        "dispatch.buckets",
        "dispatch.max_count",
        "dispatch.lookups",
        "dispatch.time_format",
        "disabled"
    };

    private readonly SplunkRestClient _restClient;
    private readonly SplunkEndpointBuilder _endpointBuilder;

    internal SplunkSavedSearchClient(SplunkRestClient restClient, SplunkEndpointBuilder endpointBuilder)
    {
        _restClient = restClient;
        _endpointBuilder = endpointBuilder;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SplunkSavedSearch>> ListAsync(
        SplunkSavedSearchListRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        request ??= new SplunkSavedSearchListRequest();
        var endpoint = _endpointBuilder.ServicesEndpoint("saved/searches", request.Namespace);
        endpoint = _endpointBuilder.AppendQuery(endpoint, request.ToQueryParameters());

        var body = await GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return SplunkAtomFeedParser.ParseSavedSearches(body);
    }

    /// <inheritdoc />
    public async Task<SplunkSavedSearch?> GetAsync(
        string name,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        var endpoint = SavedSearchEndpoint(name, splunkNamespace);
        endpoint = _endpointBuilder.AppendQuery(endpoint, [new KeyValuePair<string, string>("output_mode", "json")]);

        const string operation = "saved_search.get";
        using var activity = SplunkDiagnostics.ActivitySource.StartActivity("Splunk saved search GET", ActivityKind.Client);
        activity?.SetTag("splunk.operation", operation);

        using var response = await _restClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return SplunkAtomFeedParser.ParseSavedSearches(body).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<SplunkSavedSearch> CreateAsync(
        CreateSavedSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateName(request.Name);
        ValidateSearch(request.Search);
        if (request.IsScheduled)
        {
            ValidateCronSchedule(request.CronSchedule, nameof(request.CronSchedule));
        }

        var endpoint = _endpointBuilder.ServicesEndpoint("saved/searches", request.Namespace);
        var parameters = BuildCreateParameters(request).ToArray();
        var body = await PostFormForStringAsync(endpoint, parameters, cancellationToken).ConfigureAwait(false);
        return ParseSingleSavedSearch(body, request.Name);
    }

    /// <inheritdoc />
    public async Task<SplunkSavedSearch> UpdateAsync(
        string name,
        UpdateSavedSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        ArgumentNullException.ThrowIfNull(request);

        var endpoint = SavedSearchEndpoint(name, request.Namespace);
        var parameters = BuildUpdateParameters(request).ToArray();
        var body = await PostFormForStringAsync(endpoint, parameters, cancellationToken).ConfigureAwait(false);
        return ParseSingleSavedSearch(body, name);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string name,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        var endpoint = SavedSearchEndpoint(name, splunkNamespace);

        using var response = await _restClient.DeleteAsync(endpoint, cancellationToken).ConfigureAwait(false);
        await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SplunkSearchJob> DispatchAsync(
        string name,
        SplunkDispatchSavedSearchRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        request ??= new SplunkDispatchSavedSearchRequest();

        var endpoint = _endpointBuilder.ServicesEndpoint($"saved/searches/{Uri.EscapeDataString(name)}/dispatch", request.Namespace);
        var body = await PostFormForStringAsync(endpoint, request.Parameters?.ToArray() ?? [], cancellationToken).ConfigureAwait(false);
        return new SplunkSearchJob(ParseSearchId(body));
    }

    internal static IEnumerable<KeyValuePair<string, string>> BuildAlertParameters(SplunkAlertSettings alert)
    {
        if (alert.AlertType is not null)
        {
            yield return new KeyValuePair<string, string>("alert_type", alert.AlertType.Value.ToSplunkValue());
        }

        if (alert.Comparator is not null)
        {
            yield return new KeyValuePair<string, string>("alert_comparator", alert.Comparator.Value.ToSplunkValue());
        }

        if (!string.IsNullOrWhiteSpace(alert.Threshold))
        {
            yield return new KeyValuePair<string, string>("alert_threshold", alert.Threshold!);
        }

        if (!string.IsNullOrWhiteSpace(alert.Condition))
        {
            yield return new KeyValuePair<string, string>("alert_condition", alert.Condition!);
        }

        if (alert.Severity is not null)
        {
            yield return new KeyValuePair<string, string>("alert.severity", ((int)alert.Severity.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (alert.Expires is not null)
        {
            yield return new KeyValuePair<string, string>("alert.expires", alert.Expires);
        }

        if (alert.Track is not null)
        {
            yield return new KeyValuePair<string, string>("alert.track", ToSplunkBool(alert.Track.Value));
        }

        if (alert.DigestMode is not null)
        {
            yield return new KeyValuePair<string, string>("alert.digest_mode", ToSplunkBool(alert.DigestMode.Value));
        }

        foreach (var parameter in BuildSuppressionParameters(alert.Suppression))
        {
            yield return parameter;
        }

        var actions = BuildActionNames(alert).ToArray();
        if (actions.Length > 0)
        {
            yield return new KeyValuePair<string, string>("actions", string.Join(',', actions));
            foreach (var action in actions)
            {
                yield return new KeyValuePair<string, string>($"action.{action}", "1");
            }
        }

        foreach (var parameter in BuildEmailParameters(alert.Email))
        {
            yield return parameter;
        }

        foreach (var parameter in BuildSummaryIndexParameters(alert.SummaryIndex))
        {
            yield return parameter;
        }
    }

    internal static void ValidateCronSchedule(string? cronSchedule, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(cronSchedule))
        {
            throw new ArgumentException("Scheduled saved searches require a cron schedule.", parameterName);
        }
    }

    internal static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A saved search name is required.", nameof(name));
        }

        if (name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException("Saved search names must not contain path separators.", nameof(name));
        }
    }

    private async Task<string> GetStringAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        const string operation = "saved_search.get";
        using var activity = SplunkDiagnostics.ActivitySource.StartActivity("Splunk saved search GET", ActivityKind.Client);
        activity?.SetTag("splunk.operation", operation);

        using var response = await _restClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> PostFormForStringAsync(
        Uri endpoint,
        IReadOnlyList<KeyValuePair<string, string>> parameters,
        CancellationToken cancellationToken)
    {
        using var response = await _restClient.PostFormAsync(endpoint, parameters, cancellationToken).ConfigureAwait(false);
        await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private Uri SavedSearchEndpoint(string name, SplunkNamespace? splunkNamespace) =>
        _endpointBuilder.ServicesEndpoint($"saved/searches/{Uri.EscapeDataString(name)}", splunkNamespace);

    private static SplunkSavedSearch ParseSingleSavedSearch(string body, string fallbackName) =>
        SplunkAtomFeedParser.ParseSavedSearches(body).FirstOrDefault() ??
        new SplunkSavedSearch { Name = fallbackName };

    private static IEnumerable<KeyValuePair<string, string>> BuildCreateParameters(CreateSavedSearchRequest request)
    {
        ValidateAdditionalParameters(request.AdditionalParameters);

        yield return new KeyValuePair<string, string>("name", request.Name);
        yield return new KeyValuePair<string, string>("search", request.Search);
        yield return new KeyValuePair<string, string>("output_mode", "json");

        foreach (var parameter in CommonParameters(
            request.Description,
            request.IsScheduled,
            request.CronSchedule,
            request.TimeRange,
            request.Dispatch,
            request.Disabled,
            request.AdditionalParameters))
        {
            yield return parameter;
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> BuildUpdateParameters(UpdateSavedSearchRequest request)
    {
        ValidateAdditionalParameters(request.AdditionalParameters);

        yield return new KeyValuePair<string, string>("output_mode", "json");

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            yield return new KeyValuePair<string, string>("search", request.Search!);
        }

        foreach (var parameter in CommonParameters(
            request.Description,
            request.IsScheduled,
            request.CronSchedule,
            request.TimeRange,
            request.Dispatch,
            request.Disabled,
            request.AdditionalParameters))
        {
            yield return parameter;
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> CommonParameters(
        string? description,
        bool? isScheduled,
        string? cronSchedule,
        SplunkTimeRange? timeRange,
        SplunkSavedSearchDispatchSettings? dispatch,
        bool? disabled,
        IReadOnlyDictionary<string, string>? additionalParameters)
    {
        if (description is not null)
        {
            yield return new KeyValuePair<string, string>("description", description);
        }

        if (isScheduled is not null)
        {
            yield return new KeyValuePair<string, string>("is_scheduled", ToSplunkBool(isScheduled.Value));
        }

        if (!string.IsNullOrWhiteSpace(cronSchedule))
        {
            yield return new KeyValuePair<string, string>("cron_schedule", cronSchedule!);
        }

        if (timeRange is not null)
        {
            yield return new KeyValuePair<string, string>("dispatch.earliest_time", timeRange.Earliest);
            if (timeRange.Latest is not null)
            {
                yield return new KeyValuePair<string, string>("dispatch.latest_time", timeRange.Latest);
            }
        }

        foreach (var parameter in BuildDispatchParameters(dispatch))
        {
            yield return parameter;
        }

        if (disabled is not null)
        {
            yield return new KeyValuePair<string, string>("disabled", ToSplunkBool(disabled.Value));
        }

        if (additionalParameters is null)
        {
            yield break;
        }

        foreach (var parameter in additionalParameters)
        {
            yield return parameter;
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> BuildDispatchParameters(SplunkSavedSearchDispatchSettings? dispatch)
    {
        if (dispatch is null)
        {
            yield break;
        }

        if (dispatch.Buckets is not null)
        {
            if (dispatch.Buckets < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dispatch.Buckets), "Dispatch bucket count must be zero or greater.");
            }

            yield return new KeyValuePair<string, string>(
                "dispatch.buckets",
                dispatch.Buckets.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (dispatch.MaxCount is not null)
        {
            if (dispatch.MaxCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dispatch.MaxCount), "Dispatch max count must be zero or greater.");
            }

            yield return new KeyValuePair<string, string>(
                "dispatch.max_count",
                dispatch.MaxCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (dispatch.Lookups is not null)
        {
            yield return new KeyValuePair<string, string>("dispatch.lookups", ToSplunkBool(dispatch.Lookups.Value));
        }

        if (dispatch.TimeFormat is not null)
        {
            yield return new KeyValuePair<string, string>("dispatch.time_format", dispatch.TimeFormat);
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> BuildSuppressionParameters(SplunkAlertSuppressionSettings? suppression)
    {
        if (suppression is null)
        {
            yield break;
        }

        if (suppression.Enabled is not null)
        {
            yield return new KeyValuePair<string, string>("alert.suppress", ToSplunkBool(suppression.Enabled.Value));
        }

        if (suppression.Period is not null)
        {
            yield return new KeyValuePair<string, string>("alert.suppress.period", suppression.Period);
        }

        var fields = suppression.Fields ?? Array.Empty<string>();
        if (fields.Count > 0)
        {
            yield return new KeyValuePair<string, string>("alert.suppress.fields", string.Join(',', fields));
        }
    }

    private static IEnumerable<string> BuildActionNames(SplunkAlertSettings alert)
    {
        var actions = new List<string>();
        foreach (var action in alert.Actions ?? Array.Empty<string>())
        {
            AddAction(actions, action);
        }

        if (alert.Email is not null)
        {
            AddAction(actions, "email");
        }

        if (alert.SummaryIndex is not null)
        {
            AddAction(actions, "summary_index");
        }

        return actions;
    }

    private static void AddAction(List<string> actions, string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Alert action names must not be empty.", nameof(action));
        }

        if (!IsSafeActionName(action))
        {
            throw new ArgumentException(
                $"'{action}' is not a safe Splunk alert action name.",
                nameof(action));
        }

        if (!actions.Contains(action, StringComparer.OrdinalIgnoreCase))
        {
            actions.Add(action);
        }
    }

    private static bool IsSafeActionName(string action)
    {
        foreach (var character in action)
        {
            if (!IsAsciiLetterOrDigit(character) && character is not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'A' and <= 'Z' ||
        value is >= 'a' and <= 'z' ||
        value is >= '0' and <= '9';

    private static IEnumerable<KeyValuePair<string, string>> BuildEmailParameters(SplunkEmailAlertActionSettings? email)
    {
        if (email is null)
        {
            yield break;
        }

        var to = email.To ?? Array.Empty<string>();
        var cc = email.Cc ?? Array.Empty<string>();
        var bcc = email.Bcc ?? Array.Empty<string>();

        if (to.Count > 0)
        {
            yield return new KeyValuePair<string, string>("action.email.to", string.Join(',', to));
        }

        if (cc.Count > 0)
        {
            yield return new KeyValuePair<string, string>("action.email.cc", string.Join(',', cc));
        }

        if (bcc.Count > 0)
        {
            yield return new KeyValuePair<string, string>("action.email.bcc", string.Join(',', bcc));
        }

        if (email.Subject is not null)
        {
            yield return new KeyValuePair<string, string>("action.email.subject", email.Subject);
        }

        if (email.Message is not null)
        {
            yield return new KeyValuePair<string, string>("action.email.message.alert", email.Message);
        }

        if (email.AuthUsername is not null)
        {
            yield return new KeyValuePair<string, string>("action.email.auth_username", email.AuthUsername);
        }

        if (email.PdfView is not null)
        {
            yield return new KeyValuePair<string, string>("action.email.pdfview", email.PdfView);
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> BuildSummaryIndexParameters(SplunkSummaryIndexAlertActionSettings? summaryIndex)
    {
        if (summaryIndex?.Name is not null)
        {
            yield return new KeyValuePair<string, string>("action.summary_index._name", summaryIndex.Name);
        }
    }

    private static void ValidateSearch(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            throw new ArgumentException("A saved search SPL string is required.", nameof(search));
        }
    }

    private static void ValidateAdditionalParameters(IReadOnlyDictionary<string, string>? additionalParameters)
    {
        if (additionalParameters is null)
        {
            return;
        }

        foreach (var parameter in additionalParameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Key))
            {
                throw new ArgumentException("Additional saved search parameter names must not be empty.", nameof(additionalParameters));
            }

            if (ReservedSavedSearchParameterNames.Contains(parameter.Key))
            {
                throw new ArgumentException(
                    $"Additional saved search parameter '{parameter.Key}' is controlled by a typed SDK property.",
                    nameof(additionalParameters));
            }
        }
    }

    private static string ToSplunkBool(bool value) => value ? "1" : "0";

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
            }
            catch (JsonException ex)
            {
                throw CreateMalformedDispatchResponseException("JSON", ex);
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
                throw CreateMalformedDispatchResponseException("XML", ex);
            }
        }

        throw new SplunkApiException(System.Net.HttpStatusCode.OK, "OK", string.Empty, [new SplunkMessage("ERROR", "Splunk did not return a search ID.")]);
    }

    private static SplunkApiException CreateMalformedDispatchResponseException(string format, Exception innerException)
    {
        _ = innerException;
        return new SplunkApiException(
            System.Net.HttpStatusCode.OK,
            "OK",
            string.Empty,
            [new SplunkMessage("ERROR", $"Splunk returned malformed {format} for a saved search dispatch response.")]);
    }
}
