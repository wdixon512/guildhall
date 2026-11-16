using System.Net;
using Guildhall.AgentHost.Providers.Ollama;

namespace Guildhall.AgentHost.Tests;

public sealed class ModelDiscoveryTests
{
    private static OllamaDiscovery Discovery(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) =>
        new(new HttpClient(new Handler(action)) { BaseAddress = new Uri("http://127.0.0.1:11434") });

    [Fact]
    public async Task ModelDiscoveryNormalizesProviderData()
    {
        var discovery = Discovery((request, _) =>
        {
            Assert.Equal("/api/tags", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"models":[{"name":"local:small","digest":"private-provider-detail"}]}""") });
        });
        var result = await discovery.DiscoverAsync(default);
        Assert.Equal("available", result.Status);
        Assert.Equal("local:small", Assert.Single(result.Models).Id);
        Assert.DoesNotContain("digest", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("{\"models\":[]}", "no_models")]
    [InlineData("bad json", "invalid_response")]
    [InlineData("{}", "invalid_response")]
    [InlineData("[]", "invalid_response")]
    [InlineData("null", "invalid_response")]
    [InlineData("{\"models\":[{}]}", "invalid_response")]
    [InlineData("{\"models\":[7]}", "invalid_response")]
    public async Task ModelDiscoveryHandlesEmptyAndMalformedResponses(string body, string expected)
    {
        var discovery = Discovery((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        Assert.Equal(expected, (await discovery.DiscoverAsync(default)).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ModelDiscoveryDoesNotAcceptRedirectsOrErrors(HttpStatusCode status)
    {
        var discovery = Discovery((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        Assert.Equal("unavailable", (await discovery.DiscoverAsync(default)).Status);
    }

    [Fact]
    public async Task ModelDiscoveryRejectsOversizedResponses()
    {
        var discovery = Discovery((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(new string('x', OllamaDiscovery.MaxResponseBytes + 1)) }));
        Assert.Equal("invalid_response", (await discovery.DiscoverAsync(default)).Status);
    }

    [Fact]
    public async Task ModelDiscoveryHandlesConnectionFailure()
    {
        var discovery = Discovery((_, _) => throw new HttpRequestException("Sensitive transport detail"));
        var result = await discovery.DiscoverAsync(default);
        Assert.Equal("unavailable", result.Status);
        Assert.Empty(result.Models);
    }

    [Fact]
    public async Task ModelDiscoveryPropagatesCallerCancellation()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var discovery = Discovery((_, token) => Task.FromCanceled<HttpResponseMessage>(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery.DiscoverAsync(canceled.Token));
    }

    [Fact]
    public async Task ModelDiscoveryHasFiniteTimeout()
    {
        var discovery = Discovery(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(); });
        Assert.Equal("unavailable", (await discovery.DiscoverAsync(default)).Status);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request, cancellationToken);
    }
}
