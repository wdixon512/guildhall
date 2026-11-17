using System.Text.Json;
using Guildhall.AgentHost.Configuration;

namespace Guildhall.AgentHost.Providers.Cli;

public sealed class AgyCliEngine(
    ICliProcessRunner processRunner,
    EnvironmentScrubber environmentScrubber)
    : CliHarnessEngineBase(CliHarnessDescriptors.Find("agy-cli")!, processRunner, environmentScrubber)
{
    protected override bool UsesInlinePrompt => true;

    protected override bool IsPermissionDeniedEvent(JsonElement root) =>
        root.TryGetProperty("event", out var evProp) &&
        evProp.GetString() == "result" &&
        root.TryGetProperty("result", out var resProp) &&
        resProp.ValueKind == JsonValueKind.Object &&
        resProp.TryGetProperty("denied_actions", out var deniedProp) &&
        deniedProp.ValueKind == JsonValueKind.Array &&
        deniedProp.GetArrayLength() > 0;
}

public sealed class CodexCliEngine(
    ICliProcessRunner processRunner,
    EnvironmentScrubber environmentScrubber)
    : CliHarnessEngineBase(CliHarnessDescriptors.Find("codex-cli")!, processRunner, environmentScrubber);

public sealed class ClaudeCliEngine(
    ICliProcessRunner processRunner,
    EnvironmentScrubber environmentScrubber)
    : CliHarnessEngineBase(CliHarnessDescriptors.Find("claude-cli")!, processRunner, environmentScrubber);

public sealed class CursorCliEngine(
    ICliProcessRunner processRunner,
    EnvironmentScrubber environmentScrubber)
    : CliHarnessEngineBase(CliHarnessDescriptors.Find("cursor-cli")!, processRunner, environmentScrubber);
