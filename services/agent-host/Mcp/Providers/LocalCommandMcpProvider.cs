using System.Text;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Providers.Cli;
using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Mcp.Providers;

public sealed class LocalCommandMcpProvider(
    ICliProcessRunner processRunner,
    EnvironmentScrubber environmentScrubber,
    WorkspacePathGuard pathGuard,
    ExfiltrationGuard exfiltrationGuard) : IMcpToolProvider
{
    private const int MaxOutputChars = 32 * 1024;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    private static readonly HashSet<string> AllowedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet", "npm", "npx", "node", "git", "cargo", "rustc", "python", "pytest", "pwsh"
    };

    private static readonly IReadOnlyList<McpToolDescriptor> Tools =
    [
        new(
            "workspace-exec-mcp",
            "workspace_run_command",
            "Run an allowlisted build, test, or git command directly inside the authorized workspace/worktree (Tier 2: no shell interpolation, 60s timeout).",
            ToolSecurityTiers.Tier2,
            JsonDocument.Parse("""
                {
                  "type": "object",
                  "properties": {
                    "command": {
                      "type": "string",
                      "description": "Allowlisted executable name (dotnet, npm, npx, node, git, cargo, rustc, python, pytest, pwsh)."
                    },
                    "args": {
                      "type": "array",
                      "items": { "type": "string" },
                      "description": "Command arguments passed directly without shell interpolation."
                    },
                    "subPath": {
                      "type": "string",
                      "description": "Optional relative working directory inside the workspace (default '.')."
                    }
                  },
                  "required": ["command"]
                }
                """).RootElement.Clone())
    ];

    public string ServerId => "workspace-exec-mcp";
    public string DisplayName => "Workspace Commands (Sandboxed)";
    public string Transport => "builtin";
    public string DefaultSecurityTier => ToolSecurityTiers.Tier2;

    public IReadOnlyList<McpToolDescriptor> GetTools() => Tools;

    public Task<(bool Healthy, string? Detail)> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult<(bool, string?)>((true, "Built-in allowlisted command runner ready"));

    public async Task<McpToolCallResult> InvokeAsync(McpToolCallRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.ToolName, "workspace_run_command", StringComparison.Ordinal))
            return new McpToolCallResult(true, $"Unknown tool '{request.ToolName}'.", "tool_not_found");

        var command = request.Arguments.ValueKind == JsonValueKind.Object &&
                      request.Arguments.TryGetProperty("command", out var cmdEl) &&
                      cmdEl.ValueKind == JsonValueKind.String
            ? cmdEl.GetString()?.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(command))
            return new McpToolCallResult(true, "Missing required 'command' argument.", "invalid_arguments");

        if (command.Contains('/') || command.Contains('\\') || !AllowedCommands.Contains(command))
        {
            return new McpToolCallResult(
                true,
                $"Command '{command}' is not in the workspace allowlist ({string.Join(", ", AllowedCommands.OrderBy(x => x))}).",
                "command_not_allowed");
        }

        var subPath = request.Arguments.ValueKind == JsonValueKind.Object &&
                      request.Arguments.TryGetProperty("subPath", out var subEl) &&
                      subEl.ValueKind == JsonValueKind.String
            ? subEl.GetString()
            : ".";
        var dirCheck = pathGuard.ValidateReadPath(request.WorkspaceRootPath, subPath);
        if (!dirCheck.IsAllowed || dirCheck.FullPath is null)
            return new McpToolCallResult(true, dirCheck.ErrorMessage ?? "Workspace root not configured.", dirCheck.ErrorCode);

        if (!Directory.Exists(dirCheck.FullPath))
            return new McpToolCallResult(true, "Working directory not found.", "directory_not_found");

        var executable = processRunner.ResolveExecutable(command);
        if (executable is null)
            return new McpToolCallResult(true, $"Executable '{command}' was not found on PATH.", "command_not_installed");

        var args = new List<string>();
        if (request.Arguments.ValueKind == JsonValueKind.Object &&
            request.Arguments.TryGetProperty("args", out var argsEl) &&
            argsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in argsEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                    args.Add(s);
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CommandTimeout);

        CliProcessResult result;
        try
        {
            result = await processRunner.RunBufferedAsync(
                executable,
                args,
                dirCheck.FullPath,
                environmentScrubber.BuildScrubbedEnvironment(),
                timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new McpToolCallResult(true, $"Command '{command}' timed out after {(int)CommandTimeout.TotalSeconds}s.", "command_timeout");
        }

        var combined = new StringBuilder();
        combined.AppendLine($"Exit code: {result.ExitCode}");
        if (!string.IsNullOrWhiteSpace(result.StdOut))
        {
            combined.AppendLine("--- stdout ---");
            combined.AppendLine(exfiltrationGuard.RedactSecrets(result.StdOut.TrimEnd()));
        }
        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            combined.AppendLine("--- stderr ---");
            combined.AppendLine(exfiltrationGuard.RedactSecrets(result.StdErr.TrimEnd()));
        }

        var output = combined.ToString().TrimEnd();
        if (output.Length > MaxOutputChars)
            output = output[..MaxOutputChars] + "\n… [truncated at 32 KiB]";

        return new McpToolCallResult(result.ExitCode != 0, output, result.ExitCode != 0 ? "command_failed" : null);
    }
}
