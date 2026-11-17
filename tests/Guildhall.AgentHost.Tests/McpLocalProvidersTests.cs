using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Mcp.Providers;
using Guildhall.AgentHost.Providers.Cli;
using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Tests;

public sealed class McpLocalProvidersTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "guildhall-mcp-local-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspacePathGuard _pathGuard = new();

    public McpLocalProvidersTests()
    {
        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "docs"));
        File.WriteAllText(Path.Combine(_workspaceRoot, "README.md"), "line1\nline2\nGuildhall Phase 3\n");
        File.WriteAllText(Path.Combine(_workspaceRoot, ".env"), "SECRET=hidden");
        File.WriteAllText(Path.Combine(_workspaceRoot, "docs", "notes.txt"), "notes content");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
            Directory.Delete(_workspaceRoot, recursive: true);
    }

    [Fact]
    public async Task FilesystemProviderListsReadsAndSearchesSafelyWithoutExposingSecrets()
    {
        var provider = new LocalFilesystemMcpProvider(_pathGuard);

        var listResult = await provider.InvokeAsync(
            new McpToolCallRequest("filesystem-mcp", "fs_list_directory", JsonDocument.Parse("{}").RootElement, _workspaceRoot),
            default);
        Assert.False(listResult.IsError);
        Assert.Contains("README.md", listResult.Content);
        Assert.Contains("docs/", listResult.Content);
        Assert.DoesNotContain(".env", listResult.Content);

        var readResult = await provider.InvokeAsync(
            new McpToolCallRequest("filesystem-mcp", "fs_read_file", JsonDocument.Parse("""{"path":"README.md","startLine":2,"endLine":3}""").RootElement, _workspaceRoot),
            default);
        Assert.False(readResult.IsError);
        Assert.Contains("2: line2", readResult.Content);
        Assert.Contains("3: Guildhall Phase 3", readResult.Content);
        Assert.DoesNotContain("1: line1", readResult.Content);

        var secretRead = await provider.InvokeAsync(
            new McpToolCallRequest("filesystem-mcp", "fs_read_file", JsonDocument.Parse("""{"path":".env"}""").RootElement, _workspaceRoot),
            default);
        Assert.True(secretRead.IsError);
        Assert.Equal("sensitive_file_blocked", secretRead.ErrorCode);

        var searchResult = await provider.InvokeAsync(
            new McpToolCallRequest("filesystem-mcp", "fs_search_files", JsonDocument.Parse("""{"pattern":"Phase 3"}""").RootElement, _workspaceRoot),
            default);
        Assert.False(searchResult.IsError);
        Assert.Contains("README.md:3: Guildhall Phase 3", searchResult.Content);
    }

    private sealed class FakeGitRunner : ICliProcessRunner
    {
        public IReadOnlyList<string>? LastArguments { get; private set; }
        public IReadOnlyDictionary<string, string?>? LastEnvironment { get; private set; }

        public string? ResolveExecutable(string commandName) => @"C:\Program Files\Git\cmd\git.exe";

        public void LaunchInteractiveLogin(string executablePath, IReadOnlyList<string> arguments) { }

        public Task<CliProcessResult> RunBufferedAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?> environment,
            CancellationToken cancellationToken)
        {
            LastArguments = arguments;
            LastEnvironment = environment;
            return Task.FromResult(new CliProcessResult(0, "## phase_three_implementation_plan\n", string.Empty));
        }

        public Task<int> StreamJsonLinesAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?> environment,
            string? standardInput,
            Func<string, CancellationToken, ValueTask> onStdoutLine,
            Func<string, CancellationToken, ValueTask>? onStderrLine,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    [Fact]
    public async Task GitProviderRunsReadOnlyCommandsWithScrubbedEnv()
    {
        var runner = new FakeGitRunner();
        var provider = new LocalGitMcpProvider(runner, new EnvironmentScrubber(), _pathGuard);

        var result = await provider.InvokeAsync(
            new McpToolCallRequest("git-mcp", "git_status", JsonDocument.Parse("{}").RootElement, _workspaceRoot),
            default);

        Assert.False(result.IsError);
        Assert.Contains("## phase_three_implementation_plan", result.Content);
        Assert.NotNull(runner.LastArguments);
        Assert.Equal(["--no-pager", "status", "--short", "--branch"], runner.LastArguments!);
        Assert.DoesNotContain("GUILDHALL_SESSION_TOKEN", runner.LastEnvironment!.Keys);
    }

    [Fact]
    public void OllamaFallbackExtractsXmlToolCallsEmittedInsideThinking()
    {
        var raw = """
            Let me call both tools.
            <tool_call>
            {"name": "git-mcp__git_status", "arguments": {}}
            </tool_call>
            <tool_call>
            {"name": "git-mcp__git_log", "arguments": {"maxCount": 5}}
            </tool_call>
            """;

        var calls = Guildhall.AgentHost.Providers.Ollama.OllamaBackingEngine.ExtractXmlToolCalls(raw);
        Assert.NotNull(calls);
        Assert.Equal(2, calls!.Count);
        Assert.Equal("git-mcp__git_status", calls[0].QualifiedName);
        Assert.Equal("git-mcp__git_log", calls[1].QualifiedName);
        Assert.Equal(5, calls[1].Arguments.GetProperty("maxCount").GetInt32());
    }
}
