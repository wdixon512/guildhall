using System.Diagnostics;
using System.Text;

namespace Guildhall.AgentHost.Providers.Cli;

public sealed class CliProcessRunner : ICliProcessRunner
{
    private static readonly string[] WindowsExtensions = [".exe", ".cmd", ".bat", ""];

    public string? ResolveExecutable(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName)) return null;

        var extensions = OperatingSystem.IsWindows() ? WindowsExtensions : [""];
        var searchPaths = new List<string>();

        void AddPaths(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            foreach (var dir in raw.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!searchPaths.Contains(dir, StringComparer.OrdinalIgnoreCase))
                    searchPaths.Add(dir);
            }
        }

        AddPaths(Environment.GetEnvironmentVariable("PATH"));
        if (OperatingSystem.IsWindows())
        {
            AddPaths(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
            AddPaths(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
        }

        foreach (var dir in searchPaths)
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, commandName + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    public void LaunchInteractiveLogin(string executablePath, IReadOnlyList<string> arguments)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Interactive terminal sign-in is only supported on Windows.");

        var quotedExe = $"\"{executablePath}\"";
        var joinedArgs = string.Join(" ", arguments.Select(a => $"\"{a}\""));
        var innerCommand = string.IsNullOrEmpty(joinedArgs) ? quotedExe : $"{quotedExe} {joinedArgs}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"Guildhall CLI Sign-In\" cmd.exe /k {innerCommand}",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process.Start(startInfo);
    }

    public async Task<CliProcessResult> RunBufferedAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = CreateProcess(executablePath, arguments, workingDirectory, environment, redirectStdin: false);
        if (!process.Start())
            throw new BackingEngineException("cli_unavailable", $"Failed to start '{Path.GetFileName(executablePath)}'.");

        try
        {
            var outTask = ReadStreamAsync(process.StandardOutput, line =>
            {
                if (stdout.Length < 64 * 1024) stdout.AppendLine(line);
                return ValueTask.CompletedTask;
            }, cancellationToken);

            var errTask = ReadStreamAsync(process.StandardError, line =>
            {
                if (stderr.Length < 64 * 1024) stderr.AppendLine(line);
                return ValueTask.CompletedTask;
            }, cancellationToken);

            await Task.WhenAll(outTask, errTask, process.WaitForExitAsync(cancellationToken));
            return new CliProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch
        {
            TryKillTree(process);
            throw;
        }
    }

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
        using var process = CreateProcess(executablePath, arguments, workingDirectory, environment, redirectStdin: standardInput is not null);
        if (!process.Start())
            throw new BackingEngineException("cli_unavailable", $"Failed to start '{Path.GetFileName(executablePath)}'.");

        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
                process.StandardInput.Close();
            }

            var outTask = ReadStreamAsync(process.StandardOutput, line => onStdoutLine(line, cancellationToken), cancellationToken);
            var errTask = ReadStreamAsync(process.StandardError, line =>
                onStderrLine is not null ? onStderrLine(line, cancellationToken) : ValueTask.CompletedTask, cancellationToken);

            await Task.WhenAll(outTask, errTask, process.WaitForExitAsync(cancellationToken));
            return process.ExitCode;
        }
        catch
        {
            TryKillTree(process);
            throw;
        }
    }

    private static Process CreateProcess(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        bool redirectStdin)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = redirectStdin,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in arguments)
            startInfo.ArgumentList.Add(arg);

        startInfo.Environment.Clear();
        foreach (var (key, value) in environment)
        {
            if (value is not null)
                startInfo.Environment[key] = value;
        }

        return new Process { StartInfo = startInfo };
    }

    private static async Task ReadStreamAsync(
        StreamReader reader,
        Func<string, ValueTask> onLine,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            await onLine(line);
        }
    }

    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup on cancellation or timeout.
        }
    }
}
