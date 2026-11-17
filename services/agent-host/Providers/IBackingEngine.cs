using System.Text.Json;
using Guildhall.AgentHost.Mcp;

namespace Guildhall.AgentHost.Providers;

public static class BackingEngineKinds
{
    public const string Local = "local";
    public const string CloudApi = "cloud_api";
    public const string CliHarness = "cli_harness";
}

public static class CostSources
{
    public const string LocalOllama = "local_ollama";
    public const string MeasuredApi = "measured_api";
    public const string SubscriptionCli = "subscription_cli";
    public const string Unavailable = "unavailable";
}

public sealed record BackingEngineAttachment(
    string FileName,
    string ContentType,
    byte[] Content,
    string? ExtractedText,
    bool IsImage);

public sealed record BackingEngineMessage(
    string Role,
    string Text,
    IReadOnlyList<BackingEngineAttachment> Attachments);

public sealed record BackingEngineToolCall(
    string CallId,
    string QualifiedName,
    JsonElement Arguments);

public sealed record BackingEngineToolResult(
    string CallId,
    string QualifiedName,
    string Output,
    bool IsError);

public sealed record BackingEngineToolTurn(
    IReadOnlyList<BackingEngineToolCall> Calls,
    IReadOnlyList<BackingEngineToolResult> Results);

public sealed record BackingEngineRequest(
    Guid RunId,
    Guid ConversationId,
    string ProviderId,
    string ModelId,
    string? HarnessMode,
    string SystemPrompt,
    IReadOnlyList<BackingEngineMessage> Messages,
    string? WorkingDirectory = null,
    IReadOnlyList<McpToolDescriptor>? Tools = null,
    IReadOnlyList<BackingEngineToolTurn>? ToolTurns = null);

public sealed record BackingEngineUsage(
    int? InputTokens,
    int? OutputTokens,
    decimal? EstimatedCostUsd,
    string CostSource);

public sealed record BackingEngineChunk(
    string? DeltaText = null,
    string? ThinkingText = null,
    BackingEngineUsage? Usage = null,
    bool IsComplete = false,
    IReadOnlyList<BackingEngineToolCall>? ToolCalls = null);

public sealed class BackingEngineException(string errorCode, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}

public interface IBackingEngine
{
    string ProviderId { get; }
    string EngineKind { get; }
    Task StreamTurnAsync(
        BackingEngineRequest request,
        Func<BackingEngineChunk, CancellationToken, ValueTask> onChunk,
        CancellationToken cancellationToken);
}

public interface IBackingEngineRegistry
{
    IBackingEngine? Resolve(string providerId);
    IReadOnlyList<IBackingEngine> GetAll();
}
