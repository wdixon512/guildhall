using Guildhall.AgentHost.Health;

namespace Guildhall.AgentHost.Api;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/tiers", (FiveTierHealthSupervisor supervisor) =>
            Results.Ok(supervisor.GetSnapshot()));

        app.MapPost("/health/ui-heartbeat", (FiveTierHealthSupervisor supervisor) =>
        {
            supervisor.RecordUiHeartbeat();
            return Results.NoContent();
        });

        app.MapPost("/health/tiers/{tierId}/recover", async (
            string tierId,
            FiveTierHealthSupervisor supervisor,
            CancellationToken ct) =>
        {
            var snapshot = await supervisor.RecoverTierAsync(tierId, ct);
            return Results.Ok(snapshot);
        });

        return app;
    }
}
