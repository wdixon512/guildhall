using System.Text.Json;

namespace Guildhall.AgentHost.Api;

public sealed record McpToolView(
    string ServerId,
    string Name,
    string QualifiedName,
    string Description,
    string SecurityTier,
    JsonElement InputSchema);

public sealed record McpServerHealthView(
    string ServerId,
    string DisplayName,
    string Transport,
    string Status,
    int ConsecutiveFailures,
    DateTimeOffset? LastCheckedUtc,
    DateTimeOffset? NextRetryUtc,
    string? LastError,
    IReadOnlyList<McpToolView> Tools);

public sealed record McpCatalogResponse(
    IReadOnlyList<McpServerHealthView> Servers,
    DateTimeOffset CheckedUtc);

public sealed record CreateMcpServerRequest(
    string Id,
    string DisplayName,
    string Transport,
    string? Command,
    IReadOnlyList<string>? Arguments,
    string? EndpointUrl,
    string SecurityTier = "Tier3",
    bool AllowPrivateTunnelHttp = false);

public sealed record SetMcpTokenRequest(string Token);

public sealed record CustomMcpServerView(
    string Id,
    string DisplayName,
    string Transport,
    string? EndpointUrl,
    bool AllowPrivateTunnelHttp,
    string SecurityTier,
    bool IsEnabled,
    bool HasToken,
    long Revision,
    int ToolCount = 0,
    string HealthStatus = "Disconnected");
