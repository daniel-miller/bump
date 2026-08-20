using Bump.Api.Services;
using Xunit;

namespace Bump.Api.Tests;

public sealed class OutagePolicyTests
{
    private static List<string> History(params string[] statuses) => statuses.ToList();

    [Fact]
    public void TrailingDownStreak_counts_only_the_trailing_run()
    {
        var history = History(
            ServiceStatuses.Down,        // earlier run, does not count
            ServiceStatuses.Operational, // breaks it
            ServiceStatuses.Down,
            ServiceStatuses.Down);
        Assert.Equal(2, OutagePolicy.TrailingDownStreak(history));
    }

    [Fact]
    public void Degraded_breaks_the_run()
    {
        // A slow-but-reachable probe is not down and must reset the streak.
        var history = History(
            ServiceStatuses.Down,
            ServiceStatuses.Degraded,
            ServiceStatuses.Down);
        Assert.Equal(1, OutagePolicy.TrailingDownStreak(history));
    }

    [Fact]
    public void Empty_history_has_no_streak()
    {
        Assert.Equal(0, OutagePolicy.TrailingDownStreak(new List<string>()));
    }

    [Theory]
    [InlineData(1, 3, false)] // one failure, threshold 3 -> not yet
    [InlineData(2, 3, false)] // two failures, threshold 3 -> not yet
    [InlineData(3, 3, true)]  // threshold reached
    [InlineData(4, 3, true)]  // still open past the threshold
    public void OutageConfirmed_requires_threshold_consecutive_downs(int downCount, int threshold, bool expected)
    {
        var history = Enumerable.Repeat(ServiceStatuses.Down, downCount).ToList();
        Assert.Equal(expected, OutagePolicy.OutageConfirmed(history, threshold));
    }

    [Fact]
    public void A_single_operational_probe_before_the_run_still_confirms()
    {
        var history = History(
            ServiceStatuses.Operational,
            ServiceStatuses.Down,
            ServiceStatuses.Down,
            ServiceStatuses.Down);
        Assert.True(OutagePolicy.OutageConfirmed(history, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Threshold_of_one_or_less_opens_on_the_first_down(int threshold)
    {
        var history = History(ServiceStatuses.Down);
        Assert.True(OutagePolicy.OutageConfirmed(history, threshold));
    }

    [Fact]
    public void Threshold_of_one_does_not_open_on_a_reachable_probe()
    {
        Assert.False(OutagePolicy.OutageConfirmed(History(ServiceStatuses.Degraded), 1));
        Assert.False(OutagePolicy.OutageConfirmed(History(ServiceStatuses.Operational), 1));
    }
}
