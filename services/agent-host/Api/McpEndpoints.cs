using System.Text.Json;
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

            if (request.Transport == "streamable_http" &&
                (!Uri.TryCreate(request.EndpointUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)))
                return Results.BadRequest(new ErrorResponse("invalid_endpoint", "EndpointUrl must be HTTPS or loopback HTTP."));

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
                SecurityTier = tier,
                IsEnabled = true,
                CreatedUtc = now,
                UpdatedUtc = now,
                Revision = 1,
            };

            db.McpServerRegistrations.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/mcp/servers/{entity.Id}", entity);
        });

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
