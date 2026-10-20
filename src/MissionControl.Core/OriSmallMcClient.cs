using System.Net.Http.Json;
using System.Text.Json;

namespace MissionControl.Core;

/// <summary>
/// Sends general chat messages to the locally installed Ori Small MC Ollama model.
/// The local model is optional; callers should handle connection failures gracefully.
/// </summary>
public sealed class OriSmallMcClient
{
    public const string DefaultEndpoint = "http://localhost:11434/";
    public const string DefaultModel = "ori-small-mc";

    private readonly HttpClient _httpClient;
    private readonly Uri _chatEndpoint;
    private readonly string _model;

    public OriSmallMcClient(HttpClient? httpClient = null, string model = DefaultModel)
    {
        _httpClient = httpClient ?? new HttpClient();
        // Resolve the endpoint once so an injected HttpClient without BaseAddress works,
        // while still honoring a custom BaseAddress supplied by the host or tests.
        var apiBase = _httpClient.BaseAddress ?? new Uri(DefaultEndpoint);
        _chatEndpoint = new Uri(apiBase, "api/chat");
        _model = string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException("A model name is required.", nameof(model))
            : model;
    }

    /// <summary>
    /// Sends the current conversation to Ollama. Include prior user and assistant
    /// messages in order to maintain chat context between turns.
    /// </summary>
    public async Task<string> ChatAsync(
        IReadOnlyList<OriChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
        {
            throw new ArgumentException("At least one chat message is required.", nameof(messages));
        }

        var request = new
        {
            model = _model,
            messages = messages.Select(message => new
            {
                role = message.Role,
                content = message.Content
            }),
            stream = false
        };

        using var response = await _httpClient.PostAsJsonAsync(
            _chatEndpoint,
            request,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Ollama returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("message", out var messageElement) ||
            !messageElement.TryGetProperty("content", out var contentElement))
        {
            throw new InvalidDataException("Ollama's response did not contain message.content.");
        }

        return contentElement.GetString() ?? string.Empty;
    }
}

/// <summary>
/// One turn in an Ollama conversation. Supported roles are system, user, and assistant.
/// Ori Small MC's persistent system instructions are stored in its Ollama Modelfile.
/// </summary>
public sealed record OriChatMessage(string Role, string Content);
