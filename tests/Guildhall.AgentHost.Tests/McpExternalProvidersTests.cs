using System.Net;
using System.Text;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Mcp.Providers;

namespace Guildhall.AgentHost.Tests;

public sealed class McpExternalProvidersTests
{
    private sealed class FakeCredentialStore(IReadOnlyDictionary<string, string> secrets) : ICredentialStore
    {
        public ValueTask<string?> GetApiKeyAsync(string providerId, CancellationToken ct = default) =>
            ValueTask.FromResult(secrets.GetValueOrDefault(providerId));
        public ValueTask SetApiKeyAsync(string providerId, string apiKey, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask DeleteApiKeyAsync(string providerId, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<CredentialStatus> GetStatusAsync(string providerId, CancellationToken ct = default) =>
            ValueTask.FromResult(secrets.TryGetValue(providerId, out var k)
                ? new CredentialStatus(true, $"••••{k[^4..]}")
                : new CredentialStatus(false, null));
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler, Uri baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = baseAddress };
    }

    [Fact]
    public async Task GitHubProviderProbesAndInvokesReadAndWriteTools()
    {
        var handler = new StubHttpHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery == "/user")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"login":"wdixon512"}""", Encoding.UTF8, "application/json") };
            if (req.RequestUri.PathAndQuery.StartsWith("/repos/wdixon512/guildhall/issues", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""[{"number":1,"title":"Phase 3"}]""", Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var creds = new FakeCredentialStore(new Dictionary<string, string> { ["github-mcp"] = "ghp_testtoken1234" });
        var provider = new GitHubMcpProvider(new StubFactory(handler, new Uri("https://api.github.com")), creds);

        var probe = await provider.ProbeAsync(default);
        Assert.True(probe.Healthy);
        Assert.Contains("@wdixon512", probe.Detail);

        var result = await provider.InvokeAsync(
            new McpToolCallRequest("github-mcp", "github_list_issues", JsonDocument.Parse("""{"owner":"wdixon512","repo":"guildhall"}""").RootElement, null),
            default);
        Assert.False(result.IsError);
        Assert.Contains("Phase 3", result.Content);
    }

    [Fact]
    public async Task GitHubProviderListsRepositoriesWithoutOwnerOrRepo()
    {
        var handler = new StubHttpHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery == "/user/repos?visibility=private&sort=updated&per_page=50")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """[{"full_name":"wdixon512/guildhall","private":true,"description":"Local council","html_url":"https://github.com/wdixon512/guildhall","default_branch":"main","updated_at":"2026-11-17T00:00:00Z","extra_unused_field":"trimmed"}]""",
                        Encoding.UTF8,
                        "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var creds = new FakeCredentialStore(new Dictionary<string, string> { ["github-mcp"] = "ghp_testtoken1234" });
        var provider = new GitHubMcpProvider(new StubFactory(handler, new Uri("https://api.github.com")), creds);

        var result = await provider.InvokeAsync(
            new McpToolCallRequest("github-mcp", "github_list_repositories", JsonDocument.Parse("""{"visibility":"private","perPage":99}""").RootElement, null),
            default);

        Assert.False(result.IsError);
        Assert.Contains("wdixon512/guildhall", result.Content);
        Assert.DoesNotContain("extra_unused_field", result.Content);
    }

    [Fact]
    public async Task GoogleDriveProviderSearchesAndExportsGoogleDocs()
    {
        var handler = new StubHttpHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.Contains("/drive/v3/files?q="))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"files":[{"id":"doc-1","name":"Roadmap"}]}""", Encoding.UTF8, "application/json") };
            if (path == "/drive/v3/files/doc-1?fields=id,name,mimeType")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"doc-1","mimeType":"application/vnd.google-apps.document"}""", Encoding.UTF8, "application/json") };
            if (path == "/drive/v3/files/doc-1/export?mimeType=text/plain")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Master plan notes", Encoding.UTF8, "text/plain") };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var creds = new FakeCredentialStore(new Dictionary<string, string> { ["google-drive-mcp"] = "ya29.test-token" });
        var provider = new GoogleDriveMcpProvider(new StubFactory(handler, new Uri("https://www.googleapis.com")), creds);

        var search = await provider.InvokeAsync(
            new McpToolCallRequest("google-drive-mcp", "gdrive_search_files", JsonDocument.Parse("""{"query":"Roadmap"}""").RootElement, null),
            default);
        Assert.False(search.IsError);
        Assert.Contains("doc-1", search.Content);

        var read = await provider.InvokeAsync(
            new McpToolCallRequest("google-drive-mcp", "gdrive_read_text_file", JsonDocument.Parse("""{"fileId":"doc-1"}""").RootElement, null),
            default);
        Assert.False(read.IsError);
        Assert.Equal("Master plan notes", read.Content);
    }

    [Fact]
    public async Task GmailProviderSearchesAndCreatesDraftWithoutSending()
    {
        var handler = new StubHttpHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.StartsWith("/gmail/v1/users/me/messages?q=", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"messages":[{"id":"msg-1"}]}""", Encoding.UTF8, "application/json") };
            if (req.Method == HttpMethod.Post && req.RequestUri!.PathAndQuery == "/gmail/v1/users/me/drafts")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"draft-1"}""", Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var creds = new FakeCredentialStore(new Dictionary<string, string> { ["gmail-mcp"] = "ya29.gmail-token" });
        var provider = new GmailMcpProvider(new StubFactory(handler, new Uri("https://gmail.googleapis.com")), creds);

        var search = await provider.InvokeAsync(
            new McpToolCallRequest("gmail-mcp", "gmail_search_messages", JsonDocument.Parse("""{"query":"guildhall"}""").RootElement, null),
            default);
        Assert.False(search.IsError);
        Assert.Contains("msg-1", search.Content);

        var draft = await provider.InvokeAsync(
            new McpToolCallRequest("gmail-mcp", "gmail_create_draft", JsonDocument.Parse("""{"to":"user@example.com","subject":"Status","body":"Ready"}""").RootElement, null),
            default);
        Assert.False(draft.IsError);
        Assert.Contains("draft-1", draft.Content);
    }
}
