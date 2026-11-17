using System.Net;
using System.Text;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Providers;
using Guildhall.AgentHost.Providers.Cloud;

namespace Guildhall.AgentHost.Tests;

public sealed class CloudEngineTests
{
    private sealed class FakeCredentialStore(string? key) : ICredentialStore
    {
        public ValueTask<string?> GetApiKeyAsync(string providerId, CancellationToken ct = default) => ValueTask.FromResult(key);
        public ValueTask SetApiKeyAsync(string providerId, string apiKey, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask DeleteApiKeyAsync(string providerId, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<CredentialStatus> GetStatusAsync(string providerId, CancellationToken ct = default) => ValueTask.FromResult(new CredentialStatus(key is not null, key is null ? null : "••••test"));
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    [Fact]
    public async Task OpenAiCloudEngineStreamsDeltasAndUsage()
    {
        var sse = string.Join("\n",
        [
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello \"}}]}",
            "",
            "data: {\"choices\":[{\"delta\":{\"content\":\"world\"}}]}",
            "",
            "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":5}}",
            "",
            "data: [DONE]",
            ""
        ]);

        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var engine = new OpenAiCloudEngine(new SingleClientFactory(client), new FakeCredentialStore("sk-test-key"));

        var deltas = new List<string>();
        BackingEngineUsage? usage = null;
        var request = new BackingEngineRequest(Guid.NewGuid(), Guid.NewGuid(), "openai-api", "gpt-4.1-mini", null, "You are helpful.", [new BackingEngineMessage("user", "Hi", [])]);

        await engine.StreamTurnAsync(request, (chunk, _) =>
        {
            if (chunk.DeltaText is not null) deltas.Add(chunk.DeltaText);
            if (chunk.Usage is not null) usage = chunk.Usage;
            return ValueTask.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal("Hello world", string.Concat(deltas));
        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("sk-test-key", handler.LastRequest?.Headers.Authorization?.Parameter);
        Assert.Equal(12, usage?.InputTokens);
        Assert.Equal(5, usage?.OutputTokens);
        Assert.Equal(CostSources.MeasuredApi, usage?.CostSource);
    }

    [Fact]
    public async Task GeminiCloudEngineStreamsDeltasAndUsage()
    {
        var sse = string.Join("\n",
        [
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hi from \"}]}}]}",
            "",
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Gemini\"}]}}],\"usageMetadata\":{\"promptTokenCount\":20,\"candidatesTokenCount\":8}}",
            ""
        ]);

        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };
        var engine = new GeminiCloudEngine(new SingleClientFactory(client), new FakeCredentialStore("AIza-test"));

        var deltas = new List<string>();
        BackingEngineUsage? usage = null;
        var request = new BackingEngineRequest(Guid.NewGuid(), Guid.NewGuid(), "gemini-api", "gemini-2.5-flash", null, "System rules.", [new BackingEngineMessage("user", "Hello", [])]);

        await engine.StreamTurnAsync(request, (chunk, _) =>
        {
            if (chunk.DeltaText is not null) deltas.Add(chunk.DeltaText);
            if (chunk.Usage is not null) usage = chunk.Usage;
            return ValueTask.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal("Hi from Gemini", string.Concat(deltas));
        Assert.Equal("AIza-test", handler.LastRequest?.Headers.GetValues("x-goog-api-key").Single());
        Assert.Equal(20, usage?.InputTokens);
        Assert.Equal(8, usage?.OutputTokens);
    }

    [Fact]
    public async Task AnthropicCloudEngineMapsUnauthorizedToProviderAuthFailed()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var engine = new AnthropicCloudEngine(new SingleClientFactory(client), new FakeCredentialStore("sk-ant-bad"));
        var request = new BackingEngineRequest(Guid.NewGuid(), Guid.NewGuid(), "anthropic-api", "claude-sonnet-4-20250514", null, "System", [new BackingEngineMessage("user", "Hi", [])]);

        var ex = await Assert.ThrowsAsync<BackingEngineException>(() =>
            engine.StreamTurnAsync(request, (_, _) => ValueTask.CompletedTask, CancellationToken.None));

        Assert.Equal("provider_auth_failed", ex.ErrorCode);
    }
}
