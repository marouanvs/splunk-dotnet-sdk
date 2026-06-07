using System.Globalization;

namespace SplunkSdk.Models;

/// <summary>
/// Represents Splunk <c>earliest_time</c> and <c>latest_time</c> request parameters.
/// </summary>
public sealed record SplunkTimeRange
{
    private SplunkTimeRange(string earliest, string? latest)
    {
        Earliest = ValidateModifier(earliest, nameof(earliest));
        Latest = latest is null ? null : ValidateModifier(latest, nameof(latest));
    }

    /// <summary>
    /// Gets the earliest Splunk time modifier.
    /// </summary>
    public string Earliest { get; }

    /// <summary>
    /// Gets the latest Splunk time modifier.
    /// </summary>
    public string? Latest { get; }

    /// <summary>
    /// Creates a range from raw Splunk time modifiers, for example <c>-24h</c> to <c>now</c>.
    /// </summary>
    /// <param name="earliest">Splunk earliest time modifier.</param>
    /// <param name="latest">Splunk latest time modifier, or <c>now</c> by default.</param>
    /// <returns>A time range encoded for Splunk REST parameters.</returns>
    public static SplunkTimeRange Relative(string earliest, string latest = "now") => new(earliest, latest);

    /// <summary>
    /// Creates a range covering the last duration up to <c>now</c>.
    /// </summary>
    /// <param name="duration">Positive duration ending at <c>now</c>.</param>
    /// <returns>A relative Splunk time range such as <c>-15m</c> to <c>now</c>.</returns>
    public static SplunkTimeRange Last(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be greater than zero.");
        }

        return new SplunkTimeRange("-" + FormatDuration(duration), "now");
    }

    /// <summary>
    /// Creates a range from absolute UTC instants, encoded as Unix epoch seconds.
    /// </summary>
    /// <param name="earliest">Inclusive earliest instant.</param>
    /// <param name="latest">Exclusive latest instant, which must be after <paramref name="earliest"/>.</param>
    /// <returns>An absolute Splunk time range encoded as Unix epoch seconds.</returns>
    public static SplunkTimeRange Absolute(DateTimeOffset earliest, DateTimeOffset latest)
    {
        if (latest <= earliest)
        {
            throw new ArgumentException("Latest must be after earliest.", nameof(latest));
        }

        return new SplunkTimeRange(
            earliest.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            latest.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Creates a range covering all historical events up to <c>now</c>.
    /// </summary>
    /// <returns>A Splunk all-time range.</returns>
    /// <remarks>Use all-time searches sparingly because they can be expensive on large indexes.</remarks>
    public static SplunkTimeRange AllTime() => new("1", "now");

    internal IEnumerable<KeyValuePair<string, string>> ToFormParameters()
    {
        yield return new KeyValuePair<string, string>("earliest_time", Earliest);

        if (Latest is not null)
        {
            yield return new KeyValuePair<string, string>("latest_time", Latest);
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1 && duration.TotalDays % 1 == 0)
        {
            return $"{duration.TotalDays.ToString("0", CultureInfo.InvariantCulture)}d";
        }

        if (duration.TotalHours >= 1 && duration.TotalHours % 1 == 0)
        {
            return $"{duration.TotalHours.ToString("0", CultureInfo.InvariantCulture)}h";
        }

        if (duration.TotalMinutes >= 1 && duration.TotalMinutes % 1 == 0)
        {
            return $"{duration.TotalMinutes.ToString("0", CultureInfo.InvariantCulture)}m";
        }

        return $"{Math.Ceiling(duration.TotalSeconds).ToString("0", CultureInfo.InvariantCulture)}s";
    }

    private static string ValidateModifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A Splunk time modifier is required.", parameterName);
        }

        if (value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Splunk time modifiers must not contain whitespace.", parameterName);
        }

        return value;
    }
}
