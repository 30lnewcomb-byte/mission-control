using System.Net;
using System.Text;
using MissionControl.Core;
using Xunit;

namespace MissionControl.Core.Tests;

public sealed class LaunchLibrary2ClientTests
{
    [Fact]
    public async Task GetUpcomingLaunchesAsyncParsesMetadataWithoutInventingLiftoffTime()
    {
        const string payload = """
        {
          "results": [
            {
              "id": "us-launch-1",
              "name": "Star Mission",
              "net": "2026-10-21T16:00:00Z",
              "status": {
                "name": "Go for Launch",
                "description": "Current T-0 confirmed by official or reliable sources."
              },
              "launch_service_provider": { "name": "NASA" },
              "pad": {
                "name": "Launch Complex 39A",
                "location": {
                  "name": "Kennedy Space Center",
                  "country": { "alpha_2_code": "US" }
                }
              },
              "url": "https://ll.thespacedevs.com/2.3.0/launches/us-launch-1/"
            },
            {
              "id": "flight-launch-2",
              "name": "Reported In-Flight Launch",
              "net": "2026-10-20T15:00:00Z",
              "status": {
                "name": "Launch in Flight",
                "description": "The launch vehicle has lifted off from the launchpad."
              },
              "launch_service_provider": { "name": "SpaceX" },
              "pad": {
                "name": "Space Launch Complex 40",
                "location": {
                  "name": "Cape Canaveral Space Force Station",
                  "country": { "alpha_2_code": "US" }
                }
              },
              "url": "https://ll.thespacedevs.com/2.3.0/launches/flight-launch-2/"
            }
          ]
        }
        """;

        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, payload)));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ll2.test/api/")
        };
        var client = new LaunchLibrary2Client(httpClient);

        var launches = await client.GetUpcomingLaunchesAsync();

        Assert.Equal(2, launches.Count);
        Assert.Equal(new Uri("https://ll2.test/api/launches/upcoming/?limit=20&hide_recent_previous=True"),
            handler.LastRequestUri);

        var scheduled = launches[0];
        Assert.Equal("us-launch-1", scheduled.State.LaunchId);
        Assert.Equal("Star Mission", scheduled.State.Name);
        Assert.Equal(LaunchPhase.Scheduled, scheduled.State.Phase);
        Assert.Equal(DateTimeOffset.Parse("2026-10-21T16:00:00Z"), scheduled.State.ScheduledAtUtc);
        Assert.Equal("NASA", scheduled.ProviderName);
        Assert.Equal("Launch Complex 39A", scheduled.LaunchPadName);
        Assert.Equal("Kennedy Space Center", scheduled.LocationName);
        Assert.Equal("US", scheduled.CountryCode);
        Assert.Equal("Go for Launch", scheduled.StatusName);
        Assert.NotNull(scheduled.DetailsUri);

        var inFlight = launches[1];
        Assert.Equal(LaunchPhase.Unknown, inFlight.State.Phase);
        Assert.Null(inFlight.State.LiftoffAtUtc);
        var countdown = CountdownEngine.GetSnapshot(inFlight.State, DateTimeOffset.UtcNow);
        Assert.NotEqual(CountdownMode.TPlus, countdown.Mode);
        Assert.Contains("STATUS UNKNOWN", countdown.Label);
    }

    [Fact]
    public async Task GetUpcomingLaunchesAsyncRejectsExternalDetailsLinks()
    {
        const string payload = """
        {
          "results": [
            {
              "id": "suspicious-1",
              "name": "Suspicious Details Link",
              "net": "2026-10-21T16:00:00Z",
              "status": { "name": "Go for Launch" },
              "url": "https://example.com/redirect-to-something"
            }
          ]
        }
        """;

        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, payload)));
        using var httpClient = new HttpClient(handler);
        var client = new LaunchLibrary2Client(httpClient);

        var launch = Assert.Single(await client.GetUpcomingLaunchesAsync());

        Assert.Null(launch.DetailsUri);
    }

    [Fact]
    public async Task GetUpcomingLaunchesAsyncCachesSuccessfulResultsDuringCacheWindow()
    {
        var requestCount = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"results":[]}"""));
        });
        using var httpClient = new HttpClient(handler);
        var client = new LaunchLibrary2Client(httpClient, cacheDuration: TimeSpan.FromMinutes(5));

        Assert.Null(client.LastSuccessfulFetchAtUtc);
        var first = await client.GetUpcomingLaunchesAsync();
        var fetchedAtUtc = client.LastSuccessfulFetchAtUtc;
        var second = await client.GetUpcomingLaunchesAsync();

        Assert.Empty(first);
        Assert.Same(first, second);
        Assert.NotNull(fetchedAtUtc);
        Assert.Equal(fetchedAtUtc, client.LastSuccessfulFetchAtUtc);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task FailedRefreshKeepsTimestampOfLastSuccessfulResponse()
    {
        var requestCount = 0;
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(requestCount == 1
                ? JsonResponse(HttpStatusCode.OK, """{"results":[]}""")
                : JsonResponse(HttpStatusCode.TooManyRequests, "API rate limit exceeded."));
        });
        using var httpClient = new HttpClient(handler);
        var client = new LaunchLibrary2Client(
            httpClient,
            cacheDuration: TimeSpan.FromMilliseconds(1));

        _ = await client.GetUpcomingLaunchesAsync();
        var lastSuccessfulFetchAtUtc = client.LastSuccessfulFetchAtUtc;
        await Task.Delay(20);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetUpcomingLaunchesAsync());

        Assert.NotNull(lastSuccessfulFetchAtUtc);
        Assert.Equal(lastSuccessfulFetchAtUtc, client.LastSuccessfulFetchAtUtc);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task GetUpcomingUsLaunchesAsyncExcludesLaunchesOutsideTheUnitedStates()
    {
        const string payload = """
        {
          "results": [
            {
              "id": "us-1",
              "name": "US Launch",
              "net": "2026-10-21T16:00:00Z",
              "status": { "name": "Go for Launch" },
              "pad": {
                "name": "US Pad",
                "location": {
                  "name": "Kennedy Space Center",
                  "country": { "alpha_2_code": "US" }
                }
              }
            },
            {
              "id": "jp-1",
              "name": "Japan Launch",
              "net": "2026-10-22T16:00:00Z",
              "status": { "name": "Go for Launch" },
              "pad": {
                "name": "Japan Pad",
                "location": {
                  "name": "Tanegashima Space Center",
                  "country": { "alpha_2_code": "JP" }
                }
              }
            },
            {
              "id": "unknown-1",
              "name": "Unknown Site Launch",
              "net": "2026-10-23T16:00:00Z",
              "status": { "name": "Go for Launch" },
              "pad": { "name": "Unmapped Pad" }
            }
          ]
        }
        """;

        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, payload)));
        using var httpClient = new HttpClient(handler);
        var client = new LaunchLibrary2Client(httpClient);

        var launches = await client.GetUpcomingUsLaunchesAsync();

        var launch = Assert.Single(launches);
        Assert.Equal("us-1", launch.State.LaunchId);
        Assert.Equal("US", launch.CountryCode);
    }

    [Fact]
    public async Task GetUpcomingLaunchesAsyncIncludesDetailsWhenTheApiReturnsAnError()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse(
                HttpStatusCode.TooManyRequests,
                "API rate limit exceeded.")));
        using var httpClient = new HttpClient(handler);
        var client = new LaunchLibrary2Client(httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetUpcomingLaunchesAsync());

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Contains("API rate limit exceeded.", exception.Message);
    }

    [Fact]
    public async Task GetUpcomingLaunchesAsyncRejectsAResponseWithoutResultsArray()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"count":0}""")));
        using var httpClient = new HttpClient(handler);
        var client = new LaunchLibrary2Client(httpClient);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetUpcomingLaunchesAsync());
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return responder(request, cancellationToken);
        }
    }
}
