namespace Guildhall.AgentHost.Api;

public sealed record SqlMcpProfileView(
    Guid Id,
    string Name,
    string Server,
    string Database,
    string AuthMode,
    IReadOnlyList<string> AllowedSchemas,
    IReadOnlyList<string> AllowedObjects,
    int MaxRows,
    int CommandTimeoutSeconds,
    bool IsEnabled,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    long Revision);

public sealed record CreateSqlMcpProfileRequest(
    string Name,
    string Server,
    string Database,
    string AuthMode = "integrated",
    string? Username = null,
    string? Password = null,
    IReadOnlyList<string>? AllowedSchemas = null,
    IReadOnlyList<string>? AllowedObjects = null,
    int MaxRows = 100,
    int CommandTimeoutSeconds = 10);

public sealed record SqlMcpProbeResponse(
    Guid ProfileId,
    string Name,
    bool Healthy,
    string? Detail,
    DateTimeOffset CheckedUtc);
