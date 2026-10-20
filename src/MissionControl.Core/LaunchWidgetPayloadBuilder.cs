using System.Globalization;
using System.Text.Json;

namespace MissionControl.Core;

/// <summary>
/// JSON payload builder for the native Windows Widgets card. Keeping card data
/// generation in Core makes its countdown and trust behavior unit-testable
/// without launching the Windows Widgets host.
/// </summary>
public sealed record LaunchWidgetPayload(string TemplateJson, string DataJson);

public static class LaunchWidgetPayloadBuilder
{
    private const string AdaptiveCardSchema = "http://adaptivecards.io/schemas/adaptive-card.json";
    private const string TrustedLaunchLibraryHost = "ll.thespacedevs.com";

    public static LaunchWidgetPayload Create(
        LaunchLibrary2Launch? launch,
        DateTimeOffset nowUtc)
    {
        var missionName = launch?.State.Name ?? "No U.S. launch schedule loaded";
        var countdownLabel = launch is null
            ? "SCHEDULE UNAVAILABLE"
            : CountdownEngine.GetSnapshot(launch.State, nowUtc).Label;
        var statusLabel = GetStatusLabel(launch);
        var scheduledLabel = launch?.State.ScheduledAtUtc is { } scheduled
            ? scheduled.ToLocalTime().ToString("ddd, MMM d · h:mm tt", CultureInfo.CurrentCulture)
            : "Launch time unavailable";
        var explanation = launch is null
            ? "Waiting for schedule data. A schedule time alone never confirms liftoff."
            : CountdownEngine.GetSnapshot(launch.State, nowUtc).Explanation;
        var sourceObserved = launch?.State.StatusObservedAtUtc.ToString(
            "HH:mm 'UTC'",
            CultureInfo.InvariantCulture) ?? "Not observed";

        var card = new Dictionary<string, object?>
        {
            ["$schema"] = AdaptiveCardSchema,
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = new object[]
            {
                new { type = "TextBlock", text = "MISSION CONTROL  ·  U.S. SPACEFLIGHT", size = "small", color = "accent", weight = "bolder", wrap = true },
                new { type = "TextBlock", text = "${missionName}", size = "large", weight = "bolder", wrap = true },
                new { type = "TextBlock", text = "${countdownLabel}", size = "extraLarge", weight = "bolder", color = "accent", wrap = true, spacing = "medium" },
                new { type = "TextBlock", text = "${statusLabel}", size = "small", weight = "bolder", wrap = true },
                new { type = "TextBlock", text = "${scheduledLabel}", size = "small", isSubtle = true, wrap = true, spacing = "none" },
                new { type = "TextBlock", text = "${explanation}", size = "small", wrap = true, spacing = "medium" },
                new { type = "TextBlock", text = "SCHEDULE METADATA ONLY · NOT OFFICIAL FLIGHT TELEMETRY", size = "small", isSubtle = true, wrap = true, spacing = "medium" },
                new { type = "TextBlock", text = "UPDATED ${sourceObserved}", size = "small", isSubtle = true, wrap = true, spacing = "none" }
            }
        };

        var actions = new List<object>
        {
            new { type = "Action.Execute", title = "Refresh Schedule", verb = "refresh" }
        };

        if (TryGetTrustedDetailsUri(launch?.DetailsUri, out var detailsUri))
        {
            actions.Add(new { type = "Action.OpenUrl", title = "Open Launch Library 2 Record", url = detailsUri.AbsoluteUri });
        }

        card["actions"] = actions;

        var data = new Dictionary<string, string>
        {
            ["missionName"] = missionName,
            ["countdownLabel"] = countdownLabel,
            ["statusLabel"] = statusLabel,
            ["scheduledLabel"] = scheduledLabel,
            ["explanation"] = explanation,
            ["sourceObserved"] = sourceObserved
        };

        return new LaunchWidgetPayload(
            JsonSerializer.Serialize(card),
            JsonSerializer.Serialize(data));
    }

    private static string GetStatusLabel(LaunchLibrary2Launch? launch)
    {
        if (launch is null)
        {
            return "STATUS UNKNOWN";
        }

        return launch.State.Phase == LaunchPhase.Unknown
            ? $"STATUS UNVERIFIED · {launch.StatusName.ToUpperInvariant()}"
            : $"{launch.State.Phase.ToString().ToUpperInvariant()} · {launch.StatusName.ToUpperInvariant()}";
    }

    private static bool TryGetTrustedDetailsUri(Uri? candidate, out Uri detailsUri)
    {
        if (candidate is { IsAbsoluteUri: true } &&
            string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Host, TrustedLaunchLibraryHost, StringComparison.OrdinalIgnoreCase))
        {
            detailsUri = candidate;
            return true;
        }

        detailsUri = null!;
        return false;
    }
}
