using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Providers;
using Guildhall.AgentHost.Providers.Cli;

namespace Guildhall.AgentHost.Tests;

public sealed class CliHarnessTests
{
    private sealed class FakeCliProcessRunner(
        string? resolvedPath,
        IReadOnlyList<string> stdoutLines,
        int exitCode = 0,
        string stderrLine = "") : ICliProcessRunner
    {
        public IReadOnlyList<string>? CapturedArgs { get; private set; }
        public IReadOnlyDictionary<string, string?>? CapturedEnv { get; private set; }
        public string? CapturedStdin { get; private set; }

        public string? ResolveExecutable(string commandName) => resolvedPath;

        public void LaunchInteractiveLogin(string executablePath, IReadOnlyList<string> arguments) { }

        public Task<CliProcessResult> RunBufferedAsync(string executablePath, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken) =>
            Task.FromResult(new CliProcessResult(exitCode, string.Join("\n", stdoutLines), stderrLine));

        public async Task<int> StreamJsonLinesAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?> environment,
            string? standardInput,
            Func<string, CancellationToken, ValueTask> onStdoutLine,
            Func<string, CancellationToken, ValueTask>? onStderrLine,
            CancellationToken cancellationToken)
        {
            CapturedArgs = arguments;
            CapturedEnv = environment;
            CapturedStdin = standardInput;

            if (!string.IsNullOrEmpty(stderrLine) && onStderrLine is not null)
                await onStderrLine(stderrLine, cancellationToken);

            foreach (var line in stdoutLines)
                await onStdoutLine(line, cancellationToken);

            return exitCode;
        }
    }

    [Fact]
    public async Task CodexCliStreamsAgentMessageAndNeverPassesYolo()
    {
        var descriptor = CliHarnessDescriptors.Find("codex-cli")!;
        var runner = new FakeCliProcessRunner(
            @"C:\tools\codex.exe",
            [
                "{\"type\":\"thread.started\",\"thread_id\":\"t1\"}",
                "{\"type\":\"item.completed\",\"item\":{\"id\":\"i1\",\"type\":\"agent_message\",\"text\":\"Wrote Hello World to hello.txt.\"}}",
                "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":40,\"output_tokens\":10}}"
            ]);

        var engine = new CliHarnessEngine(descriptor, runner, new EnvironmentScrubber());
        var request = new BackingEngineRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "codex-cli",
            "codex-default",
            "workspace_write",
            "Global guidelines + member rules",
            [new BackingEngineMessage("user", "Write Hello World to hello.txt", [])],
            Environment.CurrentDirectory);

        var deltas = new List<string>();
        BackingEngineUsage? usage = null;
        await engine.StreamTurnAsync(request, (chunk, _) =>
        {
            if (chunk.DeltaText is not null) deltas.Add(chunk.DeltaText);
            if (chunk.Usage is not null) usage = chunk.Usage;
            return ValueTask.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal("Wrote Hello World to hello.txt.", string.Concat(deltas));
        Assert.NotNull(runner.CapturedArgs);
        Assert.Contains("workspace-write", runner.CapturedArgs!);
        Assert.DoesNotContain("--yolo", runner.CapturedArgs!);
        Assert.DoesNotContain("--dangerously-skip-permissions", runner.CapturedArgs!);
        Assert.Contains("[Guildhall System Context]", runner.CapturedStdin);
        Assert.Equal(CostSources.SubscriptionCli, usage?.CostSource);
    }

    [Fact]
    public async Task MissingCliBinaryThrowsCliNotInstalled()
    {
        var descriptor = CliHarnessDescriptors.Find("agy-cli")!;
        var runner = new FakeCliProcessRunner(null, []);
        var engine = new CliHarnessEngine(descriptor, runner, new EnvironmentScrubber());
        var request = new BackingEngineRequest(Guid.NewGuid(), Guid.NewGuid(), "agy-cli", "agy-default", null, "Sys", [new BackingEngineMessage("user", "Hi", [])]);

        var ex = await Assert.ThrowsAsync<BackingEngineException>(() =>
            engine.StreamTurnAsync(request, (_, _) => ValueTask.CompletedTask, CancellationToken.None));

        Assert.Equal("cli_not_installed", ex.ErrorCode);
    }

    [Fact]
    public async Task AuthFailureStderrMapsToCliNeedsSignin()
    {
        var descriptor = CliHarnessDescriptors.Find("codex-cli")!;
        var runner = new FakeCliProcessRunner(@"C:\tools\codex.exe", [], exitCode: 1, stderrLine: "Error: Not logged in. Please sign in.");
        var engine = new CliHarnessEngine(descriptor, runner, new EnvironmentScrubber());
        var request = new BackingEngineRequest(Guid.NewGuid(), Guid.NewGuid(), "codex-cli", "codex-default", null, "Sys", [new BackingEngineMessage("user", "Hi", [])]);

        var ex = await Assert.ThrowsAsync<BackingEngineException>(() =>
            engine.StreamTurnAsync(request, (_, _) => ValueTask.CompletedTask, CancellationToken.None));

        Assert.Equal("cli_needs_signin", ex.ErrorCode);
    }

    [Fact]
    public async Task AgyCliExtractsResultResponseAndNestedUsage()
    {
        var descriptor = CliHarnessDescriptors.Find("agy-cli")!;
        var runner = new FakeCliProcessRunner(
            @"C:\tools\agy.exe",
            [
                "{\"event\":\"init\",\"conversation_id\":\"c1\"}",
                "{\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"response\":\"Hello from Antigravity!\",\"usage\":{\"input_tokens\":25,\"output_tokens\":6}}}"
            ]);

        var engine = new CliHarnessEngine(descriptor, runner, new EnvironmentScrubber());
        var request = new BackingEngineRequest(Guid.NewGuid(), Guid.NewGuid(), "agy-cli", "agy-default", null, "Sys", [new BackingEngineMessage("user", "Hello!", [])]);

        var deltas = new List<string>();
        BackingEngineUsage? usage = null;
        await engine.StreamTurnAsync(request, (chunk, _) =>
        {
            if (chunk.DeltaText is not null) deltas.Add(chunk.DeltaText);
            if (chunk.Usage is not null) usage = chunk.Usage;
            return ValueTask.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal("Hello from Antigravity!", string.Concat(deltas));
        Assert.Equal(25, usage?.InputTokens);
        Assert.Equal(6, usage?.OutputTokens);
        Assert.Null(runner.CapturedStdin);
    }

    [Fact]
    public async Task AgyCliDeniedActionsWithEmptyResponseThrowsCliPermissionDenied()
    {
        var descriptor = CliHarnessDescriptors.Find("agy-cli")!;
        var runner = new FakeCliProcessRunner(
            @"C:\tools\agy.exe",
            [
                "{\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"response\":\"\",\"denied_actions\":[{\"action\":\"command\"}]}}"
            ]);

        var engine = new CliHarnessEngine(descriptor, runner, new EnvironmentScrubber());
        var request = new BackingEngineRequest(Guid.NewGuid(), Guid.NewGuid(), "agy-cli", "agy-default", "read_only", "Sys", [new BackingEngineMessage("user", "Inspect repo", [])]);

        var ex = await Assert.ThrowsAsync<BackingEngineException>(() =>
            engine.StreamTurnAsync(request, (_, _) => ValueTask.CompletedTask, CancellationToken.None));

        Assert.Equal("cli_permission_denied", ex.ErrorCode);
    }
}
