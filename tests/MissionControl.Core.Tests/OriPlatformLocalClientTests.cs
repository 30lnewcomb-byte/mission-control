using System.Net;
using System.Text;
using System.Text.Json;
using MissionControl.Core;
using Xunit;

namespace MissionControl.Core.Tests;

public sealed class OriPlatformLocalClientTests
{
    [Fact]
    public async Task ChatAsyncPostsConversationToLocalTensorFlowRuntime()
    {
        using var localRoot = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(localRoot.Path, "local-api-key.txt"), "local-test-key");

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri("http://127.0.0.1:8765/v1/chat"), request.RequestUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("local-test-key", request.Headers.Authorization?.Parameter);
            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                """{"content":"Ori Platform local response.","model":"ori-small","tool_calls":[]}"""));
        });

        using var httpClient = new HttpClient(handler);
        using var client = new OriPlatformLocalClient(
            httpClient,
            _ => Task.CompletedTask,
            localRoot.Path);

        var conversation = new[]
        {
            new OriChatMessage("user", "Hello."),
            new OriChatMessage("assistant", "Hi."),
            new OriChatMessage("user", "Explain a neural network.")
        };

        var reply = await client.ChatAsync(conversation);

        Assert.Equal("Ori Platform local response.", reply);
        using var requestJson = JsonDocument.Parse(Assert.IsType<string>(handler.LastBody));
        var root = requestJson.RootElement;
        Assert.Equal(0.2, root.GetProperty("temperature").GetDouble());
        Assert.Equal(72, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0, root.GetProperty("tools").GetArrayLength());

        var sentMessages = root.GetProperty("messages");
        Assert.Equal(3, sentMessages.GetArrayLength());
        Assert.Equal("user", sentMessages[0].GetProperty("role").GetString());
        Assert.Equal("assistant", sentMessages[1].GetProperty("role").GetString());
        Assert.Equal("Explain a neural network.", sentMessages[2].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ChatAsyncReportsLocalRuntimeErrors()
    {
        using var localRoot = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(localRoot.Path, "local-api-key.txt"), "local-test-key");

        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.BadGateway,
            """{"detail":"Model failed to generate."}""")));

        using var httpClient = new HttpClient(handler);
        using var client = new OriPlatformLocalClient(
            httpClient,
            _ => Task.CompletedTask,
            localRoot.Path);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.ChatAsync([new OriChatMessage("user", "Hi")]));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Contains("Model failed to generate.", exception.Message);
    }

    [Fact]
    public async Task ChatAsyncRejectsEmptyConversationWithoutSendingRequest()
    {
        using var localRoot = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(localRoot.Path, "local-api-key.txt"), "local-test-key");

        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"content":"unexpected"}""")));

        using var httpClient = new HttpClient(handler);
        using var client = new OriPlatformLocalClient(
            httpClient,
            _ => Task.CompletedTask,
            localRoot.Path);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ChatAsync(Array.Empty<OriChatMessage>()));

        Assert.Null(handler.LastMethod);
    }

    [Fact]
    public async Task ChatAsyncRejectsResponseWithoutContent()
    {
        using var localRoot = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(localRoot.Path, "local-api-key.txt"), "local-test-key");

        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"model":"ori-small"}""")));

        using var httpClient = new HttpClient(handler);
        using var client = new OriPlatformLocalClient(
            httpClient,
            _ => Task.CompletedTask,
            localRoot.Path);

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
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return await responder(request, cancellationToken);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "mission-control-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
