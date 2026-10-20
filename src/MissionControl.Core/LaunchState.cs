namespace MissionControl.Core;

public enum LaunchPhase
{
    Unknown,
    Scheduled,
    Delayed,
    Scrubbed,
    LiftoffConfirmed,
    InFlight,
    Completed
}

public sealed record LaunchState(
    string LaunchId,
    string Name,
    DateTimeOffset? ScheduledAtUtc,
    LaunchPhase Phase,
    string StatusSource,
    DateTimeOffset StatusObservedAtUtc,
    DateTimeOffset? LiftoffAtUtc = null,
    string? StatusNote = null);

public enum CountdownMode
{
    Unavailable,
    TMinus,
    TPlus,
    Ended,
    Hold
}

public sealed record CountdownSnapshot(
    CountdownMode Mode,
    TimeSpan? Offset,
    string Label,
    string Explanation);

public static class CountdownEngine
{
    /// <summary>
    /// Computes display mode from known launch state. Time passing by itself
    /// never confirms liftoff and therefore never changes T- to T+.
    /// </summary>
    public static CountdownSnapshot GetSnapshot(LaunchState launch, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(launch);

        switch (launch.Phase)
        {
            case LaunchPhase.LiftoffConfirmed:
            case LaunchPhase.InFlight:
                if (launch.LiftoffAtUtc is not { } liftoffAt)
                {
                    return new CountdownSnapshot(
                        CountdownMode.Unavailable,
                        null,
                        "LIFTOFF TIME UNCONFIRMED",
                        $"Status from {launch.StatusSource} indicates flight, but no confirmed liftoff timestamp is available.");
                }

                var elapsed = nowUtc - liftoffAt;
                if (elapsed < TimeSpan.Zero)
                {
                    return new CountdownSnapshot(
                        CountdownMode.Unavailable,
                        null,
                        "CHECKING LIFTOFF TIME",
                        "The reported liftoff time is in the future; waiting for consistent status data.");
                }

                return new CountdownSnapshot(
                    CountdownMode.TPlus,
                    elapsed,
                    FormatTPlus(elapsed),
                    $"Liftoff confirmation reported by {launch.StatusSource}.");

            case LaunchPhase.Completed:
                return new CountdownSnapshot(
                    CountdownMode.Ended,
                    null,
                    "MISSION COMPLETE",
                    "The mission is marked complete.");

            case LaunchPhase.Delayed:
                return new CountdownSnapshot(
                    CountdownMode.Hold,
                    null,
                    "DELAYED",
                    launch.StatusNote ?? "The launch has been delayed. Waiting for an updated schedule.");

            case LaunchPhase.Scrubbed:
                return new CountdownSnapshot(
                    CountdownMode.Hold,
                    null,
                    "SCRUBBED",
                    launch.StatusNote ?? "This launch attempt has been scrubbed.");

            case LaunchPhase.Unknown:
                return new CountdownSnapshot(
                    CountdownMode.Unavailable,
                    null,
                    "STATUS UNKNOWN",
                    "Launch status is not sufficiently confirmed.");

            case LaunchPhase.Scheduled:
                if (launch.ScheduledAtUtc is not { } scheduledAt)
                {
                    return new CountdownSnapshot(
                        CountdownMode.Unavailable,
                        null,
                        "TIME UNAVAILABLE",
                        "No scheduled launch time is available.");
                }

                // If the scheduled time has passed but no liftoff confirmation
                // exists, hold at zero; do not infer a T+ state from the clock.
                if (nowUtc >= scheduledAt)
                {
                    return new CountdownSnapshot(
                        CountdownMode.TMinus,
                        TimeSpan.Zero,
                        "T−00:00:00",
                        "Scheduled time has passed; liftoff has not been confirmed.");
                }

                var remaining = scheduledAt - nowUtc;
                return new CountdownSnapshot(
                    CountdownMode.TMinus,
                    remaining,
                    FormatTMinus(remaining),
                    "Counting down to the currently scheduled launch time.");

            default:
                throw new ArgumentOutOfRangeException(nameof(launch), "Unsupported launch phase.");
        }
    }

    private static string FormatTMinus(TimeSpan remaining)
    {
        var totalHours = (long)Math.Floor(remaining.TotalHours);
        return $"T−{totalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private static string FormatTPlus(TimeSpan elapsed)
    {
        var totalHours = (long)Math.Floor(elapsed.TotalHours);
        return $"T+{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
}
