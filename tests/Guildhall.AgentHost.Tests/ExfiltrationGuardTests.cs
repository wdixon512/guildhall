using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Tests;

public sealed class ExfiltrationGuardTests
{
    private readonly ExfiltrationGuard _guard = new();

    [Fact]
    public void RedactsApiKeysBearerTokensSqlPasswordsAndPrivateKeys()
    {
        var raw = """
            Authorization: Bearer secret-session-token-12345
            OpenAI: sk-proj-abcdefghijklmnopqrstuvwxyz1234
            Gemini: AIzaSyD-1234567890abcdefghijklmnop
            GitHub: ghp_1234567890abcdefghijklmnopqrstuvwxyz
            SQL: Server=localhost;Database=GuildhallDev;User Id=guildhall_runtime;Password=SuperSecretPassword123;
            GUILDHALL_SESSION_TOKEN=tok_abc123def456
            -----BEGIN RSA PRIVATE KEY-----
            MIIEowIBAAKCAQE...
            -----END RSA PRIVATE KEY-----
            """;

        var redacted = _guard.RedactSecrets(raw);

        Assert.DoesNotContain("secret-session-token-12345", redacted);
        Assert.DoesNotContain("sk-proj-abcdefghijklmnopqrstuvwxyz1234", redacted);
        Assert.DoesNotContain("AIzaSyD-1234567890abcdefghijklmnop", redacted);
        Assert.DoesNotContain("ghp_1234567890abcdefghijklmnopqrstuvwxyz", redacted);
        Assert.DoesNotContain("SuperSecretPassword123", redacted);
        Assert.DoesNotContain("tok_abc123def456", redacted);
        Assert.DoesNotContain("MIIEowIBAAKCAQE", redacted);
        Assert.Contains("[REDACTED]", redacted);
        Assert.Contains("[REDACTED_TOKEN]", redacted);
        Assert.Contains("[REDACTED_PRIVATE_KEY]", redacted);
    }

    [Fact]
    public void DetectsOutboundSecretsForExfiltrationBlocking()
    {
        Assert.True(_guard.ContainsSensitiveSecret("{\"body\":\"ghp_1234567890abcdefghijklmnopqrstuvwxyz\"}"));
        Assert.True(_guard.ContainsSensitiveSecret("Server=localhost;Password=MyPassword;"));
        Assert.False(_guard.ContainsSensitiveSecret("{\"path\":\"README.md\",\"maxCount\":10}"));
    }

    [Fact]
    public void TruncateAndRedactCapsLongOutputs()
    {
        var input = new string('a', 500) + " sk-proj-abcdefghijklmnopqrstuvwxyz1234";
        var result = _guard.TruncateAndRedact(input, 100);

        Assert.True(result.Length < 130);
        Assert.EndsWith("… [truncated]", result);
        Assert.DoesNotContain("sk-proj-abcdefghijklmnopqrstuvwxyz1234", result);
    }
}
