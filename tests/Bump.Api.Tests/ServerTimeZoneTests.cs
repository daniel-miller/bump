namespace Bump.Api.Tests;

// Admin screens send the wall-clock time an operator typed, and the server reads it in
// its own zone. A browser on the wrong zone once scheduled an announcement an hour late.
public class ServerTimeZoneTests
{
    private static readonly ServerTimeZone Denver = new(TimeZoneInfo.FindSystemTimeZoneById("America/Denver"));

    // A zone with no clock changes at all, like Alberta's permanent UTC-6.
    private static readonly ServerTimeZone FixedMinusSix = new(
        TimeZoneInfo.CreateCustomTimeZone("Test Fixed", TimeSpan.FromHours(-6), "Test Fixed", "Test Fixed"));

    [Fact]
    public void Reads_a_summer_time_with_the_daylight_offset()
    {
        Assert.True(Denver.TryToUtc("2026-10-22T14:30", out var utc, out _));
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 20, 30, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void Reads_a_winter_time_with_the_standard_offset()
    {
        Assert.True(Denver.TryToUtc("2026-01-15T14:30", out var utc, out _));
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 21, 30, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void A_zone_without_clock_changes_keeps_its_offset_in_winter()
    {
        Assert.True(FixedMinusSix.TryToUtc("2026-12-15T14:30", out var utc, out _));
        Assert.Equal(new DateTimeOffset(2026, 12, 15, 20, 30, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void Refuses_the_hour_the_clocks_skip()
    {
        Assert.False(Denver.TryToUtc("2026-03-08T02:30", out _, out var error));
        Assert.Contains("does not exist", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tomorrow")]
    [InlineData("2026-10-22T14:30:00-06:00")]
    [InlineData("22/10/2026 14:30")]
    public void Refuses_text_that_is_not_a_plain_date_and_time(string text)
    {
        Assert.False(Denver.TryToUtc(text, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Formats_an_instant_as_the_zone_wall_clock_with_its_offset()
    {
        Assert.Equal("2026-10-22 15:30 UTC-06:00", Denver.Format(new DateTimeOffset(2026, 10, 22, 21, 30, 0, TimeSpan.Zero)));
        Assert.Equal("2026-12-15 14:30 UTC-06:00", FixedMinusSix.Format(new DateTimeOffset(2026, 12, 15, 20, 30, 0, TimeSpan.Zero)));
    }
}
