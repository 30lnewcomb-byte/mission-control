using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MissionControl.Core;

/// <summary>
/// Talks to a local copy of Ori Platform's native TensorFlow runtime.
/// The service is launched on loopback only and is never exposed to the LAN.
/// </summary>
public sealed class OriPlatformLocalClient : IDisposable
{
    public const string DefaultEndpoint = "http://127.0.0.1:8765/";
    private const int StartupTimeoutSeconds = 180;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _chatEndpoint;
    private readonly Uri _statusEndpoint;
    private readonly string _localRoot;
    private readonly Func<CancellationToken, Task>? _ensureRuntimeReadyOverride;
    private Process? _ownedRuntimeProcess;
    private bool _disposed;

    public OriPlatformLocalClient(
        HttpClient? httpClient = null,
        Func<CancellationToken, Task>? ensureRuntimeReady = null,
        string? localRoot = null,
        Uri? endpoint = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        var apiBase = endpoint ?? new Uri(DefaultEndpoint);
        _chatEndpoint = new Uri(apiBase, "v1/chat");
        _statusEndpoint = new Uri(apiBase, "v1/status");
        _localRoot = localRoot ?? GetDefaultLocalRoot();
        _ensureRuntimeReadyOverride = ensureRuntimeReady;
    }

    public bool IsInstalled =>
        File.Exists(Path.Combine(_localRoot, ".venv", "Scripts", "python.exe")) &&
        File.Exists(Path.Combine(_localRoot, "services", "intelligence", "runtime.py")) &&
        File.Exists(Path.Combine(_localRoot, "services", "intelligence", "artifacts", "ori-small", "config.json")) &&
        File.Exists(Path.Combine(_localRoot, "services", "intelligence", "artifacts", "ori-small", "model.weights.h5")) &&
        File.Exists(Path.Combine(_localRoot, "services", "intelligence", "artifacts", "ori-small", "ori_tokenizer.model")) &&
        File.Exists(Path.Combine(_localRoot, "local-api-key.txt"));

    public async Task<string> ChatAsync(
        IReadOnlyList<OriChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            throw new ArgumentException("At least one chat message is required.", nameof(messages));
        }

        if (_ensureRuntimeReadyOverride is not null)
        {
            await _ensureRuntimeReadyOverride(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await EnsureRuntimeReadyAsync(cancellationToken).ConfigureAwait(false);
        }

        var apiKey = ReadLocalApiKey();
        var payload = new
        {
            messages = messages.Select(message => new
            {
                role = message.Role,
                content = message.Content
            }),
            temperature = 0.2,
            max_tokens = 72,
            top_k = 20,
            tools = Array.Empty<object>()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Ori Platform's local TensorFlow runtime returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("content", out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Ori Platform's local runtime response did not contain a text content field.");
        }

        var content = contentElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidDataException("Ori Platform's local model returned an empty response.");
        }

        return content;
    }

    private async Task EnsureRuntimeReadyAsync(CancellationToken cancellationToken)
    {
        if (!IsInstalled)
        {
            throw new InvalidOperationException(
                "Ori Platform's local TensorFlow model is not installed. Open the Start menu and run 'Install AI Model', then choose option 1.");
        }

        var apiKey = ReadLocalApiKey();
        if (await IsRuntimeReadyAsync(apiKey, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (_ownedRuntimeProcess is null || _ownedRuntimeProcess.HasExited)
        {
            StartLocalRuntime(apiKey);
        }

        for (var attempt = 0; attempt < StartupTimeoutSeconds; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_ownedRuntimeProcess is { HasExited: true } exited)
            {
                throw new InvalidOperationException(
                    $"Ori Platform's local TensorFlow process exited with code {exited.ExitCode}. Re-run 'Install AI Model' from the Start menu to repair the setup.");
            }

            if (await IsRuntimeReadyAsync(apiKey, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        throw new TaskCanceledException(
            $"Ori Platform's local TensorFlow model did not finish loading within {StartupTimeoutSeconds} seconds. The first startup can be slow; try again after a moment.");
    }

    private void StartLocalRuntime(string apiKey)
    {
        var pythonPath = Path.Combine(_localRoot, ".venv", "Scripts", "python.exe");
        var serviceRoot = Path.Combine(_localRoot, "services", "intelligence");
        if (!File.Exists(pythonPath) || !File.Exists(Path.Combine(serviceRoot, "runtime.py")))
        {
            throw new InvalidOperationException("Ori Platform's local runtime files are incomplete. Re-run 'Install AI Model' and choose option 1.");
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            WorkingDirectory = serviceRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        processInfo.ArgumentList.Add("-m");
        processInfo.ArgumentList.Add("uvicorn");
        processInfo.ArgumentList.Add("runtime:app");
        processInfo.ArgumentList.Add("--host");
        processInfo.ArgumentList.Add("127.0.0.1");
        processInfo.ArgumentList.Add("--port");
        processInfo.ArgumentList.Add("8765");
        processInfo.Environment["ORI_INTELLIGENCE_API_KEY"] = apiKey;
        processInfo.Environment["ORI_MODEL_DIR"] = Path.Combine(serviceRoot, "models");
        processInfo.Environment["ORI_LM_DIR"] = Path.Combine(serviceRoot, "artifacts", "ori-small");
        processInfo.Environment["ORI_WORKER_ROOT"] = Path.Combine(serviceRoot, "artifacts");

        try
        {
            _ownedRuntimeProcess = Process.Start(processInfo)
                ?? throw new InvalidOperationException("Windows could not start Ori Platform's local TensorFlow process.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Windows could not start Ori Platform's local Python runtime. Re-run 'Install AI Model' and choose option 1.",
                exception);
        }
    }

    private async Task<bool> IsRuntimeReadyAsync(string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _statusEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return document.RootElement.TryGetProperty("language_model_loaded", out var loaded) &&
                loaded.ValueKind == JsonValueKind.True;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            JsonException or
            IOException)
        {
            return false;
        }
    }

    private string ReadLocalApiKey()
    {
        var keyPath = Path.Combine(_localRoot, "local-api-key.txt");
        if (!File.Exists(keyPath))
        {
            throw new InvalidOperationException(
                "Ori Platform's local API key is missing. Re-run 'Install AI Model' from the Start menu and choose option 1.");
        }

        var key = File.ReadAllText(keyPath).Trim();
        return string.IsNullOrWhiteSpace(key)
            ? throw new InvalidOperationException("Ori Platform's local API key is empty. Re-run 'Install AI Model' and choose option 1.")
            : key;
    }

    private static string GetDefaultLocalRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Programs", "Mission Control", "ori-platform-local");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownedRuntimeProcess is { HasExited: false } process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                Win32Exception)
            {
                // Closing Mission Control must not fail because a local process already exited.
            }
        }

        _ownedRuntimeProcess?.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
