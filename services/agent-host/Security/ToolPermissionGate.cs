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

    public static string Normalize(string? value) =>
        string.Equals(value, AlwaysAsk, StringComparison.OrdinalIgnoreCase)
            ? AlwaysAsk
            : AskUnsafe;
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

        if (string.Equals(securityTier, ToolSecurityTiers.Tier2, StringComparison.OrdinalIgnoreCase))
        {
            return new ToolGateDecision(
                GateOutcome.Deny,
                "tier2_planned_phase4",
                $"Tool '{toolName}' requires Tier 2 workspace write/worktree permissions (planned for Phase 4).");
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
            var normalizedPolicy = PermissionPolicies.Normalize(policy);
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
