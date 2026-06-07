using System.Globalization;
using Marouanvs.Splunk.Diagnostics;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.SavedSearches;

namespace Marouanvs.Splunk.Alerts;

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

        var additional = request.AdditionalParameters is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(request.AdditionalParameters, StringComparer.OrdinalIgnoreCase);
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

        using var activity = SplunkSavedSearchClient.StartOperationActivity("Splunk alert create", "alert.create");
        var completed = false;

        try
        {
            var savedSearch = await _savedSearches.CreateAsync(savedSearchRequest, cancellationToken).ConfigureAwait(false);
            completed = true;
            return savedSearch;
        }
        catch (Exception ex)
        {
            SplunkDiagnostics.SetException(activity, ex);
            throw;
        }
        finally
        {
            activity?.SetTag("splunk.completed", completed);
        }
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
        var endpoint = SavedSearchActionEndpoint(name, "acknowledge", splunkNamespace);
        await PostFormAsync(endpoint, [], "Splunk alert acknowledge", "alert.acknowledge", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SuppressAsync(
        string name,
        TimeSpan expiration,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default)
    {
        SplunkSavedSearchClient.ValidateName(name);
        if (expiration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiration),
                "Alert suppression expiration must be greater than zero.");
        }

        var seconds = (long)Math.Ceiling(expiration.TotalSeconds);
        var endpoint = SavedSearchActionEndpoint(name, "suppress", splunkNamespace);
        var parameters = new[]
        {
            new KeyValuePair<string, string>("expiration", seconds.ToString(CultureInfo.InvariantCulture))
        };
        await PostFormAsync(endpoint, parameters, "Splunk alert suppress", "alert.suppress", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnsuppressAsync(
        string name,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default)
    {
        SplunkSavedSearchClient.ValidateName(name);
        var endpoint = SavedSearchActionEndpoint(name, "suppress", splunkNamespace);
        var parameters = new[]
        {
            new KeyValuePair<string, string>("expiration", "0")
        };
        await PostFormAsync(endpoint, parameters, "Splunk alert unsuppress", "alert.unsuppress", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SplunkAlertSuppression> GetSuppressionAsync(
        string name,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default)
    {
        SplunkSavedSearchClient.ValidateName(name);

        using var activity = SplunkSavedSearchClient.StartOperationActivity("Splunk alert suppression get", "alert.suppression.get");
        var completed = false;

        try
        {
            var endpoint = SavedSearchActionEndpoint(name, "suppress", splunkNamespace);
            endpoint = _endpointBuilder.AppendQuery(endpoint, [new KeyValuePair<string, string>("output_mode", "json")]);

            var body = await GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var suppression = SplunkAlertResponseParser.ParseSuppression(body);
            completed = true;
            return suppression;
        }
        catch (Exception ex)
        {
            SplunkDiagnostics.SetException(activity, ex);
            throw;
        }
        finally
        {
            activity?.SetTag("splunk.completed", completed);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SplunkFiredAlertGroup>> ListFiredAlertGroupsAsync(
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = SplunkSavedSearchClient.StartOperationActivity("Splunk fired alert groups list", "alert.fired.groups");
        var completed = false;

        try
        {
            var endpoint = _endpointBuilder.ServicesEndpoint("alerts/fired_alerts", splunkNamespace);
            endpoint = _endpointBuilder.AppendQuery(endpoint, [new KeyValuePair<string, string>("output_mode", "json")]);

            var body = await GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var groups = SplunkAlertResponseParser.ParseFiredAlertGroups(body);
            completed = true;
            return groups;
        }
        catch (Exception ex)
        {
            SplunkDiagnostics.SetException(activity, ex);
            throw;
        }
        finally
        {
            activity?.SetTag("splunk.completed", completed);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SplunkFiredAlert>> ListFiredAlertsAsync(
        string savedSearchName,
        SplunkNamespace? splunkNamespace = null,
        CancellationToken cancellationToken = default)
    {
        SplunkSavedSearchClient.ValidateName(savedSearchName);

        using var activity = SplunkSavedSearchClient.StartOperationActivity("Splunk fired alerts list", "alert.fired.list");
        var completed = false;

        try
        {
            var endpoint = _endpointBuilder.ServicesEndpoint(
                $"alerts/fired_alerts/{Uri.EscapeDataString(savedSearchName)}",
                splunkNamespace);
            endpoint = _endpointBuilder.AppendQuery(endpoint, [new KeyValuePair<string, string>("output_mode", "json")]);

            var body = await GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var alerts = SplunkAlertResponseParser.ParseFiredAlerts(body);
            completed = true;
            return alerts;
        }
        catch (Exception ex)
        {
            SplunkDiagnostics.SetException(activity, ex);
            throw;
        }
        finally
        {
            activity?.SetTag("splunk.completed", completed);
        }
    }

    private Uri SavedSearchActionEndpoint(string name, string action, SplunkNamespace? splunkNamespace) =>
        _endpointBuilder.ServicesEndpoint($"saved/searches/{Uri.EscapeDataString(name)}/{action}", splunkNamespace);

    private async Task PostFormAsync(
        Uri endpoint,
        IReadOnlyList<KeyValuePair<string, string>> parameters,
        string activityName,
        string operation,
        CancellationToken cancellationToken)
    {
        using var activity = SplunkSavedSearchClient.StartOperationActivity(activityName, operation);
        var completed = false;

        try
        {
            using var response = await _restClient.PostFormAsync(endpoint, parameters, cancellationToken).ConfigureAwait(false);
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
        }
    }

    private async Task<string> GetStringAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var response = await _restClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        await _restClient.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}
