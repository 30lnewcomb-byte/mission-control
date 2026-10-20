using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MissionControl.Core;

namespace MissionControl.Desktop;

public partial class MainWindow : Window
{
    private const int MaxConversationMessages = 24;

    private readonly HttpClient _launchHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    private readonly HttpClient _ollamaHttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    private readonly LaunchLibrary2Client _launchClient;
    private readonly OriSmallMcClient _oriClient;
    private readonly OriPlatformLocalClient _oriPlatformClient;
    private bool _initializingUi = true;
    private readonly DispatcherTimer _countdownTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private readonly List<OriChatMessage> _conversationHistory = [];
    private bool _refreshingSchedule;
    private bool _sendingChat;

    public MainWindow()
    {
        InitializeComponent();

        DataContext = this;
        _launchClient = new LaunchLibrary2Client(_launchHttpClient);
        _oriClient = new OriSmallMcClient(_ollamaHttpClient);
        _oriPlatformClient = new OriPlatformLocalClient();
        _initializingUi = false;

        ChatLines.Add(new ChatLine(
            "MISSION CONTROL",
            "This dashboard uses real launch schedule data when available. Choose Ori Platform TensorFlow or Ori Small MC (Qwen) below; both chat models run locally. Countdown truth stays in deterministic code."));

        _countdownTimer.Tick += CountdownTimer_Tick;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    public ObservableCollection<LaunchRow> Launches { get; } = [];

    public ObservableCollection<ChatLine> ChatLines { get; } = [];

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await Task.WhenAll(
            RefreshLaunchesAsync(silent: true),
            RefreshAiStatusAsync());

        _countdownTimer.Start();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _countdownTimer.Stop();
        _launchHttpClient.Dispose();
        _ollamaHttpClient.Dispose();
        _oriPlatformClient.Dispose();
    }

