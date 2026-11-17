using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Mcp.Providers;
using Guildhall.AgentHost.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Api;

public static class SqlMcpEndpoints
{
    public static IEndpointRouteBuilder MapSqlMcpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mcp/sql/profiles", async (
            IDbContextFactory<GuildhallDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var profiles = await db.SqlMcpProfiles.AsNoTracking()
                .OrderBy(x => x.Name)
                .ToListAsync(ct);

            return Results.Ok(profiles.Select(ToView).ToArray());
        });

        app.MapPost("/mcp/sql/profiles", async (
            CreateSqlMcpProfileRequest request,
            IDbContextFactory<GuildhallDbContext> dbFactory,
            ICredentialStore credentialStore,
            SqlReadOnlyGuard readOnlyGuard,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var name = request.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
                return Results.BadRequest(new ErrorResponse("invalid_profile_name", "Profile name is required (1–100 characters)."));

            var server = request.Server?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(server) || server.Length > 200)
                return Results.BadRequest(new ErrorResponse("invalid_server", "SQL Server host/instance is required."));

            var database = request.Database?.Trim() ?? string.Empty;
            if (string.Equals(database, "*", StringComparison.Ordinal))
                database = string.Empty;
            var dbCheck = readOnlyGuard.ValidateDatabaseTarget(database);
            if (!dbCheck.IsAllowed)
                return Results.BadRequest(new ErrorResponse("protected_database_blocked", dbCheck.Reason ?? "Invalid database target."));

            var authMode = string.Equals(request.AuthMode, "sql_login", StringComparison.OrdinalIgnoreCase)
                ? "sql_login"
                : "integrated";

            if (authMode == "sql_login" && (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password)))
                return Results.BadRequest(new ErrorResponse("invalid_credentials", "Username and password are required for SQL login mode."));

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            if (await db.SqlMcpProfiles.AnyAsync(x => x.Name == name, ct))
                return Results.Conflict(new ErrorResponse("profile_exists", $"SQL MCP profile '{name}' already exists."));

            var schemas = (request.AllowedSchemas ?? ["dbo"])
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (schemas.Length == 0) schemas = ["dbo"];

            var objects = (request.AllowedObjects ?? [])
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var entity = new SqlMcpProfile
            {
                Id = Guid.NewGuid(),
                Name = name,
                Server = server,
                Database = database,
                AuthMode = authMode,
                AllowedSchemasJson = JsonSerializer.Serialize(schemas),
                AllowedObjectsJson = JsonSerializer.Serialize(objects),
                MaxRows = Math.Clamp(request.MaxRows, 1, 500),
                CommandTimeoutSeconds = Math.Clamp(request.CommandTimeoutSeconds, 1, 30),
                IsEnabled = true,
                CreatedUtc = now,
                UpdatedUtc = now,
                Revision = 1,
            };

            if (authMode == "sql_login")
            {
                var secretPayload = JsonSerializer.Serialize(new
                {
                    username = request.Username!.Trim(),
                    password = request.Password!
                });
                await credentialStore.SetApiKeyAsync(SqlServerMcpProvider.GetCredentialKey(entity.Id), secretPayload, ct);
            }

            db.SqlMcpProfiles.Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/mcp/sql/profiles/{entity.Id}", ToView(entity));
        });

        app.MapDelete("/mcp/sql/profiles/{id:guid}", async (
            Guid id,
            IDbContextFactory<GuildhallDbContext> dbFactory,
            ICredentialStore credentialStore,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await db.SqlMcpProfiles.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (existing is null)
                return Results.NotFound(new ErrorResponse("unknown_profile", "SQL MCP profile not found."));

            db.SqlMcpProfiles.Remove(existing);
            await db.SaveChangesAsync(ct);
            await credentialStore.DeleteApiKeyAsync(SqlServerMcpProvider.GetCredentialKey(id), ct);
            return Results.NoContent();
        });

        app.MapPost("/mcp/sql/profiles/{id:guid}/probe", async (
            Guid id,
            IDbContextFactory<GuildhallDbContext> dbFactory,
            IEnumerable<IMcpToolProvider> providers,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var profile = await db.SqlMcpProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (profile is null)
                return Results.NotFound(new ErrorResponse("unknown_profile", "SQL MCP profile not found."));

            var sqlProvider = providers.OfType<SqlServerMcpProvider>().SingleOrDefault();
            if (sqlProvider is null)
                return Results.Problem("SqlServerMcpProvider is not registered.");

            var (healthy, detail) = await sqlProvider.ProbeProfileAsync(profile, ct);
            return Results.Ok(new SqlMcpProbeResponse(profile.Id, profile.Name, healthy, detail, timeProvider.GetUtcNow()));
        });

        return app;
    }

    private static SqlMcpProfileView ToView(SqlMcpProfile p) =>
        new(
            p.Id,
            p.Name,
            p.Server,
            p.Database,
            p.AuthMode,
            Deserialize(p.AllowedSchemasJson),
            Deserialize(p.AllowedObjectsJson),
            p.MaxRows,
            p.CommandTimeoutSeconds,
            p.IsEnabled,
            p.CreatedUtc,
            p.UpdatedUtc,
            p.Revision);

    private static IReadOnlyList<string> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
