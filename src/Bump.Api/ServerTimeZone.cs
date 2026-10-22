using System.Globalization;

namespace Bump.Api;

/// <summary>
/// The API host's own time zone, in which admin screens take and show wall-clock times.
/// The browser is not trusted with it: in October 2026 Chrome did not recognise Windows'
/// new "Mountain Time (Alberta)" zone, fell back to a fixed UTC-7, and an announcement
/// typed for 14:30 was scheduled for 15:30. The server reads its zone from Windows, which
/// carries the rule, so the browser sends the time as typed and the server places it.
/// </summary>
public sealed class ServerTimeZone(TimeZoneInfo zone)
{
    private static readonly string[] WallClockFormats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];

    public TimeZoneInfo Zone { get; } = zone;

    /// <summary>The zone as Windows names it, such as "(UTC-06:00) Mountain Time (Alberta)".</summary>
    public string DisplayName => Zone.DisplayName;

    /// <summary>
    /// The instant a wall-clock time in this zone names. Fails with a message when the text
    /// is not a date and time, or names a time the zone skips (the hour lost when clocks go
    /// forward). A time the zone repeats (the hour when clocks go back) is read as standard
    /// time, as <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> does.
    /// </summary>
    public bool TryToUtc(string wallClock, out DateTimeOffset utc, out string? error)
    {
        utc = default;
        if (!DateTime.TryParseExact(wallClock, WallClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            error = $"'{wallClock}' is not a date and time in the form yyyy-MM-ddTHH:mm.";
            return false;
        }
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(local))
        {
            error = $"{local:yyyy-MM-dd HH:mm} does not exist in {DisplayName}; the clocks skip that hour.";
            return false;
        }
        utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Zone), TimeSpan.Zero);
        error = null;
        return true;
    }

    /// <summary>An instant as this zone's wall clock with its offset: "2026-10-22 15:30 UTC-06:00".</summary>
    public string Format(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Zone);
        return $"{local:yyyy-MM-dd HH:mm} UTC{local:zzz}";
    }
}
