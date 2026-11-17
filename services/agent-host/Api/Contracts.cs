namespace Guildhall.AgentHost.Api;

public sealed record HealthResponse(string Status = "ok", int ApiVersion = 1);
public sealed record ReadinessResponse(string Status, string? Reason = null);
public sealed record ModelDescriptor(string Id, string DisplayName);
public sealed record ModelDiscoveryResponse(string ProviderId, string Status, IReadOnlyList<ModelDescriptor> Models);
public sealed record RunningModelDescriptor(string Name, long SizeBytes, long SizeVramBytes, DateTimeOffset? ExpiresAtUtc, int? ContextLength);
public sealed record VramResidencyResponse(string ProviderId, string Status, bool UseGpu, int KeepAliveSeconds, IReadOnlyList<RunningModelDescriptor> Models);
