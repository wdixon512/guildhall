using System.Net;
using System.Text;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Tests;

public sealed class Phase5SqlAndGatewayTests
{
    [Fact]
    public async Task StreamableHttpClientSupportsSseAndSessionIdAndBearerToken()
    {
        string? capturedAuth = null;
        string? capturedSessionId = null;
        var callCount = 0;

        var handler = new StubHttpMessageHandler(req =>
        {
            callCount++;
            capturedAuth = req.Headers.Authorization?.ToString();
            if (req.Headers.TryGetValues("Mcp-Session-Id", out var vals))
                capturedSessionId = vals.FirstOrDefault();

            if (callCount == 1)
            {
                var sseBody = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[{\"name\":\"remote_ping\",\"description\":\"Ping\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}]}}\n\n";
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sseBody, Encoding.UTF8, "text/event-stream")
                };
                resp.Headers.TryAddWithoutValidation("Mcp-Session-Id", "sess-wg-42");
                return resp;
            }

            var jsonBody = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"isError\":false,\"content\":[{\"type\":\"text\",\"text\":\"pong\"}]}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
        });

        var client = new McpJsonRpcClient(
            new SingleHandlerHttpFactory(handler),
            new InMemoryCredentialStore("mcp:wg-remote", "secret-bearer-token-123"),
            new EnvironmentScrubber(),
            new PrivateNetworkGuard());

        var reg = new McpServerRegistration
        {
            Id = "wg-remote",
            DisplayName = "Droplet Relay",
            Transport = "streamable_http",
            EndpointUrl = "http://10.66.0.1:8080/mcp",
            AllowPrivateTunnelHttp = true,
            SecurityTier = ToolSecurityTiers.Tier3,
        };

        var tools = await client.ListToolsAsync(reg, CancellationToken.None);
        Assert.Single(tools);
        Assert.Equal("remote_ping", tools[0].Name);
        Assert.Equal("Bearer secret-bearer-token-123", capturedAuth);
        Assert.Null(capturedSessionId);

        var result = await client.CallToolAsync(reg, "remote_ping", JsonDocument.Parse("{}").RootElement, CancellationToken.None);
        Assert.False(result.IsError);
        Assert.Equal("pong", result.Content);
        Assert.Equal("sess-wg-42", capturedSessionId);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class SingleHandlerHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class InMemoryCredentialStore(string key, string value) : ICredentialStore
    {
        public ValueTask<string?> GetApiKeyAsync(string providerId, CancellationToken ct = default) =>
            ValueTask.FromResult<string?>(string.Equals(providerId, key, StringComparison.OrdinalIgnoreCase) ? value : null);
        public ValueTask SetApiKeyAsync(string providerId, string apiKey, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask DeleteApiKeyAsync(string providerId, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<CredentialStatus> GetStatusAsync(string providerId, CancellationToken ct = default) =>
            ValueTask.FromResult(new CredentialStatus(true, "••••123"));
    }
}
