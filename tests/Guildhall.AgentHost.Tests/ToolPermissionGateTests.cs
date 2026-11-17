using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Tests;

public sealed class ToolPermissionGateTests
{
    private readonly ToolPermissionGate _gate = new(new ExfiltrationGuard());
    private static readonly HashSet<string> Enabled = ["filesystem-mcp", "git-mcp", "github-mcp"];

    [Fact]
    public void DeniesToolWhenServerIsNotEnabledForMember()
    {
        var decision = _gate.Evaluate(PermissionPolicies.AskUnsafe, Enabled, "gmail-mcp", "gmail_search_messages", ToolSecurityTiers.Tier1, "{}");

        Assert.Equal(GateOutcome.Deny, decision.Outcome);
        Assert.Equal("tool_not_enabled", decision.ErrorCode);
    }

    [Fact]
    public void Tier2WritesRequireApprovalUnderAskUnsafe()
    {
        var decision = _gate.Evaluate(PermissionPolicies.AskUnsafe, Enabled, "filesystem-mcp", "fs_write_file", ToolSecurityTiers.Tier2, "{}");

        Assert.Equal(GateOutcome.RequireApproval, decision.Outcome);
        Assert.Null(decision.ErrorCode);
    }

    [Fact]
    public void Tier1ReadOnlyRespectsAlwaysAskVsAskUnsafe()
    {
        var auto = _gate.Evaluate(PermissionPolicies.AskUnsafe, Enabled, "filesystem-mcp", "fs_read_file", ToolSecurityTiers.Tier1, "{}");
        Assert.Equal(GateOutcome.Allow, auto.Outcome);

        var prompt = _gate.Evaluate(PermissionPolicies.AlwaysAsk, Enabled, "filesystem-mcp", "fs_read_file", ToolSecurityTiers.Tier1, "{}");
        Assert.Equal(GateOutcome.RequireApproval, prompt.Outcome);
    }

    [Fact]
    public void Tier3AlwaysRequiresApprovalAndBlocksExfiltration()
    {
        var approval = _gate.Evaluate(PermissionPolicies.AskUnsafe, Enabled, "github-mcp", "github_create_issue", ToolSecurityTiers.Tier3, "{\"title\":\"Bug\"}");
        Assert.Equal(GateOutcome.RequireApproval, approval.Outcome);

        var blocked = _gate.Evaluate(PermissionPolicies.AskUnsafe, Enabled, "github-mcp", "github_create_issue", ToolSecurityTiers.Tier3, "{\"body\":\"ghp_1234567890abcdefghijklmnopqrstuvwxyz\"}");
        Assert.Equal(GateOutcome.Deny, blocked.Outcome);
        Assert.Equal("exfiltration_blocked", blocked.ErrorCode);
    }
}
