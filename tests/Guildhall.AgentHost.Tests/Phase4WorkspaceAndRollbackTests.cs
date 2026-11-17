using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Mcp.Providers;
using Guildhall.AgentHost.Providers.Cli;
using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Tests;

public sealed class Phase4WorkspaceAndRollbackTests
{
    private sealed class StubProcessRunner : ICliProcessRunner
    {
        public string? ResolveExecutable(string executableName) =>
            string.Equals(executableName, "git", StringComparison.OrdinalIgnoreCase) ? "git" : null;

        public void LaunchInteractiveLogin(string executablePath, IReadOnlyList<string> arguments) { }

        public Task<CliProcessResult> RunBufferedAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?> environment,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CliProcessResult(0, "git version 2.47.0", string.Empty));

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
    public void WorkspacePathGuardBlocksGitMetadataAndRootWrites()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"guildhall-p4-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var guard = new WorkspacePathGuard();
            Assert.False(guard.ValidateWritePath(tempRoot, ".").IsAllowed);
            Assert.False(guard.ValidateWritePath(tempRoot, ".git/config").IsAllowed);
            Assert.False(guard.ValidateWritePath(tempRoot, ".guildhall-worktrees/abc/file.txt").IsAllowed);
            Assert.False(guard.ValidateWritePath(tempRoot, "../escape.txt").IsAllowed);
            Assert.True(guard.ValidateWritePath(tempRoot, "src/Program.cs").IsAllowed);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(PermissionPolicies.AlwaysAsk, ToolSecurityTiers.Tier1, GateOutcome.RequireApproval)]
    [InlineData(PermissionPolicies.AlwaysAsk, ToolSecurityTiers.Tier2, GateOutcome.RequireApproval)]
    [InlineData(PermissionPolicies.AskUnsafe, ToolSecurityTiers.Tier1, GateOutcome.Allow)]
    [InlineData(PermissionPolicies.AskUnsafe, ToolSecurityTiers.Tier2, GateOutcome.RequireApproval)]
    [InlineData(PermissionPolicies.AskUnsafe, ToolSecurityTiers.Tier3, GateOutcome.RequireApproval)]
    [InlineData(PermissionPolicies.Unrestricted, ToolSecurityTiers.Tier2, GateOutcome.Allow)]
    [InlineData(PermissionPolicies.Unrestricted, ToolSecurityTiers.Tier3, GateOutcome.RequireApproval)]
    public void ToolPermissionGateEnforcesAccessModeMatrix(string policy, string tier, GateOutcome expected)
    {
        var gate = new ToolPermissionGate(new ExfiltrationGuard());
        var servers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "filesystem-mcp" };
        var decision = gate.Evaluate(policy, servers, "filesystem-mcp", "fs_write_file", tier, """{"path":"a.txt"}""");
        Assert.Equal(expected, decision.Outcome);
    }

    [Fact]
    public void ToolPermissionGateBlocksSecretsEvenInUnrestrictedMode()
    {
        var gate = new ToolPermissionGate(new ExfiltrationGuard());
        var servers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "filesystem-mcp" };
        var decision = gate.Evaluate(
            PermissionPolicies.Unrestricted,
            servers,
            "filesystem-mcp",
            "fs_write_file",
            ToolSecurityTiers.Tier2,
            """{"content":"sk-proj-1234567890abcdefghijklmnop"}""");
        Assert.Equal(GateOutcome.Deny, decision.Outcome);
        Assert.Equal("exfiltration_blocked", decision.ErrorCode);
    }

    [Fact]
    public async Task LocalFilesystemProviderWritesEditsAndDeletesFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"guildhall-p4-fs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var provider = new LocalFilesystemMcpProvider(new WorkspacePathGuard());

            var writeRes = await provider.InvokeAsync(
                new McpToolCallRequest("filesystem-mcp", "fs_write_file", JsonDocument.Parse("""{"path":"notes/hello.txt","content":"Hello world"}""").RootElement, tempRoot),
                default);
            Assert.False(writeRes.IsError);
            Assert.Equal("Hello world", await File.ReadAllTextAsync(Path.Combine(tempRoot, "notes", "hello.txt")));

            var editRes = await provider.InvokeAsync(
                new McpToolCallRequest("filesystem-mcp", "fs_edit_file", JsonDocument.Parse("""{"path":"notes/hello.txt","target":"world","replacement":"Guildhall"}""").RootElement, tempRoot),
                default);
            Assert.False(editRes.IsError);
            Assert.Equal("Hello Guildhall", await File.ReadAllTextAsync(Path.Combine(tempRoot, "notes", "hello.txt")));

            var delRes = await provider.InvokeAsync(
                new McpToolCallRequest("filesystem-mcp", "fs_delete_file", JsonDocument.Parse("""{"path":"notes/hello.txt"}""").RootElement, tempRoot),
                default);
            Assert.False(delRes.IsError);
            Assert.False(File.Exists(Path.Combine(tempRoot, "notes", "hello.txt")));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task LocalCommandProviderRejectsDisallowedCommandsAndRunsAllowlisted()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"guildhall-p4-cmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var provider = new LocalCommandMcpProvider(
                new StubProcessRunner(),
                new EnvironmentScrubber(),
                new WorkspacePathGuard(),
                new ExfiltrationGuard());

            var blocked = await provider.InvokeAsync(
                new McpToolCallRequest("workspace-exec-mcp", "workspace_run_command", JsonDocument.Parse("""{"command":"cmd","args":["/c","echo hi"]}""").RootElement, tempRoot),
                default);
            Assert.True(blocked.IsError);
            Assert.Equal("command_not_allowed", blocked.ErrorCode);

            var allowed = await provider.InvokeAsync(
                new McpToolCallRequest("workspace-exec-mcp", "workspace_run_command", JsonDocument.Parse("""{"command":"git","args":["--version"]}""").RootElement, tempRoot),
                default);
            Assert.False(allowed.IsError);
            Assert.Contains("git version 2.47.0", allowed.Content);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void CliHarnessDescriptorsRespectReadOnlyVsUnrestrictedModes()
    {
        var codexRo = CliHarnessDescriptors.BuildRunArguments("codex-cli", "codex-default", "read_only", "C:\\repo");
        Assert.Contains("read-only", codexRo);

        var codexUnrestricted = CliHarnessDescriptors.BuildRunArguments("codex-cli", "codex-default", "unrestricted", "C:\\repo");
        Assert.Contains("workspace-write", codexUnrestricted);

        var geminiRo = CliHarnessDescriptors.BuildRunArguments("gemini-cli", "gemini-2.5-pro", "read_only", "C:\\repo");
        Assert.Contains("--sandbox", geminiRo);

        var geminiUnrestricted = CliHarnessDescriptors.BuildRunArguments("gemini-cli", "gemini-2.5-pro", "unrestricted", "C:\\repo");
        Assert.DoesNotContain("--sandbox", geminiUnrestricted);
    }
}
