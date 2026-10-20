using MissionControl.Core;

namespace MissionControl.Core.Tests;

public sealed class CountdownEngineTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 19, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FutureScheduleProducesTMinus()
    {
        var launch = NewLaunch(LaunchPhase.Scheduled, Now.AddMinutes(5));

        var result = CountdownEngine.GetSnapshot(launch, Now);

        Assert.Equal(CountdownMode.TMinus, result.Mode);
        Assert.Equal(TimeSpan.FromMinutes(5), result.Offset);
        Assert.StartsWith("T−", result.Label);
    }

    [Fact]
    public void PassingScheduledTimeDoesNotConfirmLiftoff()
    {
        var launch = NewLaunch(LaunchPhase.Scheduled, Now.AddMinutes(-2));

        var result = CountdownEngine.GetSnapshot(launch, Now);

        Assert.Equal(CountdownMode.TMinus, result.Mode);
        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.Contains("not been confirmed", result.Explanation);
    }

    [Fact]
    public void ConfirmedLiftoffProducesTPlusFromLiftoffTimestamp()
    {
        var launch = NewLaunch(
            LaunchPhase.InFlight,
            Now.AddMinutes(-10),
            liftoffAt: Now.AddMinutes(-3));

        var result = CountdownEngine.GetSnapshot(launch, Now);

        Assert.Equal(CountdownMode.TPlus, result.Mode);
        Assert.Equal(TimeSpan.FromMinutes(3), result.Offset);
        Assert.StartsWith("T+", result.Label);
    }

    [Fact]
    public void InFlightWithoutLiftoffTimestampDoesNotInventTPlus()
    {
        var launch = NewLaunch(LaunchPhase.InFlight, Now.AddMinutes(-5));

        var result = CountdownEngine.GetSnapshot(launch, Now);

        Assert.Equal(CountdownMode.Unavailable, result.Mode);
        Assert.Contains("UNCONFIRMED", result.Label);
    }

    [Theory]
    [InlineData(LaunchPhase.Delayed, "DELAYED")]
    [InlineData(LaunchPhase.Scrubbed, "SCRUBBED")]
    public void DelayOrScrubProducesHold(LaunchPhase phase, string label)
    {
        var launch = NewLaunch(phase, Now.AddMinutes(5), note: "Waiting on conditions.");

        var result = CountdownEngine.GetSnapshot(launch, Now);

        Assert.Equal(CountdownMode.Hold, result.Mode);
        Assert.Equal(label, result.Label);
        Assert.Equal("Waiting on conditions.", result.Explanation);
    }

    private static LaunchState NewLaunch(
        LaunchPhase phase,
        DateTimeOffset? scheduledAt,
        DateTimeOffset? liftoffAt = null,
        string? note = null) =>
        new(
            "test-launch",
            "Test Mission",
            scheduledAt,
            phase,
            "Unit test source",
            Now,
            liftoffAt,
            note);
}
