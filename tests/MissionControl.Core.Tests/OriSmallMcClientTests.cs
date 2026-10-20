using System.Net;
using System.Text;
using System.Text.Json;
using MissionControl.Core;
using Xunit;

namespace MissionControl.Core.Tests;

public sealed class OriSmallMcClientTests
{
    [Fact]
    public async Task ChatAsyncPostsConversationAndReturnsAssistantContent()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                """{"message":{"role":"assistant","content":"Hello from Ori Small MC."},"done":true}""")));

        using var httpClient = new HttpClient(handler);
        var client = new OriSmallMcClient(httpClient);
        var conversation = new[]
        {
            new OriChatMessage("user", "Hello."),
            new OriChatMessage("assistant", "Hi!"),
            new OriChatMessage("user", "What can you help with?")
        };

        var reply = await client.ChatAsync(conversation);

        Assert.Equal("Hello from Ori Small MC.", reply);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(new Uri("http://localhost:11434/api/chat"), handler.LastRequestUri);

        using var requestJson = JsonDocument.Parse(Assert.IsType<string>(handler.LastBody));
        var root = requestJson.RootElement;
        Assert.Equal(OriSmallMcClient.DefaultModel, root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());

        var sentMessages = root.GetProperty("messages");
        Assert.Equal(3, sentMessages.GetArrayLength());
        Assert.Equal("user", sentMessages[0].GetProperty("role").GetString());
        Assert.Equal("Hello.", sentMessages[0].GetProperty("content").GetString());
        Assert.Equal("assistant", sentMessages[1].GetProperty("role").GetString());
        Assert.Equal("What can you help with?", sentMessages[2].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ChatAsyncReportsOllamaHttpErrorsWithResponseDetails()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse(
                HttpStatusCode.ServiceUnavailable,
                "Ollama is still starting.")));

        using var httpClient = new HttpClient(handler);
        var client = new OriSmallMcClient(httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ChatAsync([new OriChatMessage("user", "Hi")]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("Ollama is still starting.", exception.Message);
    }

    [Fact]
    public async Task ChatAsyncRejectsAnEmptyConversationWithoutSendingARequest()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"message":{"content":"unexpected"}}""")));

        using var httpClient = new HttpClient(handler);
        var client = new OriSmallMcClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ChatAsync(Array.Empty<OriChatMessage>()));

        Assert.Null(handler.LastMethod);
    }

    [Fact]
    public async Task ChatAsyncRejectsAResponseWithoutMessageContent()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"done":true}""")));

        using var httpClient = new HttpClient(handler);
        var client = new OriSmallMcClient(httpClient);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.ChatAsync([new OriChatMessage("user", "Hi")]));
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
        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return await responder(request, cancellationToken);
        }
    }
}
