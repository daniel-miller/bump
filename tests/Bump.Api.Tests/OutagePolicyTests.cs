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

    [Fact]
    public void TrailingUpStreak_counts_degraded_as_up()
    {
        // A slow answer is still an answer, on the way back up as on the way down.
        var history = History(
            ServiceStatuses.Down,
            ServiceStatuses.Operational,
            ServiceStatuses.Degraded,
            ServiceStatuses.Operational);
        Assert.Equal(3, OutagePolicy.TrailingUpStreak(history));
    }

    [Theory]
    [InlineData(1, 3, false)]
    [InlineData(2, 3, false)]
    [InlineData(3, 3, true)]
    public void RecoveryConfirmed_requires_threshold_consecutive_answers(int upCount, int threshold, bool expected)
    {
        var history = History(ServiceStatuses.Down, ServiceStatuses.Down, ServiceStatuses.Down)
            .Concat(Enumerable.Repeat(ServiceStatuses.Operational, upCount)).ToList();
        Assert.Equal(expected, OutagePolicy.RecoveryConfirmed(history, threshold));
    }

    private static readonly List<string> ThreeDown = History(ServiceStatuses.Down, ServiceStatuses.Down, ServiceStatuses.Down);
    private static readonly List<string> ThreeUp = History(ServiceStatuses.Operational, ServiceStatuses.Operational, ServiceStatuses.Operational);
    private static readonly List<string> Flapping = History(ServiceStatuses.Down, ServiceStatuses.Operational, ServiceStatuses.Down);

    [Fact]
    public void Decide_opens_on_confirmed_failure_with_nothing_open()
    {
        Assert.Equal(OutageAction.Open, OutagePolicy.Decide(ThreeDown, 3, hasOpenOutage: false, openOutageAutoCreated: false));
    }

    [Fact]
    public void Decide_does_not_open_a_second_outage()
    {
        Assert.Equal(OutageAction.None, OutagePolicy.Decide(ThreeDown, 3, hasOpenOutage: true, openOutageAutoCreated: true));
    }

    [Fact]
    public void Decide_resolves_an_outage_it_opened_once_recovered()
    {
        Assert.Equal(OutageAction.Resolve, OutagePolicy.Decide(ThreeUp, 3, hasOpenOutage: true, openOutageAutoCreated: true));
    }

    [Fact]
    public void Decide_leaves_an_admin_outage_for_the_admin()
    {
        Assert.Equal(OutageAction.None, OutagePolicy.Decide(ThreeUp, 3, hasOpenOutage: true, openOutageAutoCreated: false));
    }

    [Fact]
    public void Decide_does_nothing_for_a_healthy_service()
    {
        Assert.Equal(OutageAction.None, OutagePolicy.Decide(ThreeUp, 3, hasOpenOutage: false, openOutageAutoCreated: false));
    }

    [Fact]
    public void Decide_neither_opens_nor_resolves_while_flapping()
    {
        // One answer between failures is not a recovery, and one failure is not an outage.
        Assert.Equal(OutageAction.None, OutagePolicy.Decide(Flapping, 3, hasOpenOutage: true, openOutageAutoCreated: true));
        Assert.Equal(OutageAction.None, OutagePolicy.Decide(Flapping, 3, hasOpenOutage: false, openOutageAutoCreated: false));
    }
}
