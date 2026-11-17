using System.Net;
using System.Text;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Providers;
using Guildhall.AgentHost.Providers.Cli;
using Guildhall.AgentHost.Providers.Ollama;
using Microsoft.Extensions.Logging.Abstractions;

namespace Guildhall.AgentHost.Tests;

public sealed class ConnectionCatalogTests
{
    private sealed class StubCredentialStore(IReadOnlyDictionary<string, string> keys) : ICredentialStore
    {
        public ValueTask<string?> GetApiKeyAsync(string providerId, CancellationToken ct = default) =>
            ValueTask.FromResult(keys.GetValueOrDefault(providerId));
        public ValueTask SetApiKeyAsync(string providerId, string apiKey, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask DeleteApiKeyAsync(string providerId, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<CredentialStatus> GetStatusAsync(string providerId, CancellationToken ct = default) =>
            ValueTask.FromResult(keys.TryGetValue(providerId, out var k)
                ? new CredentialStatus(true, $"••••{k[^4..]}")
                : new CredentialStatus(false, null));
    }

    private sealed class StubCliRunner(string? installedCommand) : ICliProcessRunner
    {
        public string? ResolveExecutable(string commandName) =>
            string.Equals(commandName, installedCommand, StringComparison.OrdinalIgnoreCase)
                ? $@"C:\tools\{commandName}.exe"
                : null;

        public void LaunchInteractiveLogin(string executablePath, IReadOnlyList<string> arguments) { }

        public Task<CliProcessResult> RunBufferedAsync(string executablePath, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken) =>
            Task.FromResult(new CliProcessResult(0, "codex-cli 1.0.0 (logged in)", ""));

        public Task<int> StreamJsonLinesAsync(string executablePath, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?> environment, string? standardInput, Func<string, CancellationToken, ValueTask> onStdoutLine, Func<string, CancellationToken, ValueTask>? onStderrLine, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"models\":[]}", Encoding.UTF8, "application/json")
            });
    }

    private sealed class StubClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    [Fact]
    public async Task CatalogReportsAllThreePillarsWithAccurateStatuses()
    {
        var httpClient = new HttpClient(new StubHandler()) { BaseAddress = new Uri("http://127.0.0.1:11434") };
        var ollama = new OllamaDiscovery(httpClient);
        var creds = new StubCredentialStore(new Dictionary<string, string> { ["openai-api"] = "sk-12345678" });
        var cli = new StubCliRunner("codex");
        var service = new ConnectionCatalogService(
            ollama,
            creds,
            cli,
            new EnvironmentScrubber(),
            new StubClientFactory(httpClient),
            TimeProvider.System);

        var catalog = await service.GetCatalogAsync(runLiveProbes: true, CancellationToken.None);

        var ollamaEntry = catalog.Entries.Single(x => x.Id == "ollama");
        Assert.Equal("Connected", ollamaEntry.Status);

        var openai = catalog.Entries.Single(x => x.Id == "openai-api");
        Assert.Equal("Connected", openai.Status);
        Assert.Equal("••••5678", openai.KeyHint);

        var geminiApi = catalog.Entries.Single(x => x.Id == "gemini-api");
        Assert.Equal("Not connected", geminiApi.Status);

        var codexCli = catalog.Entries.Single(x => x.Id == "codex-cli");
        Assert.Equal("Connected", codexCli.Status);

        var agyCli = catalog.Entries.Single(x => x.Id == "agy-cli");
        Assert.Equal("Unavailable", agyCli.Status);

        var fsMcp = catalog.Entries.Single(x => x.Id == "filesystem-mcp");
        Assert.Equal("Connected", fsMcp.Status);
        Assert.False(fsMcp.IsPlanned);

        var github = catalog.Entries.Single(x => x.Id == "github-mcp");
        Assert.Equal("Not connected", github.Status);
        Assert.True(github.CanConfigureApiKey);
        Assert.False(github.IsPlanned);

        var sqlMcp = catalog.Entries.Single(x => x.Id == "sql-server-mcp");
        Assert.Equal("Not connected", sqlMcp.Status);
        Assert.True(sqlMcp.CanProbe);
        Assert.False(sqlMcp.IsPlanned);
    }
}
