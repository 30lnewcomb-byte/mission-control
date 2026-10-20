using System.Globalization;
using System.Text.Json;

namespace MissionControl.Core;

/// <summary>
/// Launch Library 2 schedule metadata attached to the canonical launch state.
/// LL2 is a schedule source, not sufficient authority to manufacture a liftoff timestamp.
/// </summary>
public sealed record LaunchLibrary2Launch(
    LaunchState State,
    string StatusName,
    string? ProviderName,
    string? LaunchPadName,
    string? LocationName,
    string? CountryCode,
    Uri? DetailsUri);

/// <summary>
/// Reads upcoming launch schedule records from Launch Library 2 v2.3.0.
/// Results are cached in-process to respect the public API's rate limits.
/// The adapter deliberately never sets LiftoffAtUtc.
/// </summary>
public sealed class LaunchLibrary2Client
{
    public const string DefaultBaseAddress = "https://ll.thespacedevs.com/2.3.0/";

    private const string StatusSource = "Launch Library 2 (schedule metadata only)";

    private readonly HttpClient _httpClient;
    private readonly Uri _upcomingEndpoint;
    private readonly TimeSpan _cacheDuration;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private IReadOnlyList<LaunchLibrary2Launch>? _cachedLaunches;
    private DateTimeOffset _cachedAtUtc;

    public LaunchLibrary2Client(
        HttpClient? httpClient = null,
        int limit = 20,
        TimeSpan? cacheDuration = null)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 100.");
        }

        _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(10);
        if (_cacheDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheDuration), "Cache duration must be positive.");
        }

        _httpClient = httpClient ?? new HttpClient();
        var apiBase = _httpClient.BaseAddress ?? new Uri(DefaultBaseAddress);
        _upcomingEndpoint = new Uri(
            apiBase,
            $"launches/upcoming/?limit={limit}&hide_recent_previous=True");
    }

    /// <summary>
    /// Timestamp of the most recent fully parsed, successful source response.
    /// This remains unchanged when a later refresh fails, so the UI can label stale data honestly.
    /// </summary>
    public DateTimeOffset? LastSuccessfulFetchAtUtc =>
        _cachedLaunches is null ? null : _cachedAtUtc;

    /// <summary>
    /// Returns a cached successful result for the cache window, otherwise refreshes
    /// from LL2. Network and schema errors are surfaced instead of being disguised
    /// as fresh data; callers can retain their own last-known-good snapshot.
    /// </summary>
    public async Task<IReadOnlyList<LaunchLibrary2Launch>> GetUpcomingLaunchesAsync(
        CancellationToken cancellationToken = default)
    {
        if (HasFreshCache())
        {
            return _cachedLaunches!;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (HasFreshCache())
            {
                return _cachedLaunches!;
            }

            using var response = await _httpClient.GetAsync(_upcomingEndpoint, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Launch Library 2 returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}",
                    null,
                    response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Launch Library 2 response did not contain a results array.");
            }

            var fetchedAtUtc = DateTimeOffset.UtcNow;
            var launches = results.EnumerateArray()
                .Select(item => ParseLaunch(item, fetchedAtUtc))
                .ToArray();

            _cachedLaunches = Array.AsReadOnly(launches);
            _cachedAtUtc = fetchedAtUtc;
            return _cachedLaunches;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Returns only launches with an explicit US launch-site country code.
    /// Entries without a known country are excluded rather than guessed.
    /// </summary>
    public async Task<IReadOnlyList<LaunchLibrary2Launch>> GetUpcomingUsLaunchesAsync(
        CancellationToken cancellationToken = default)
    {
        var launches = await GetUpcomingLaunchesAsync(cancellationToken).ConfigureAwait(false);
        var usLaunches = launches
            .Where(launch => string.Equals(launch.CountryCode, "US", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Array.AsReadOnly(usLaunches);
    }

    private bool HasFreshCache() =>
        _cachedLaunches is not null &&
        DateTimeOffset.UtcNow - _cachedAtUtc < _cacheDuration;

    private static LaunchLibrary2Launch ParseLaunch(JsonElement item, DateTimeOffset fetchedAtUtc)
    {
        var id = ReadString(item, "id");
        var name = ReadString(item, "name");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException("Launch Library 2 returned a launch without an id or name.");
        }

        var statusName = ReadNestedString(item, "status", "name") ?? "Unknown";
        var statusDescription = ReadNestedString(item, "status", "description");
        var phase = MapPhase(statusName);
        var scheduledAt = ReadDateTimeOffset(item, "net");
        var statusNote = BuildStatusNote(statusName, statusDescription);

        var state = new LaunchState(
            id,
            name,
            scheduledAt,
            phase,
            StatusSource,
            fetchedAtUtc,
            LiftoffAtUtc: null,
            StatusNote: statusNote);

        var detailsUri = ReadTrustedDetailsUri(ReadString(item, "url"));

        return new LaunchLibrary2Launch(
            state,
            statusName,
            ReadNestedString(item, "launch_service_provider", "name"),
            ReadNestedString(item, "pad", "name"),
            ReadNestedString(item, "pad", "location", "name"),
            ReadNestedString(item, "pad", "location", "country", "alpha_2_code"),
            detailsUri);
    }

    private static Uri? ReadTrustedDetailsUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        // The API record link is opened by the desktop shell, so only trust
        // HTTPS links whose host is exactly the Launch Library 2 domain.
        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(uri.Host, "ll.thespacedevs.com", StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;
    }

    private static LaunchPhase MapPhase(string statusName)
    {
        var normalized = statusName.Trim();

        if (normalized.Equals("Go for Launch", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchPhase.Scheduled;
        }

        if (normalized.Equals("On Hold", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchPhase.Delayed;
        }

        if (normalized.Contains("scrub", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchPhase.Scrubbed;
        }

        if (normalized.Equals("Payload Deployed", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Launch Successful", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Launch Failure", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Launch was a Partial Failure", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchPhase.Completed;
        }

        // In particular, an LL2 "Launch in Flight" status is not promoted to
        // authoritative in-flight state. A trusted status adapter must confirm it.
        return LaunchPhase.Unknown;
    }

    private static string BuildStatusNote(string statusName, string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? $"Launch Library 2 reports: {statusName}."
            : $"Launch Library 2 reports: {statusName}. {description.Trim()}";

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static string? ReadNestedString(JsonElement element, params string[] path)
    {
        for (var index = 0; index < path.Length; index++)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(path[index], out var child))
            {
                return null;
            }

            element = child;
            if (index < path.Length - 1 && element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
