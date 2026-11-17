using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Tests;

public sealed class WorkspacePathGuardTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "guildhall-path-guard-" + Guid.NewGuid().ToString("N"));

    public WorkspacePathGuardTests()
    {
        Directory.CreateDirectory(_workspaceRoot);
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src"));
        File.WriteAllText(Path.Combine(_workspaceRoot, "src", "Program.cs"), "Console.WriteLine(\"ok\");");
        File.WriteAllText(Path.Combine(_workspaceRoot, ".env"), "SECRET=123");
        File.WriteAllText(Path.Combine(_workspaceRoot, ".env.local"), "SECRET=456");
        File.WriteAllText(Path.Combine(_workspaceRoot, "secrets.json"), "{}");
        File.WriteAllText(Path.Combine(_workspaceRoot, "id_ed25519"), "key");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
            Directory.Delete(_workspaceRoot, recursive: true);
    }

    [Fact]
    public void AllowsValidRelativeFileInsideWorkspace()
    {
        var guard = new WorkspacePathGuard();
        var result = guard.ValidateReadPath(_workspaceRoot, "src/Program.cs");

        Assert.True(result.IsAllowed);
        Assert.Equal(Path.Combine(_workspaceRoot, "src", "Program.cs"), result.FullPath);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void RejectsMissingWorkspaceRoot()
    {
        var guard = new WorkspacePathGuard();
        var result = guard.ValidateReadPath(null, "src/Program.cs");

        Assert.False(result.IsAllowed);
        Assert.Equal("workspace_not_configured", result.ErrorCode);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("src/../../outside.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    [InlineData("//server/share/file.txt")]
    public void RejectsTraversalAndUncPaths(string requestedPath)
    {
        var guard = new WorkspacePathGuard();
        var result = guard.ValidateReadPath(_workspaceRoot, requestedPath);

        Assert.False(result.IsAllowed);
        Assert.Equal("path_outside_workspace", result.ErrorCode);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData("secrets.json")]
    [InlineData("id_ed25519")]
    [InlineData("runtime.credential.xml")]
    [InlineData("openai.dpapi")]
    [InlineData(".git/credentials")]
    public void BlocksSensitiveCredentialAndSecretFiles(string requestedPath)
    {
        var guard = new WorkspacePathGuard();
        var result = guard.ValidateReadPath(_workspaceRoot, requestedPath);

        Assert.False(result.IsAllowed);
        Assert.Equal("sensitive_file_blocked", result.ErrorCode);
    }
}
