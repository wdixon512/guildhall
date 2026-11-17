namespace Guildhall.AgentHost.Security;

public static class ToolSecurityTiers
{
    public const string Tier0 = "Tier0";
    public const string Tier1 = "Tier1";
    public const string Tier2 = "Tier2";
    public const string Tier3 = "Tier3";
}

public static class PermissionPolicies
{
    public const string AlwaysAsk = "always_ask";
    public const string AskUnsafe = "ask_unsafe";
    public const string Unrestricted = "unrestricted";

    public static string Normalize(string? value) =>
        string.Equals(value, AlwaysAsk, StringComparison.OrdinalIgnoreCase)
            ? AlwaysAsk
            : string.Equals(value, Unrestricted, StringComparison.OrdinalIgnoreCase)
                ? Unrestricted
                : AskUnsafe;

    public static string ResolveEffective(string? workspaceOverride, string? conversationOverride, string? memberPolicy)
    {
        if (!string.IsNullOrWhiteSpace(workspaceOverride))
            return Normalize(workspaceOverride);
        if (!string.IsNullOrWhiteSpace(conversationOverride))
            return Normalize(conversationOverride);
        return Normalize(memberPolicy);
    }
}

public enum GateOutcome
{
    Allow,
    RequireApproval,
    Deny,
}

public sealed record ToolGateDecision(
    GateOutcome Outcome,
    string? ErrorCode,
    string? Reason);

public sealed class ToolPermissionGate(ExfiltrationGuard exfiltrationGuard)
{
    public ToolGateDecision Evaluate(
        string? policy,
        IReadOnlySet<string> enabledServerIds,
        string serverId,
        string toolName,
        string securityTier,
        string? argumentsJson)
    {
        if (!enabledServerIds.Contains(serverId))
        {
            return new ToolGateDecision(
                GateOutcome.Deny,
                "tool_not_enabled",
                $"MCP server '{serverId}' is not enabled for this council member.");
        }

        var normalizedPolicy = PermissionPolicies.Normalize(policy);

        if (string.Equals(securityTier, ToolSecurityTiers.Tier2, StringComparison.OrdinalIgnoreCase))
        {
            if (exfiltrationGuard.ContainsSensitiveSecret(argumentsJson))
            {
                return new ToolGateDecision(
                    GateOutcome.Deny,
                    "exfiltration_blocked",
                    "Blocked workspace mutation: arguments contain a sensitive secret or credential.");
            }

            if (normalizedPolicy == PermissionPolicies.Unrestricted)
                return new ToolGateDecision(GateOutcome.Allow, null, null);

            return new ToolGateDecision(
                GateOutcome.RequireApproval,
                null,
                normalizedPolicy == PermissionPolicies.AlwaysAsk
                    ? $"Policy is set to Always ask: approve workspace tool '{toolName}' before execution."
                    : $"Tool '{toolName}' modifies the workspace (Tier 2) and requires human approval.");
        }

        if (string.Equals(securityTier, ToolSecurityTiers.Tier3, StringComparison.OrdinalIgnoreCase))
        {
            if (exfiltrationGuard.ContainsSensitiveSecret(argumentsJson))
            {
                return new ToolGateDecision(
                    GateOutcome.Deny,
                    "exfiltration_blocked",
                    "Blocked outbound tool call: arguments contain a sensitive secret or credential.");
            }

            // Tier 3 (external/remote side effects) always requires a human, even in Unrestricted:
            // Unrestricted is scoped to the local project/worktree, where rollback is possible.
            return new ToolGateDecision(
                GateOutcome.RequireApproval,
                null,
                $"Tool '{toolName}' is Tier 3 (external or mutating) and requires human approval.");
        }

        if (string.Equals(securityTier, ToolSecurityTiers.Tier0, StringComparison.OrdinalIgnoreCase))
        {
            return new ToolGateDecision(GateOutcome.Allow, null, null);
        }

        if (string.Equals(securityTier, ToolSecurityTiers.Tier1, StringComparison.OrdinalIgnoreCase))
        {
            if (normalizedPolicy == PermissionPolicies.AlwaysAsk)
            {
                return new ToolGateDecision(
                    GateOutcome.RequireApproval,
                    null,
                    $"Policy is set to Always ask: approve read-only tool '{toolName}' before execution.");
            }

            return new ToolGateDecision(GateOutcome.Allow, null, null);
        }

        // Fail-safe default for any unrecognized tier.
        return new ToolGateDecision(
            GateOutcome.RequireApproval,
            null,
            $"Tool '{toolName}' requires explicit approval.");
    }
}
