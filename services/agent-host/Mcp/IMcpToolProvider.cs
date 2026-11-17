namespace Guildhall.AgentHost.Mcp;

public interface IMcpToolProvider
{
    string ServerId { get; }
    string DisplayName { get; }
    string Transport { get; }
    string DefaultSecurityTier { get; }

    IReadOnlyList<McpToolDescriptor> GetTools();

    Task<(bool Healthy, string? Detail)> ProbeAsync(CancellationToken cancellationToken);

    Task<McpToolCallResult> InvokeAsync(McpToolCallRequest request, CancellationToken cancellationToken);
}
