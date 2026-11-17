using System.Text.Json;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Mcp.Providers;
using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Api;

public static class McpGatewayEndpoints
{
    private static readonly HashSet<string> GatewayAllowedServerIds = new(StringComparer.OrdinalIgnoreCase)
    {
        SqlServerMcpProvider.ProviderId
    };

    public static IEndpointRouteBuilder MapMcpGatewayEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/mcp/gateway", async (
            JsonElement rpcRequest,
            McpRegistry registry,
            CancellationToken ct) =>
        {
            var id = rpcRequest.TryGetProperty("id", out var idEl) ? idEl.Clone() : JsonDocument.Parse("1").RootElement.Clone();
            var method = rpcRequest.TryGetProperty("method", out var mEl) ? mEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(method))
            {
                return Results.Ok(new
                {
                    jsonrpc = "2.0",
                    id,
                    error = new { code = -32600, message = "Invalid JSON-RPC request: missing method." }
                });
            }

            if (string.Equals(method, "initialize", StringComparison.Ordinal))
            {
                return Results.Ok(new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new
                    {
                        protocolVersion = "2025-03-26",
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = "guildhall-mcp-gateway", version = "1.0.0" }
                    }
                });
            }

            var servers = await registry.ListServersAsync(ct);
            var gatewayTools = servers
                .Where(s => GatewayAllowedServerIds.Contains(s.ServerId))
                .SelectMany(s => s.Tools)
                .Where(t => string.Equals(t.SecurityTier, ToolSecurityTiers.Tier1, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (string.Equals(method, "tools/list", StringComparison.Ordinal))
            {
                return Results.Ok(new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new
                    {
                        tools = gatewayTools.Select(t => new
                        {
                            name = t.Name,
                            description = t.Description,
                            inputSchema = t.InputSchema
                        })
                    }
                });
            }

            if (string.Equals(method, "tools/call", StringComparison.Ordinal))
            {
                if (!rpcRequest.TryGetProperty("params", out var paramsEl) || paramsEl.ValueKind != JsonValueKind.Object)
                {
                    return Results.Ok(new
                    {
                        jsonrpc = "2.0",
                        id,
                        error = new { code = -32602, message = "Missing params object." }
                    });
                }

                var toolName = paramsEl.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                var descriptor = gatewayTools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
                if (descriptor is null)
                {
                    return Results.Ok(new
                    {
                        jsonrpc = "2.0",
                        id,
                        error = new { code = -32601, message = $"Tool '{toolName}' is not exposed on the authenticated read-only MCP gateway." }
                    });
                }

                var args = paramsEl.TryGetProperty("arguments", out var aEl) && aEl.ValueKind == JsonValueKind.Object
                    ? aEl
                    : JsonDocument.Parse("{}").RootElement;

                var result = await registry.InvokeToolAsync(
                    new McpToolCallRequest(descriptor.ServerId, descriptor.Name, args, null),
                    ct);

                return Results.Ok(new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new
                    {
                        isError = result.IsError,
                        content = new[] { new { type = "text", text = result.Content } }
                    }
                });
            }

            return Results.Ok(new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = -32601, message = $"Method '{method}' is not supported by the Guildhall MCP gateway." }
            });
        });

        return app;
    }
}
