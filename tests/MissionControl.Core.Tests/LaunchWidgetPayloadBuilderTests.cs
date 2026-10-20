using System.Text.Json;
using MissionControl.Core;
using Xunit;

namespace MissionControl.Core.Tests;

public sealed class LaunchWidgetPayloadBuilderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 20, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FutureScheduleShowsTMinusAndTrustedRecordAction()
    {
        var launch = NewLaunch(
            LaunchPhase.Scheduled,
            Now.AddMinutes(30),
            statusName: "Go for Launch",
            detailsUrl: "https://ll.thespacedevs.com/2.3.0/launches/widget-test/");

        var payload = LaunchWidgetPayloadBuilder.Create(launch, Now);

        using var template = JsonDocument.Parse(payload.TemplateJson);
        using var data = JsonDocument.Parse(payload.DataJson);
        Assert.Equal("AdaptiveCard", template.RootElement.GetProperty("type").GetString());
        Assert.Equal("T−00:30:00", data.RootElement.GetProperty("countdownLabel").GetString());
        Assert.Contains("SCHEDULED", data.RootElement.GetProperty("statusLabel").GetString());
        Assert.Equal(
            "https://ll.thespacedevs.com/2.3.0/launches/widget-test/",
            template.RootElement.GetProperty("actions")[0].GetProperty("url").GetString());
    }

    [Fact]
    public void PassedScheduledTimeStaysAtZeroAndExplainsUnconfirmedLiftoff()
    {
        var launch = NewLaunch(LaunchPhase.Scheduled, Now.AddMinutes(-2));

        var payload = LaunchWidgetPayloadBuilder.Create(launch, Now);

        using var data = JsonDocument.Parse(payload.DataJson);
        var root = data.RootElement;
        Assert.Equal("T−00:00:00", root.GetProperty("countdownLabel").GetString());
        Assert.Contains("liftoff has not been confirmed", root.GetProperty("explanation").GetString());
        Assert.DoesNotContain("T+", root.GetProperty("countdownLabel").GetString());
    }

    [Fact]
    public void ScheduleFeedCannotTurnReportedInFlightIntoTPlus()
    {
        var launch = NewLaunch(
            LaunchPhase.Unknown,
            Now.AddMinutes(-5),
            statusName: "Launch in Flight");

        var payload = LaunchWidgetPayloadBuilder.Create(launch, Now);

        using var data = JsonDocument.Parse(payload.DataJson);
        Assert.Equal("LIFTOFF TIME UNCONFIRMED",
            data.RootElement.GetProperty("countdownLabel").GetString());
        Assert.Contains("STATUS UNVERIFIED",
            data.RootElement.GetProperty("statusLabel").GetString());
    }

    [Fact]
    public void MissingScheduleCreatesHonestUnavailableCard()
    {
        var payload = LaunchWidgetPayloadBuilder.Create(null, Now);

        using var data = JsonDocument.Parse(payload.DataJson);
        Assert.Equal("SCHEDULE UNAVAILABLE",
            data.RootElement.GetProperty("countdownLabel").GetString());
        Assert.Contains("never confirms liftoff",
            data.RootElement.GetProperty("explanation").GetString());
        using var template = JsonDocument.Parse(payload.TemplateJson);
        Assert.False(template.RootElement.TryGetProperty("actions", out _));
    }

    [Fact]
    public void ExternalRecordUrlDoesNotCreateOpenUrlAction()
    {
        var launch = NewLaunch(
            LaunchPhase.Scheduled,
            Now.AddMinutes(5),
            detailsUrl: "https://example.org/untrusted");

        var payload = LaunchWidgetPayloadBuilder.Create(launch, Now);

        using var template = JsonDocument.Parse(payload.TemplateJson);
        Assert.False(template.RootElement.TryGetProperty("actions", out _));
    }

    private static LaunchLibrary2Launch NewLaunch(
        LaunchPhase phase,
        DateTimeOffset? scheduledAt,
        string statusName = "Go for Launch",
        string? detailsUrl = null)
    {
        Uri? detailsUri = Uri.TryCreate(detailsUrl, UriKind.Absolute, out var uri) ? uri : null;
        var state = new LaunchState(
            "widget-test",
            "Mission Control Test Flight",
            scheduledAt,
            phase,
            "Launch Library 2 (schedule metadata only)",
            Now,
            LiftoffAtUtc: null,
            StatusNote: null);
        return new LaunchLibrary2Launch(
            state,
            statusName,
            "NASA",
            "Launch Complex 39A",
            "Kennedy Space Center",
            "US",
            detailsUri);
    }
}