    private void PinWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinWindowButton.Content = Topmost ? "UNPIN WINDOW" : "PIN WINDOW";
    }

    private async void AiModelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingUi)
        {
            return;
        }

        await RefreshAiStatusAsync();
    }

    private bool IsOriPlatformSelected() =>
        (AiModelSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "ori-platform";

    private async void RefreshLaunches_Click(object sender, RoutedEventArgs e) =>
        await RefreshLaunchesAsync(silent: false);

    private async Task RefreshLaunchesAsync(bool silent)
    {
        if (_refreshingSchedule)
        {
            return;
        }

        _refreshingSchedule = true;
        RefreshScheduleButton.IsEnabled = false;
        LaunchFeedStatusText.Text = "SCHEDULE FEED · FETCHING";

        try
        {
            var fetchedLaunches = await _launchClient.GetUpcomingUsLaunchesAsync();

            // Replace the visible collection only after a complete, valid response.
            // A failed refresh therefore leaves any previously displayed records intact.
            Launches.Clear();
            foreach (var launch in fetchedLaunches)
            {
                Launches.Add(new LaunchRow(launch));
            }

            LaunchFeedStatusText.Text = $"SCHEDULE FEED · {Launches.Count} U.S. RECORDS";
            LaunchCountSummary.Text = Launches.Count == 0
                ? "No upcoming U.S. launch records were returned by the schedule feed."
                : $"{Launches.Count} U.S. records · Launch Library 2 schedule metadata only";

            LaunchesList.SelectedIndex = Launches.Count > 0 ? 0 : -1;
            UpdateSelectedLaunch();
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            JsonException or
            InvalidDataException or
            InvalidOperationException)
        {
            LaunchFeedStatusText.Text = "SCHEDULE FEED · UNAVAILABLE";
            LaunchCountSummary.Text = Launches.Count > 0
                ? $"{Launches.Count} previously loaded records remain visible; refresh failed."
                : "No schedule data loaded. Check the connection and retry.";

            if (!silent || Launches.Count == 0)
            {
                AddStatusMessage($"The launch schedule could not be refreshed: {exception.Message}");
            }
        }
        finally
        {
            _refreshingSchedule = false;
            RefreshScheduleButton.IsEnabled = true;
        }
    }

    private async Task RefreshAiStatusAsync()
    {
        AiStatusTextFallback();

        if (IsOriPlatformSelected())
        {
            AiStatusText.Text = _oriPlatformClient.IsInstalled
                ? "ORI PLATFORM · LOCAL MODEL INSTALLED"
                : "ORI PLATFORM · SETUP REQUIRED";
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var response = await _ollamaHttpClient.GetAsync(
                "http://localhost:11434/api/tags",
                timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                AiStatusText.Text = "ORI SMALL MC · LOCAL SERVICE UNAVAILABLE";
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);

            var modelFound = document.RootElement.TryGetProperty("models", out var models) &&
                models.ValueKind == JsonValueKind.Array &&
                models.EnumerateArray().Any(model =>
                {
                    if (!model.TryGetProperty("name", out var nameElement))
                    {
                        return false;
                    }

                    var name = nameElement.GetString();
                    return string.Equals(name, OriSmallMcClient.DefaultModel, StringComparison.OrdinalIgnoreCase) ||
                        (name?.StartsWith(
                            OriSmallMcClient.DefaultModel + ":",
                            StringComparison.OrdinalIgnoreCase) ?? false);
                });

            AiStatusText.Text = modelFound
                ? "ORI SMALL MC · READY"
                : "OLLAMA ONLINE · MODEL MISSING";
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            JsonException or
            InvalidOperationException)
        {
            AiStatusText.Text = "ORI SMALL MC · OFFLINE";
        }
    }

    private void AiStatusTextFallback()
    {
        AiStatusText.Text = "AI MODEL · CHECKING";
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        foreach (var launch in Launches)
        {
            launch.RefreshCountdown(nowUtc);
        }

        UpdateSelectedLaunch();
    }

    private void LaunchesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectedLaunch();

    private void UpdateSelectedLaunch()
    {
        if (LaunchesList.SelectedItem is not LaunchRow selected)
        {
            SelectedLaunchName.Text = "Waiting for schedule data";
            SelectedLaunchStatus.Text = "SCHEDULE STATUS · NOT LOADED";
            SelectedLaunchSchedule.Text = "Scheduled time unavailable";
            SelectedLaunchExplanation.Text = "Select a launch to see why its countdown is in this state.";
            SelectedLaunchCountdown.Text = "T− --:--:--";
            SelectedLaunchTrustText.Visibility = Visibility.Collapsed;
            OpenLaunchDetailsButton.IsEnabled = false;
            return;
        }

        SelectedLaunchName.Text = selected.Name;
        SelectedLaunchStatus.Text = $"LL2 STATUS · {selected.StatusName.ToUpperInvariant()}";
        SelectedLaunchSchedule.Text = selected.ScheduledLabel;
        SelectedLaunchExplanation.Text = selected.CountdownExplanation;
        SelectedLaunchCountdown.Text = selected.CountdownLabel;
        SelectedLaunchTrustText.Visibility = Visibility.Visible;
        SelectedLaunchTrustText.Text =
            "SCHEDULE DATA ONLY · A TRUSTED LIFTOFF TIMESTAMP IS NOT CONNECTED";
        OpenLaunchDetailsButton.IsEnabled =
            selected.Source.DetailsUri is { } detailsUri &&
            detailsUri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(detailsUri.Host, "ll.thespacedevs.com", StringComparison.OrdinalIgnoreCase);
    }

    private void OpenLaunchDetails_Click(object sender, RoutedEventArgs e)
    {
        if (LaunchesList.SelectedItem is not LaunchRow selected ||
            selected.Source.DetailsUri is not { } detailsUri ||
            detailsUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(detailsUri.Host, "ll.thespacedevs.com", StringComparison.OrdinalIgnoreCase))
        {
            AddStatusMessage("A trusted Launch Library 2 record link is not available for this launch.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(detailsUri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            AddStatusMessage($"Windows could not open the launch record: {exception.Message}");
        }
    }

    private async void SendChat_Click(object sender, RoutedEventArgs e) =>
        await SendChatAsync();

    private async void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            return;
        }

        e.Handled = true;
        await SendChatAsync();
    }

    private async Task SendChatAsync()
    {
        if (_sendingChat)
        {
            return;
        }

        var text = ChatInput.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        ChatInput.Clear();
        ChatLines.Add(new ChatLine("YOU", text));
        _conversationHistory.Add(new OriChatMessage("user", text));
        TrimConversationHistory();

        _sendingChat = true;
        SendChatButton.IsEnabled = false;
        ChatInput.IsEnabled = false;
        AiModelSelector.IsEnabled = false;
        AiStatusText.Text = IsOriPlatformSelected() ? "ORI PLATFORM · THINKING" : "ORI SMALL MC · THINKING";

        var thinkingLine = new ChatLine(IsOriPlatformSelected() ? "ORI PLATFORM" : "ORI SMALL MC", "Thinking…");
        ChatLines.Add(thinkingLine);
        ScrollChatToBottom();

        try
        {
            var reply = IsOriPlatformSelected()
                ? await _oriPlatformClient.ChatAsync(_conversationHistory)
                : await _oriClient.ChatAsync(_conversationHistory);
            _conversationHistory.Add(new OriChatMessage("assistant", reply));
            TrimConversationHistory();

            ChatLines.Remove(thinkingLine);
            ChatLines.Add(new ChatLine(IsOriPlatformSelected() ? "ORI PLATFORM" : "ORI SMALL MC", reply));
            AiStatusText.Text = "ORI SMALL MC · READY";
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            JsonException or
            InvalidDataException or
            InvalidOperationException)
        {
            if (_conversationHistory.Count > 0 &&
                string.Equals(_conversationHistory[^1].Role, "user", StringComparison.Ordinal))
            {
                _conversationHistory.RemoveAt(_conversationHistory.Count - 1);
            }

            ChatLines.Remove(thinkingLine);
            var explanation = exception is TaskCanceledException
                ? "The local model took too long to answer. Try again after it is ready."
                : IsOriPlatformSelected()
                    ? "I couldn't reach Ori Platform's local TensorFlow model. Run 'Install AI Model' from the Start menu and choose option 1. " + exception.Message
                    : "I couldn't reach Ori Small MC. Make sure Ollama is running and ori-small-mc is installed; then run 'Install AI Model' and choose option 2.";
            ChatLines.Add(new ChatLine("MISSION CONTROL", explanation));
            AiStatusText.Text = IsOriPlatformSelected() ? "ORI PLATFORM · UNAVAILABLE" : "ORI SMALL MC · UNAVAILABLE";
        }
        finally
        {
            _sendingChat = false;
            SendChatButton.IsEnabled = true;
            ChatInput.IsEnabled = true;
            AiModelSelector.IsEnabled = true;
            ChatInput.Focus();
            ScrollChatToBottom();
        }
    }

    private void TrimConversationHistory()
    {
        while (_conversationHistory.Count > MaxConversationMessages)
        {
            _conversationHistory.RemoveRange(0, Math.Min(2, _conversationHistory.Count));
        }
    }

    private void AddStatusMessage(string text)
    {
        ChatLines.Add(new ChatLine("MISSION CONTROL", text));
        ScrollChatToBottom();
    }

    private void ScrollChatToBottom()
    {
        if (ChatMessagesList.Items.Count > 0)
        {
            ChatMessagesList.ScrollIntoView(ChatMessagesList.Items[ChatMessagesList.Items.Count - 1]);
        }
    }
}
