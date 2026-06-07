using SplunkSdk.Authentication;
using SplunkSdk.Models;

namespace SplunkSdk;

internal static class SplunkEnumExtensions
{
    public static string ToHeaderValue(this SplunkAuthorizationScheme scheme) =>
        scheme switch
        {
            SplunkAuthorizationScheme.Bearer => "Bearer",
            SplunkAuthorizationScheme.Splunk => "Splunk",
            _ => throw new ArgumentOutOfRangeException(nameof(scheme), scheme, null)
        };

    public static string ToSplunkValue(this SplunkOutputMode mode) =>
        mode switch
        {
            SplunkOutputMode.Json => "json",
            SplunkOutputMode.JsonColumns => "json_cols",
            SplunkOutputMode.JsonRows => "json_rows",
            SplunkOutputMode.Csv => "csv",
            SplunkOutputMode.Raw => "raw",
            SplunkOutputMode.Xml => "xml",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

    public static string ToSplunkValue(this SplunkExecutionMode mode) =>
        mode switch
        {
            SplunkExecutionMode.Normal => "normal",
            SplunkExecutionMode.Blocking => "blocking",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

    public static string ToSplunkValue(this SplunkAlertType type) =>
        type switch
        {
            SplunkAlertType.Always => "always",
            SplunkAlertType.Custom => "custom",
            SplunkAlertType.NumberOfEvents => "number of events",
            SplunkAlertType.NumberOfHosts => "number of hosts",
            SplunkAlertType.NumberOfSources => "number of sources",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

    public static string ToSplunkValue(this SplunkAlertComparator comparator) =>
        comparator switch
        {
            SplunkAlertComparator.GreaterThan => "greater than",
            SplunkAlertComparator.LessThan => "less than",
            SplunkAlertComparator.EqualTo => "equal to",
            SplunkAlertComparator.RisesBy => "rises by",
            SplunkAlertComparator.DropsBy => "drops by",
            SplunkAlertComparator.RisesByPercentage => "rises by perc",
            SplunkAlertComparator.DropsByPercentage => "drops by perc",
            _ => throw new ArgumentOutOfRangeException(nameof(comparator), comparator, null)
        };

    public static SplunkAlertType? TryParseAlertType(string? value) =>
        value switch
        {
            "always" => SplunkAlertType.Always,
            "custom" => SplunkAlertType.Custom,
            "number of events" => SplunkAlertType.NumberOfEvents,
            "number of hosts" => SplunkAlertType.NumberOfHosts,
            "number of sources" => SplunkAlertType.NumberOfSources,
            _ => null
        };

    public static SplunkAlertComparator? TryParseAlertComparator(string? value) =>
        value switch
        {
            "greater than" => SplunkAlertComparator.GreaterThan,
            "less than" => SplunkAlertComparator.LessThan,
            "equal to" => SplunkAlertComparator.EqualTo,
            "rises by" => SplunkAlertComparator.RisesBy,
            "drops by" => SplunkAlertComparator.DropsBy,
            "rises by perc" => SplunkAlertComparator.RisesByPercentage,
            "drops by perc" => SplunkAlertComparator.DropsByPercentage,
            _ => null
        };
}
