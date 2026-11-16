using Guildhall.AgentHost.Api;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Persistence;

public interface IDatabaseReadiness
{
    Task<ReadinessResponse> CheckAsync(CancellationToken cancellationToken);
}

public sealed class DatabaseReadiness(string? connection) : IDatabaseReadiness
{
    public async Task<ReadinessResponse> CheckAsync(CancellationToken cancellationToken)
    {
        if (connection is null) return new("unavailable", "database_not_configured");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            await using var db = new GuildhallDbContext(new DbContextOptionsBuilder<GuildhallDbContext>()
                .UseSqlServer(connection, sql => sql.CommandTimeout(3)).Options);
            if (!await db.Database.CanConnectAsync(deadline.Token)) return new("unavailable", "database_unavailable");
            var applied = (await db.Database.GetAppliedMigrationsAsync(deadline.Token)).ToArray();
            var expected = db.Database.GetMigrations().ToArray();
            if (expected.Length == 0 || !applied.SequenceEqual(expected)) return new("unavailable", "migration_required");
            if (!await db.ApplicationMetadata.AnyAsync(x => x.Id == 1, deadline.Token)) return new("unavailable", "migration_required");
            return new("ready");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is Microsoft.Data.SqlClient.SqlException or OperationCanceledException or InvalidOperationException)
        { return new("unavailable", "database_unavailable"); }
    }
}
