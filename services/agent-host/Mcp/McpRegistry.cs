using System.Collections.Concurrent;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Security;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Mcp;

public sealed class McpRegistry(
    IEnumerable<IMcpToolProvider> builtInProviders,
    IDbContextFactory<GuildhallDbContext> dbFactory,
    IMcpJsonRpcClient jsonRpcClient,
    McpHealthSupervisor healthSupervisor,
    ExfiltrationGuard exfiltrationGuard)
{
    private readonly Dictionary<string, IMcpToolProvider> _builtIns = builtInProviders
        .ToDictionary(x => x.ServerId, StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, IReadOnlyList<McpToolDescriptor>> _externalToolsCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<McpServerInfo>> ListServersAsync(CancellationToken cancellationToken)
    {
        var servers = new List<McpServerInfo>();

        foreach (var provider in _builtIns.Values.OrderBy(x => x.ServerId, StringComparer.OrdinalIgnoreCase))
        {
            healthSupervisor.GetOrCreate(provider.ServerId, provider.DisplayName, provider.Transport);
            servers.Add(new McpServerInfo(
                provider.ServerId,
                provider.DisplayName,
                provider.Transport,
                provider.DefaultSecurityTier,
                provider.GetTools()));
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var custom = await db.McpServerRegistrations.AsNoTracking()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var reg in custom)
        {
            healthSupervisor.GetOrCreate(reg.Id, reg.DisplayName, reg.Transport, McpHealthStatuses.Disconnected);
            var tools = _externalToolsCache.GetValueOrDefault(reg.Id) ?? [];
            servers.Add(new McpServerInfo(reg.Id, reg.DisplayName, reg.Transport, reg.SecurityTier, tools));
        }

        return servers;
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> GetEnabledToolsForMemberAsync(
        IReadOnlySet<string> enabledServerIds,
        CancellationToken cancellationToken)
    {
        if (enabledServerIds.Count == 0) return [];

        var servers = await ListServersAsync(cancellationToken);
        var tools = new List<McpToolDescriptor>();
        foreach (var server in servers)
        {
            if (!enabledServerIds.Contains(server.ServerId)) continue;
            if (!healthSupervisor.CanAttempt(server.ServerId)) continue;
            tools.AddRange(server.Tools);
        }
        return tools;
    }

    public async Task<McpServerHealthState?> ProbeServerAsync(string serverId, CancellationToken cancellationToken)
    {
        if (_builtIns.TryGetValue(serverId, out var builtIn))
        {
            healthSupervisor.ResetForProbe(builtIn.ServerId, builtIn.DisplayName, builtIn.Transport);
            try
            {
                var (healthy, detail) = await builtIn.ProbeAsync(cancellationToken);
                return healthy
                    ? healthSupervisor.RecordSuccess(builtIn.ServerId, builtIn.DisplayName, builtIn.Transport)
                    : healthSupervisor.MarkDisconnected(builtIn.ServerId, builtIn.DisplayName, builtIn.Transport, detail);
            }
            catch (Exception ex)
            {
                return healthSupervisor.RecordFailure(builtIn.ServerId, builtIn.DisplayName, builtIn.Transport, ex.Message);
            }
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var reg = await db.McpServerRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == serverId, cancellationToken);
        if (reg is null) return null;

        healthSupervisor.ResetForProbe(reg.Id, reg.DisplayName, reg.Transport);
        try
        {
            var discovered = await jsonRpcClient.ListToolsAsync(reg, cancellationToken);
            _externalToolsCache[reg.Id] = discovered;
            return healthSupervisor.RecordSuccess(reg.Id, reg.DisplayName, reg.Transport);
        }
        catch (Exception ex)
        {
            return healthSupervisor.RecordFailure(reg.Id, reg.DisplayName, reg.Transport, ex.Message);
        }
    }

    public async Task<McpToolCallResult> InvokeToolAsync(McpToolCallRequest request, CancellationToken cancellationToken)
    {
        if (_builtIns.TryGetValue(request.ServerId, out var builtIn))
        {
            if (!healthSupervisor.CanAttempt(builtIn.ServerId))
                return new McpToolCallResult(true, $"MCP server '{builtIn.DisplayName}' is currently unavailable (Tier 5 circuit open).", "mcp_server_unavailable");

            try
            {
                var result = await builtIn.InvokeAsync(request, cancellationToken);
                healthSupervisor.RecordSuccess(builtIn.ServerId, builtIn.DisplayName, builtIn.Transport);
                return result with { Content = exfiltrationGuard.TruncateAndRedact(result.Content) };
            }
            catch (Exception ex)
            {
                healthSupervisor.RecordFailure(builtIn.ServerId, builtIn.DisplayName, builtIn.Transport, ex.Message);
                return new McpToolCallResult(true, exfiltrationGuard.RedactSecrets(ex.Message), "mcp_tool_failed");
            }
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var reg = await db.McpServerRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ServerId && x.IsEnabled, cancellationToken);
        if (reg is null)
            return new McpToolCallResult(true, $"MCP server '{request.ServerId}' is not registered or enabled.", "mcp_server_not_found");

        if (!healthSupervisor.CanAttempt(reg.Id))
            return new McpToolCallResult(true, $"MCP server '{reg.DisplayName}' is currently unavailable (Tier 5 circuit open).", "mcp_server_unavailable");

        try
        {
            var result = await jsonRpcClient.CallToolAsync(reg, request.ToolName, request.Arguments, cancellationToken);
            healthSupervisor.RecordSuccess(reg.Id, reg.DisplayName, reg.Transport);
            return result with { Content = exfiltrationGuard.TruncateAndRedact(result.Content) };
        }
        catch (Exception ex)
        {
            healthSupervisor.RecordFailure(reg.Id, reg.DisplayName, reg.Transport, ex.Message);
            return new McpToolCallResult(true, exfiltrationGuard.RedactSecrets(ex.Message), "mcp_tool_failed");
        }
    }
}
