namespace Guildhall.AgentHost.Providers.Cli;

public sealed record CliHarnessDescriptor(
    string ProviderId,
    string DisplayName,
    string ExecutableName,
    string DefaultModelId,
    IReadOnlyList<string> ProbeArgs,
    IReadOnlyList<string> LoginArgs,
    string SignInHint);

public static class CliHarnessDescriptors
{
    public static readonly IReadOnlyList<CliHarnessDescriptor> All =
    [
        new(
            ProviderId: "codex-cli",
            DisplayName: "OpenAI Codex CLI",
            ExecutableName: "codex",
            DefaultModelId: "codex-default",
            ProbeArgs: ["login", "status"],
            LoginArgs: ["login"],
            SignInHint: "Run `codex login` in your terminal to sign in with ChatGPT Plus/Pro."),
        new(
            ProviderId: "agy-cli",
            DisplayName: "Antigravity CLI",
            ExecutableName: "agy",
            DefaultModelId: "agy-default",
            ProbeArgs: ["--version"],
            LoginArgs: [],
            SignInHint: "Run `agy` in your terminal to sign in with your Google account."),
        new(
            ProviderId: "claude-cli",
            DisplayName: "Claude Code CLI",
            ExecutableName: "claude",
            DefaultModelId: "claude-sonnet-4",
            ProbeArgs: ["--version"],
            LoginArgs: [],
            SignInHint: "Run `claude` in your terminal to sign in with your Claude Pro/Max subscription."),
        new(
            ProviderId: "cursor-cli",
            DisplayName: "Cursor Agent CLI",
            ExecutableName: "agent",
            DefaultModelId: "cursor-agent",
            ProbeArgs: ["--version"],
            LoginArgs: ["login"],
            SignInHint: "Run `agent login` in your terminal to sign in with your Cursor subscription."),
        new(
            ProviderId: "gemini-cli",
            DisplayName: "Google Gemini CLI",
            ExecutableName: "gemini",
            DefaultModelId: "gemini-2.5-pro",
            ProbeArgs: ["--version"],
            LoginArgs: [],
            SignInHint: "Run `gemini` in your terminal to sign in with your Google account.")
    ];

    public static CliHarnessDescriptor? Find(string providerId) =>
        All.FirstOrDefault(x => string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> BuildRunArguments(
        string providerId,
        string modelId,
        string? harnessMode,
        string workingDirectory,
        string? inlinePrompt = null)
    {
        var allowWorkspaceWrite =
            string.Equals(harnessMode, "workspace_write", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(harnessMode, "unrestricted", StringComparison.OrdinalIgnoreCase);

        return providerId.ToLowerInvariant() switch
        {
            "codex-cli" => BuildCodexArgs(modelId, allowWorkspaceWrite, workingDirectory),
            "agy-cli" => BuildAgyArgs(modelId, allowWorkspaceWrite, inlinePrompt ?? string.Empty),
            "claude-cli" => BuildClaudeArgs(modelId, allowWorkspaceWrite),
            "cursor-cli" => BuildCursorArgs(modelId, allowWorkspaceWrite),
            "gemini-cli" => BuildGeminiArgs(modelId, allowWorkspaceWrite),
            _ => throw new BackingEngineException("provider_unavailable", $"Unknown CLI harness '{providerId}'.")
        };
    }

    private static IReadOnlyList<string> BuildCodexArgs(string modelId, bool allowWorkspaceWrite, string workingDirectory)
    {
        var args = new List<string>
        {
            "exec",
            "--json",
            "--sandbox",
            allowWorkspaceWrite ? "workspace-write" : "read-only",
            "-C",
            workingDirectory
        };
        if (!string.IsNullOrWhiteSpace(modelId) && !string.Equals(modelId, "codex-default", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-m");
            args.Add(modelId);
        }
        args.Add("-");
        return args;
    }

    private static IReadOnlyList<string> BuildAgyArgs(string modelId, bool allowWorkspaceWrite, string prompt)
    {
        var args = new List<string>
        {
            "--output-format",
            "stream-json",
            "--mode",
            allowWorkspaceWrite ? "accept-edits" : "plan"
        };
        if (!string.IsNullOrWhiteSpace(modelId) && !string.Equals(modelId, "agy-default", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--model");
            args.Add(modelId);
        }
        var boundedPrompt = prompt.Length > 24_000 ? prompt[..24_000] : prompt;
        args.Add($"-p={boundedPrompt}");
        return args;
    }

    private static IReadOnlyList<string> BuildClaudeArgs(string modelId, bool allowWorkspaceWrite)
    {
        var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose" };
        if (!string.IsNullOrWhiteSpace(modelId) && !string.Equals(modelId, "claude-default", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--model");
            args.Add(modelId);
        }
        args.Add("--permission-mode");
        args.Add(allowWorkspaceWrite ? "acceptEdits" : "plan");
        return args;
    }

    private static IReadOnlyList<string> BuildCursorArgs(string modelId, bool allowWorkspaceWrite)
    {
        var args = new List<string> { "-p", "--output-format", "stream-json" };
        if (!allowWorkspaceWrite)
            args.AddRange(["--mode", "plan"]);
        if (!string.IsNullOrWhiteSpace(modelId) && !string.Equals(modelId, "cursor-agent", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--model");
            args.Add(modelId);
        }
        return args;
    }

    private static IReadOnlyList<string> BuildGeminiArgs(string modelId, bool allowWorkspaceWrite)
    {
        var args = new List<string> { "--output-format", "stream-json" };
        if (!allowWorkspaceWrite)
            args.Add("--sandbox");
        if (!string.IsNullOrWhiteSpace(modelId) && !string.Equals(modelId, "gemini-2.5-pro", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-m");
            args.Add(modelId);
        }
        return args;
    }
}
