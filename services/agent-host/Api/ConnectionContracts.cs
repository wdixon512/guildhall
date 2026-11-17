namespace Guildhall.AgentHost.Api;

public sealed record ConnectionModelOption(string Id, string DisplayName, bool SupportsVision);

public sealed record ConnectionEntryResponse(
    string Id,
    string Pillar,
    string EngineKind,
    string DisplayName,
    string AuthType,
    string Status,
    string? StatusDetail,
    string? KeyHint,
    string? DefaultModelId,
    IReadOnlyList<ConnectionModelOption> Models,
    bool CanConfigureApiKey,
    bool CanProbe,
    bool IsPlanned);

public sealed record ConnectionsCatalogResponse(
    IReadOnlyList<ConnectionEntryResponse> Entries,
    DateTimeOffset CheckedUtc);

public sealed record SetApiKeyRequest(string ApiKey);
