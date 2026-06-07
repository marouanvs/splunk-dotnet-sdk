using SplunkSdk.Models;
using SplunkSdk.SavedSearches;

namespace SplunkSdk.Alerts;

/// <summary>
/// Default alert client implemented over Splunk saved search endpoints.
/// </summary>
public sealed class SplunkAlertClient : ISplunkAlertClient
{
    private readonly ISplunkSavedSearchClient _savedSearches;
    private readonly SplunkRestClient _restClient;
    private readonly SplunkEndpointBuilder _endpointBuilder;

    internal SplunkAlertClient(
        ISplunkSavedSearchClient savedSearches,
        SplunkRestClient restClient,
        SplunkEndpointBuilder endpointBuilder)
    {
        _savedSearches = savedSearches;
        _restClient = restClient;
        _endpointBuilder = endpointBuilder;
    }

    /// <inheritdoc />
    public async Task<SplunkSavedSearch> CreateAsync(
        CreateSplunkAlertRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        SplunkSavedSearchClient.ValidateCronSchedule(request.CronSchedule, nameof(request.CronSchedule));

        var additional = new Dictionary<string, string>(request.AdditionalParameters, StringComparer.OrdinalIgnoreCase);
        ValidateAlertSettings(request.Alert, additional);
        foreach (var parameter in SplunkSavedSearchClient.BuildAlertParameters(request.Alert))
        {
            if (additional.ContainsKey(parameter.Key))
            {
                throw new ArgumentException(
                    $"Additional alert parameter '{parameter.Key}' is controlled by a typed alert setting.",
                    nameof(request));
            }

            additional[parameter.Key] = parameter.Value;
        }

        var savedSearchRequest = new CreateSavedSearchRequest(request.Name, request.Search)
        {
            Namespace = request.Namespace,
            Description = request.Description,
            IsScheduled = true,
            CronSchedule = request.CronSchedule,
            TimeRange = request.TimeRange,
            Disabled = request.Disabled,
            AdditionalParameters = additional
        };

        return await _savedSearches.CreateAsync(savedSearchRequest, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateAlertSettings(
        SplunkAlertSettings alert,
        IReadOnlyDictionary<string, string> additionalParameters)
    {
        ArgumentNullException.ThrowIfNull(alert);

        if (alert.Condition is not null && alert.AlertType is not null && alert.AlertType != SplunkAlertType.Custom)
        {
            throw new ArgumentException(
                "Custom alert conditions must use SplunkAlertType.Custom or leave AlertType unset.",
                nameof(alert));
        }

        if (IsEmailActionEnabled(alert, additionalParameters) &&
            (alert.Email?.To?.Count ?? 0) == 0 &&
            !additionalParameters.ContainsKey("action.email.to"))
        {
            throw new ArgumentException(
                "Email alert actions require at least one recipient in Email.To or AdditionalParameters[\"action.email.to\"].",
                nameof(alert));
        }

        if (alert.Suppression?.Enabled == true &&
            string.IsNullOrWhiteSpace(alert.Suppression.Period))
        {
            throw new ArgumentException("Enabled alert suppression requires a suppression period.", nameof(alert));
        }
    }

    private static bool IsEmailActionEnabled(
        SplunkAlertSettings alert,
        IReadOnlyDictionary<string, string> additionalParameters)
    {
        if (alert.Email is not null ||
            (alert.Actions ?? Array.Empty<string>()).Any(action => string.Equals(action, "email", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return additionalParameters.TryGetValue("actions", out var actions) &&
            actions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(action => string.Equals(action, "email", StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task AcknowledgeAsync(
        string name,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default)
    {
        SplunkSavedSearchClient.ValidateName(name);
        var endpoint = _endpointBuilder.ServicesEndpoint($"saved/searches/{Uri.EscapeDataString(name)}/acknowledge", splunkNamespace);
        using var response = await _restClient.PostFormAsync(endpoint, [], cancellationToken).ConfigureAwait(false);
        await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SuppressAsync(
        string name,
        string period,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default)
    {
        SplunkSavedSearchClient.ValidateName(name);
        if (string.IsNullOrWhiteSpace(period))
        {
            throw new ArgumentException("An alert suppression period is required.", nameof(period));
        }

        var endpoint = _endpointBuilder.ServicesEndpoint($"saved/searches/{Uri.EscapeDataString(name)}/suppress", splunkNamespace);
        var parameters = new[] { new KeyValuePair<string, string>("period", period) };
        using var response = await _restClient.PostFormAsync(endpoint, parameters, cancellationToken).ConfigureAwait(false);
        await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }
}
