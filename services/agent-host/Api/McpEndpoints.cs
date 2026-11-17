using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Security;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Api;

public static class McpEndpoints
{
    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mcp/tools", async (
            McpRegistry registry,
            McpHealthSupervisor healthSupervisor,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var servers = await registry.ListServersAsync(ct);
            var views = servers.Select(server =>
            {
                var health = healthSupervisor.Get(server.ServerId);
                var tools = server.Tools.Select(t => new McpToolView(
                    t.ServerId,
                    t.Name,
                    t.QualifiedName,
                    t.Description,
                    t.SecurityTier,
                    t.InputSchema)).ToArray();

                return new McpServerHealthView(
                    server.ServerId,
                    server.DisplayName,
                    server.Transport,
                    health?.Status ?? McpHealthStatuses.Connected,
                    health?.ConsecutiveFailures ?? 0,
                    health?.LastCheckedUtc,
                    health?.NextRetryUtc,
                    health?.LastError,
                    tools);
            }).ToArray();

            return Results.Ok(new McpCatalogResponse(views, timeProvider.GetUtcNow()));
        });

        app.MapPost("/mcp/servers", async (
            CreateMcpServerRequest request,
            IDbContextFactory<GuildhallDbContext> dbFactory,
            PrivateNetworkGuard privateNetworkGuard,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var id = request.Id?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id) || id.Length > 80 || id.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                return Results.BadRequest(new ErrorResponse("invalid_server_id", "Server ID must be alphanumeric with dashes/underscores."));

            if (string.IsNullOrWhiteSpace(request.DisplayName))
                return Results.BadRequest(new ErrorResponse("invalid_display_name", "Display name is required."));

            if (request.Transport is not ("stdio" or "streamable_http"))
                return Results.BadRequest(new ErrorResponse("invalid_transport", "Transport must be 'stdio' or 'streamable_http'."));

            if (request.Transport == "stdio" && string.IsNullOrWhiteSpace(request.Command))
                return Results.BadRequest(new ErrorResponse("invalid_command", "Command is required for stdio MCP servers."));

            if (request.Transport == "streamable_http")
            {
                if (!Uri.TryCreate(request.EndpointUrl, UriKind.Absolute, out var uri))
                    return Results.BadRequest(new ErrorResponse("invalid_endpoint", "EndpointUrl must be a valid absolute URI."));

                var validation = privateNetworkGuard.ValidateMcpEndpoint(uri, request.AllowPrivateTunnelHttp);
                if (!validation.IsAllowed)
                    return Results.BadRequest(new ErrorResponse("invalid_endpoint", validation.Reason ?? "EndpointUrl is not allowed."));
            }

            var tier = request.SecurityTier is ToolSecurityTiers.Tier1 or ToolSecurityTiers.Tier3
                ? request.SecurityTier
                : ToolSecurityTiers.Tier3;

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            if (await db.McpServerRegistrations.AnyAsync(x => x.Id == id, ct))
                return Results.Conflict(new ErrorResponse("server_exists", $"MCP server '{id}' already exists."));

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var entity = new McpServerRegistration
            {
                Id = id,
                DisplayName = request.DisplayName.Trim(),
                Transport = request.Transport,
                Command = request.Command?.Trim(),
                ArgumentsJson = JsonSerializer.Serialize(request.Arguments ?? []),
                EndpointUrl = request.EndpointUrl?.Trim(),
                AllowPrivateTunnelHttp = request.AllowPrivateTunnelHttp,
                SecurityTier = tier,
                IsEnabled = true,
                CreatedUtc = now,
                UpdatedUtc = now,
                Revision = 1,
            };

            db.McpServerRegistrations.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/mcp/servers/{entity.Id}", new CustomMcpServerView(
                entity.Id,
                entity.DisplayName,
                entity.Transport,
                entity.EndpointUrl,
                entity.AllowPrivateTunnelHttp,
                entity.SecurityTier,
                entity.IsEnabled,
                false,
                entity.Revision,
                0,
                McpHealthStatuses.Disconnected));
        });

        app.MapGet("/mcp/servers/custom", async (
            IDbContextFactory<GuildhallDbContext> dbFactory,
            ICredentialStore credentialStore,
            McpRegistry registry,
            McpHealthSupervisor healthSupervisor,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var list = await db.McpServerRegistrations.AsNoTracking()
                .OrderBy(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.DisplayName,
                    x.Transport,
                    x.EndpointUrl,
                    x.AllowPrivateTunnelHttp,
                    x.SecurityTier,
                    x.IsEnabled,
                    x.Revision
                })
                .ToListAsync(ct);

            var views = new List<CustomMcpServerView>(list.Count);
            foreach (var reg in list)
            {
                var status = await credentialStore.GetStatusAsync($"mcp:{reg.Id}", ct);
                var health = healthSupervisor.Get(reg.Id);
                views.Add(new CustomMcpServerView(
                    reg.Id,
                    reg.DisplayName,
                    reg.Transport,
                    reg.EndpointUrl,
                    reg.AllowPrivateTunnelHttp,
                    reg.SecurityTier,
                    reg.IsEnabled,
                    status.HasKey,
                    reg.Revision,
                    registry.GetCachedToolCount(reg.Id),
                    health?.Status ?? McpHealthStatuses.Disconnected));
            }
            return Results.Ok(views);
        });

        app.MapPut("/mcp/servers/{id}/token", async (
            string id,
            SetMcpTokenRequest request,
            IDbContextFactory<GuildhallDbContext> dbFactory,
            ICredentialStore credentialStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Trim().Length < 8 || request.Token.Length > 1024)
                return Results.BadRequest(new ErrorResponse("invalid_token", "Bearer token must be 8–1024 characters."));

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var exists = await db.McpServerRegistrations.AsNoTracking().AnyAsync(x => x.Id == id, ct);
            if (!exists)
                return Results.NotFound(new ErrorResponse("unknown_server", "MCP server not found."));

            await credentialStore.SetApiKeyAsync($"mcp:{id}", request.Token.Trim(), ct);
            return Results.NoContent();
        });

        app.MapDelete("/mcp/servers/{id}/token", async (
            string id,
            ICredentialStore credentialStore,
            CancellationToken ct) =>
        {
            await credentialStore.DeleteApiKeyAsync($"mcp:{id}", ct);
            return Results.NoContent();
        });

        app.MapGet("/infrastructure/network-status", (PrivateNetworkProbeService probeService) =>
            Results.Ok(probeService.Inspect()));

        app.MapDelete("/mcp/servers/{id}", async (
            string id,
            long? expectedRevision,
            IDbContextFactory<GuildhallDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await db.McpServerRegistrations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (existing is null)
                return Results.NotFound(new ErrorResponse("unknown_server", "MCP server not found."));

            if (expectedRevision.HasValue && existing.Revision != expectedRevision.Value)
                return Results.Conflict(new ErrorResponse("revision_conflict", "MCP server was modified elsewhere."));

            db.McpServerRegistrations.Remove(existing);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        app.MapPost("/mcp/servers/{id}/probe", async (
            string id,
            McpRegistry registry,
            CancellationToken ct) =>
        {
            var state = await registry.ProbeServerAsync(id, ct);
            return state is null
                ? Results.NotFound(new ErrorResponse("unknown_server", "MCP server not found."))
                : Results.Ok(state);
        });

        return app;
    }
}
