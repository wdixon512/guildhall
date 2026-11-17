namespace Guildhall.AgentHost.Providers.Cli;

public sealed record CliProcessResult(int ExitCode, string StdOut, string StdErr);

public interface ICliProcessRunner
{
    string? ResolveExecutable(string commandName);

    void LaunchInteractiveLogin(string executablePath, IReadOnlyList<string> arguments);

    Task<CliProcessResult> RunBufferedAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken);

    Task<int> StreamJsonLinesAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        string? standardInput,
        Func<string, CancellationToken, ValueTask> onStdoutLine,
        Func<string, CancellationToken, ValueTask>? onStderrLine,
        CancellationToken cancellationToken);
}
