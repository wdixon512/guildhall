using System.Text.Json;

namespace Guildhall.AgentHost.Mcp;

public static class McpHealthStatuses
{
    public const string Connected = "Connected";
    public const string Degraded = "Degraded";
    public const string Failed = "Failed";
    public const string Disconnected = "Disconnected";
}

public sealed record McpToolDescriptor(
    string ServerId,
    string Name,
    string Description,
    string SecurityTier,
    JsonElement InputSchema)
{
    public string QualifiedName => $"{ServerId}__{Name}";
}

public sealed record McpToolCallRequest(
    string ServerId,
    string ToolName,
    JsonElement Arguments,
    string? WorkspaceRootPath);

public sealed record McpToolCallResult(
    bool IsError,
    string Content,
    string? ErrorCode = null);

public sealed record McpServerHealthState(
    string ServerId,
    string DisplayName,
    string Transport,
    string Status,
    int ConsecutiveFailures,
    DateTimeOffset? LastCheckedUtc,
    DateTimeOffset? NextRetryUtc,
    string? LastError);

public sealed record McpServerInfo(
    string ServerId,
    string DisplayName,
    string Transport,
    string DefaultSecurityTier,
    IReadOnlyList<McpToolDescriptor> Tools);
