using System.Text;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Providers.Cli;
using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Mcp.Providers;

public sealed class LocalGitMcpProvider(
    ICliProcessRunner processRunner,
    EnvironmentScrubber environmentScrubber,
    WorkspacePathGuard pathGuard) : IMcpToolProvider
{
    private const int MaxOutputChars = 32 * 1024;

    private static readonly IReadOnlyList<McpToolDescriptor> Tools =
    [
        CreateDescriptor(
            "git_status",
            "Inspect the current Git branch and working tree status (read-only).",
            """{"type":"object","properties":{},"required":[]}"""),
        CreateDescriptor(
            "git_log",
            "Show recent Git commits on the current branch (read-only).",
            """{"type":"object","properties":{"maxCount":{"type":"integer","description":"Number of commits to show (1-25, default 10)."}},"required":[]}"""),
        CreateDescriptor(
            "git_diff",
            "Show unstaged or target diff in the authorized repository (read-only).",
            """{"type":"object","properties":{"target":{"type":"string","description":"Optional ref/commit or relative file path."}},"required":[]}"""),
        CreateDescriptor(
            "git_show",
            "Show metadata and diff for a specific commit hash or ref (read-only).",
            """{"type":"object","properties":{"revision":{"type":"string","description":"Commit hash or ref (default HEAD)."}},"required":[]}"""),
    ];

    public string ServerId => "git-mcp";
    public string DisplayName => "Local Git (Read-Only)";
    public string Transport => "builtin";
    public string DefaultSecurityTier => ToolSecurityTiers.Tier1;

    public IReadOnlyList<McpToolDescriptor> GetTools() => Tools;

    public Task<(bool Healthy, string? Detail)> ProbeAsync(CancellationToken cancellationToken)
    {
        var path = processRunner.ResolveExecutable("git");
        return Task.FromResult<(bool, string?)>(path is not null
            ? (true, "git CLI available on PATH")
            : (false, "git CLI not found on PATH"));
    }

    public async Task<McpToolCallResult> InvokeAsync(McpToolCallRequest request, CancellationToken cancellationToken)
    {
        var rootCheck = pathGuard.ValidateReadPath(request.WorkspaceRootPath, ".");
        if (!rootCheck.IsAllowed || rootCheck.FullPath is null)
            return new McpToolCallResult(true, rootCheck.ErrorMessage ?? "Workspace root not configured.", rootCheck.ErrorCode);

        var gitPath = processRunner.ResolveExecutable("git");
        if (gitPath is null)
            return new McpToolCallResult(true, "git CLI not found on PATH.", "git_not_installed");

        var args = new List<string> { "--no-pager" };
        switch (request.ToolName)
        {
            case "git_status":
                args.AddRange(["status", "--short", "--branch"]);
                break;
            case "git_log":
            {
                var maxCount = request.Arguments.ValueKind == JsonValueKind.Object &&
                               request.Arguments.TryGetProperty("maxCount", out var mc) &&
                               mc.TryGetInt32(out var val)
                    ? Math.Clamp(val, 1, 25)
                    : 10;
                args.AddRange(["log", $"--max-count={maxCount}", "--oneline", "--decorate"]);
                break;
            }
            case "git_diff":
            {
                args.Add("diff");
                if (request.Arguments.ValueKind == JsonValueKind.Object &&
                    request.Arguments.TryGetProperty("target", out var targetEl) &&
                    targetEl.ValueKind == JsonValueKind.String &&
                    targetEl.GetString() is { Length: > 0 } target &&
                    !target.StartsWith('-'))
                {
                    args.Add(target);
                }
                break;
            }
            case "git_show":
            {
                var rev = request.Arguments.ValueKind == JsonValueKind.Object &&
                          request.Arguments.TryGetProperty("revision", out var revEl) &&
                          revEl.ValueKind == JsonValueKind.String &&
                          revEl.GetString() is { Length: > 0 } r &&
                          !r.StartsWith('-')
                    ? r
                    : "HEAD";
                args.AddRange(["show", "--stat", "--patch", rev]);
                break;
            }
            default:
                return new McpToolCallResult(true, $"Unknown tool '{request.ToolName}'.", "tool_not_found");
        }

        var result = await processRunner.RunBufferedAsync(
            gitPath,
            args,
            rootCheck.FullPath,
            environmentScrubber.BuildScrubbedEnvironment(),
            cancellationToken);

        if (result.ExitCode != 0)
        {
            var err = string.IsNullOrWhiteSpace(result.StdErr) ? $"git exited with code {result.ExitCode}." : result.StdErr.Trim();
            return new McpToolCallResult(true, err, "git_error");
        }

        var output = result.StdOut.Length > MaxOutputChars
            ? result.StdOut[..MaxOutputChars] + "\n… [truncated]"
            : result.StdOut.TrimEnd();

        return new McpToolCallResult(false, string.IsNullOrEmpty(output) ? "(no output)" : output);
    }

    private static McpToolDescriptor CreateDescriptor(string name, string description, string schemaJson) =>
        new("git-mcp", name, description, ToolSecurityTiers.Tier1, JsonDocument.Parse(schemaJson).RootElement.Clone());
}
