using System.Text.RegularExpressions;

namespace Guildhall.AgentHost.Security;

public sealed partial class ExfiltrationGuard
{
    [GeneratedRegex(@"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z0-9 ]*PRIVATE KEY-----", RegexOptions.Compiled)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(@"\b(sk-ant-[A-Za-z0-9_-]{16,}|sk-[A-Za-z0-9_-]{20,}|AIza[A-Za-z0-9_-]{20,}|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|gho_[A-Za-z0-9]{20,}|ya29\.[A-Za-z0-9\-._]{16,})\b", RegexOptions.Compiled)]
    private static partial Regex ProviderTokenRegex();

    [GeneratedRegex(@"(?i)(Bearer\s+)[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)((?:Password|Pwd)\s*=\s*)[^;""'\r\n]+", RegexOptions.Compiled)]
    private static partial Regex SqlPasswordRegex();

    [GeneratedRegex(@"(?i)(GUILDHALL_SESSION_TOKEN\s*[=:]\s*)[^\s""';]+", RegexOptions.Compiled)]
    private static partial Regex SessionTokenRegex();

    public string RedactSecrets(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var redacted = PrivateKeyRegex().Replace(input, "[REDACTED_PRIVATE_KEY]");
        redacted = ProviderTokenRegex().Replace(redacted, "[REDACTED_TOKEN]");
        redacted = BearerTokenRegex().Replace(redacted, "$1[REDACTED]");
        redacted = SqlPasswordRegex().Replace(redacted, "$1[REDACTED]");
        redacted = SessionTokenRegex().Replace(redacted, "$1[REDACTED]");
        return redacted;
    }

    public bool ContainsSensitiveSecret(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        return PrivateKeyRegex().IsMatch(input) ||
               ProviderTokenRegex().IsMatch(input) ||
               SessionTokenRegex().IsMatch(input) ||
               (input.Contains("Server=", StringComparison.OrdinalIgnoreCase) && SqlPasswordRegex().IsMatch(input));
    }

    public string TruncateAndRedact(string? input, int maxChars = 16_000)
    {
        var redacted = RedactSecrets(input);
        if (redacted.Length <= maxChars) return redacted;
        return redacted[..maxChars] + "\n… [truncated]";
    }
}
