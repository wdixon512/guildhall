using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Guildhall.AgentHost.Tests;

public sealed class ApiFactory(string? token = ApiFactory.Token, string readiness = "ready") : WebApplicationFactory<Program>
{
    public const string Token = "deterministic-test-token-with-at-least-32-characters";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GUILDHALL_SESSION_TOKEN"] = token,
            ["GUILDHALL_HOST_URL"] = "http://127.0.0.1:43120",
            ["ConnectionStrings:Guildhall"] = null
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDatabaseReadiness>();
            services.AddSingleton<IDatabaseReadiness>(new FakeReadiness(readiness));
        });
    }
    public HttpClient AuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:43120") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }
    private sealed class FakeReadiness(string status) : IDatabaseReadiness
    {
        public Task<ReadinessResponse> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(status == "ready" ? new ReadinessResponse("ready") : new("unavailable", status));
    }
}

public sealed class HealthAndLocalAuthTests
{
    [Fact]
    public async Task HealthReturnsVersionWithoutExternalDependencies()
    {
        await using var app = new ApiFactory();
        using var client = app.AuthenticatedClient();
        var result = await client.GetFromJsonAsync<HealthResponse>("/health");
        Assert.Equal(new HealthResponse(), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-token")]
    public async Task LocalAuthRejectsMissingOrWrongToken(string? token)
    {
        await using var app = new ApiFactory();
        using var client = app.AuthenticatedClient();
        client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        foreach (var endpoint in new[] { "/health", "/ready", "/models" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(endpoint)).StatusCode);
    }

    [Fact]
    public void LocalAuthRefusesMissingStartupToken()
    {
        using var app = new ApiFactory(null);
        Assert.ThrowsAny<InvalidOperationException>(() => app.CreateClient());
    }

    [Fact]
    public async Task LocalAuthRejectsUnrelatedOriginsEvenWithValidToken()
    {
        await using var app = new ApiFactory();
        using var client = app.AuthenticatedClient();
        client.DefaultRequestHeaders.Add("Origin", "https://unrelated.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task LocalAuthAllowsOnlyApprovedPreflightMethodsWithoutToken()
    {
        await using var app = new ApiFactory();
        using var client = app.AuthenticatedClient();
        client.DefaultRequestHeaders.Authorization = null;
        foreach (var method in new[] { "GET", "POST", "PATCH", "DELETE" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/conversations/example");
            request.Headers.Add("Origin", "http://127.0.0.1:1420");
            request.Headers.Add("Access-Control-Request-Method", method);
            request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("http://127.0.0.1:1420", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.Contains(method, response.Headers.GetValues("Access-Control-Allow-Methods").Single(), StringComparison.OrdinalIgnoreCase);
        }
        using var forbidden = new HttpRequestMessage(HttpMethod.Options, "/conversations/example");
        forbidden.Headers.Add("Origin", "http://127.0.0.1:1420");
        forbidden.Headers.Add("Access-Control-Request-Method", "PUT");
        var forbiddenResponse = await client.SendAsync(forbidden);
        Assert.Equal(HttpStatusCode.NoContent, forbiddenResponse.StatusCode);
        var allowedMethods = string.Join(',', forbiddenResponse.Headers.GetValues("Access-Control-Allow-Methods"));
        Assert.DoesNotContain("PUT", allowedMethods, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task LocalAuthRejectsUnexpectedHost()
    {
        await using var app = new ApiFactory();
        using var client = app.AuthenticatedClient();
        client.DefaultRequestHeaders.Host = "attacker.example:43120";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/health")).StatusCode);
    }

    [Theory]
    [InlineData("https://127.0.0.1:43120")]
    [InlineData("http://example.com")]
    [InlineData("http://127.0.0.1/path")]
    [InlineData("http://localhost:43120")]
    public void LocalAuthRejectsNonNumericLoopbackConfiguration(string value) =>
        Assert.Throws<InvalidOperationException>(() => LocalConfiguration.LoopbackUri(value));
}

public sealed class ReadinessTests
{
    [Theory]
    [InlineData("ready", 200)]
    [InlineData("migration_required", 503)]
    [InlineData("database_unavailable", 503)]
    public async Task ReadinessDoesNotChangeLiveness(string state, int status)
    {
        await using var app = new ApiFactory(readiness: state);
        using var client = app.AuthenticatedClient();
        Assert.Equal(status, (int)(await client.GetAsync("/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task ReadinessHandlesMissingDatabaseConfiguration() =>
        Assert.Equal("database_not_configured", (await new DatabaseReadiness(null).CheckAsync(default)).Reason);
}
