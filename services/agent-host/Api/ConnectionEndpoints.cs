using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Providers;
using Guildhall.AgentHost.Providers.Cli;

namespace Guildhall.AgentHost.Api;

public static class ConnectionEndpoints
{
    private static readonly HashSet<string> CloudApiIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "openai-api",
        "gemini-api",
        "anthropic-api"
    };

    public static IEndpointRouteBuilder MapConnections(this IEndpointRouteBuilder app)
    {
        app.MapGet("/connections", async (bool? probe, ConnectionCatalogService catalog, CancellationToken ct) =>
            Results.Ok(await catalog.GetCatalogAsync(probe ?? false, ct)));

        app.MapPut("/connections/{id}/api-key", async (string id, SetApiKeyRequest request, ICredentialStore credentials, ConnectionCatalogService catalog, CancellationToken ct) =>
        {
            if (!CloudApiIds.Contains(id))
                return Results.NotFound(new ErrorResponse("unknown_provider", "Provider does not support API key configuration."));

            if (string.IsNullOrWhiteSpace(request.ApiKey) || request.ApiKey.Trim().Length < 8 || request.ApiKey.Length > 512)
                return Results.BadRequest(new ErrorResponse("invalid_api_key", "A valid API key is required."));

            await credentials.SetApiKeyAsync(id, request.ApiKey, ct);
            var entry = await catalog.ProbeAsync(id, ct);
            return Results.Ok(entry);
        });

        app.MapDelete("/connections/{id}/api-key", async (string id, ICredentialStore credentials, ConnectionCatalogService catalog, CancellationToken ct) =>
        {
            if (!CloudApiIds.Contains(id))
                return Results.NotFound(new ErrorResponse("unknown_provider", "Provider does not support API key configuration."));

            await credentials.DeleteApiKeyAsync(id, ct);
            var entry = await catalog.ProbeAsync(id, ct);
            return Results.Ok(entry);
        });

        app.MapPost("/connections/{id}/probe", async (string id, ConnectionCatalogService catalog, CancellationToken ct) =>
        {
            var entry = await catalog.ProbeAsync(id, ct);
            return entry is null
                ? Results.NotFound(new ErrorResponse("unknown_connection", "Connection not found."))
                : Results.Ok(entry);
        });

        app.MapPost("/connections/{id}/sign-in", (string id, ICliProcessRunner cliProcessRunner) =>
        {
            var descriptor = CliHarnessDescriptors.Find(id);
            if (descriptor is null)
                return Results.NotFound(new ErrorResponse("unknown_connection", "CLI harness not found."));

            var executable = cliProcessRunner.ResolveExecutable(descriptor.ExecutableName);
            if (executable is null)
                return Results.Conflict(new ErrorResponse("cli_not_installed", $"{descriptor.DisplayName} is not installed on PATH."));

            cliProcessRunner.LaunchInteractiveLogin(executable, descriptor.LoginArgs);
            return Results.Accepted($"/connections/{id}", new { launched = true, message = descriptor.SignInHint });
        });

        return app;
    }
}
