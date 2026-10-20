using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Windows.Widgets.Providers;
using MissionControl.Core;

namespace MissionControl.WidgetProvider;

internal sealed class LaunchWidgetInstance : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);

    private readonly string _widgetId;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly LaunchLibrary2Client _launchClient;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _lifecycleLock = new();

    private CancellationTokenSource? _activeCancellation;
    private Task? _activeRefreshLoop;
    private LaunchLibrary2Launch? _lastLaunch;
    private bool _disposed;

    public LaunchWidgetInstance(string widgetId)
    {
        _widgetId = widgetId;
        _launchClient = new LaunchLibrary2Client(_httpClient, cacheDuration: RefreshInterval);
    }

    public void Activate()
    {
        lock (_lifecycleLock)
        {
            if (_disposed || _activeCancellation is not null)
            {
                return;
            }

            _activeCancellation = new CancellationTokenSource();
            var token = _activeCancellation.Token;
            _activeRefreshLoop = Task.Run(() => RunRefreshLoopAsync(token));
        }
    }

    public void Deactivate()
    {
        lock (_lifecycleLock)
        {
            _activeCancellation?.Cancel();
            _activeCancellation?.Dispose();
            _activeCancellation = null;
            _activeRefreshLoop = null;
        }
    }

    public void RefreshNow()
    {
        CancellationToken token;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            token = _activeCancellation?.Token ?? CancellationToken.None;
        }

        _ = Task.Run(() => RefreshAndPublishAsync(token));
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAndPublishAsync(cancellationToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(RefreshInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshAndPublishAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The Widgets host deactivated or removed this widget.
        }
    }

    private async Task RefreshAndPublishAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            try
            {
                var upcoming = await _launchClient.GetUpcomingUsLaunchesAsync(cancellationToken)
                    .ConfigureAwait(false);

                _lastLaunch = upcoming
                    .Where(item => item.State.ScheduledAtUtc.HasValue)
                    .OrderBy(item => item.State.ScheduledAtUtc)
                    .FirstOrDefault();
            }
            catch (Exception exception) when (
                exception is OperationCanceledException or HttpRequestException or
                JsonException or InvalidDataException or InvalidOperationException)
            {
                // Preserve last-known-good schedule data when a refresh fails.
                // The source observation timestamp remains unchanged.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var payload = LaunchWidgetPayloadBuilder.Create(_lastLaunch, DateTimeOffset.UtcNow);
            var options = new WidgetUpdateRequestOptions(_widgetId)
            {
                Template = payload.TemplateJson,
                Data = payload.DataJson,
                CustomState = _lastLaunch?.State.LaunchId ?? string.Empty
            };

            try
            {
                WidgetManager.GetDefault().UpdateWidget(options);
            }
            catch (Exception exception) when (
                exception is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                // The host may dispose a widget while a background refresh is
                // completing; dropping that update is harmless.
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _activeCancellation?.Cancel();
            _activeCancellation?.Dispose();
            _activeCancellation = null;
            _activeRefreshLoop = null;
        }

        _httpClient.Dispose();
    }
}
