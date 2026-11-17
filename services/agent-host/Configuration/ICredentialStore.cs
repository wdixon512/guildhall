namespace Guildhall.AgentHost.Configuration;

public sealed record CredentialStatus(bool HasKey, string? MaskedHint);

public interface ICredentialStore
{
    ValueTask<string?> GetApiKeyAsync(string providerId, CancellationToken ct = default);
    ValueTask SetApiKeyAsync(string providerId, string apiKey, CancellationToken ct = default);
    ValueTask DeleteApiKeyAsync(string providerId, CancellationToken ct = default);
    ValueTask<CredentialStatus> GetStatusAsync(string providerId, CancellationToken ct = default);
}
