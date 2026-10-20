using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MissionControl.Core;

namespace MissionControl.Desktop;

public sealed class LaunchRow : INotifyPropertyChanged
{
    private string _countdownLabel = "CALCULATING";
    private string _countdownExplanation = "Waiting for schedule information.";

    public LaunchRow(LaunchLibrary2Launch launch)
    {
        Source = launch ?? throw new ArgumentNullException(nameof(launch));
        RefreshCountdown(DateTimeOffset.UtcNow);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LaunchLibrary2Launch Source { get; }

    public LaunchState State => Source.State;

    public string Name => State.Name;

    public string StatusName => Source.StatusName;

    public string ProviderName => Source.ProviderName ?? "Provider unavailable";

    public string MetadataLine
    {
        get
        {
            var details = new[] { Source.ProviderName, Source.LocationName }
                .Where(value => !string.IsNullOrWhiteSpace(value));
            var line = string.Join("  ·  ", details);
            return string.IsNullOrWhiteSpace(line) ? "Launch-site details unavailable" : line;
        }
    }

    public string ScheduledLabel => State.ScheduledAtUtc is { } scheduledAt
        ? scheduledAt.ToLocalTime().ToString("ddd, MMM d · h:mm tt", CultureInfo.CurrentCulture)
        : "Launch time unavailable";

    public string StateLabel => State.Phase switch
    {
        LaunchPhase.Scheduled => "SCHEDULED",
        LaunchPhase.Delayed => "ON HOLD",
        LaunchPhase.Scrubbed => "SCRUBBED",
        LaunchPhase.Completed => "COMPLETED",
        _ => $"UNVERIFIED · {StatusName.ToUpperInvariant()}"
    };

    public string CountdownLabel
    {
        get => _countdownLabel;
        private set => SetField(ref _countdownLabel, value);
    }

    public string CountdownExplanation
    {
        get => _countdownExplanation;
        private set => SetField(ref _countdownExplanation, value);
    }

    public void RefreshCountdown(DateTimeOffset nowUtc)
    {
        var snapshot = CountdownEngine.GetSnapshot(State, nowUtc);
        CountdownLabel = snapshot.Label;
        CountdownExplanation = snapshot.Explanation;
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
