using System.Net;
using System.Text;
using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Chat;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Health;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Providers;
using Guildhall.AgentHost.Providers.Ollama;
using Guildhall.AgentHost.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Guildhall.AgentHost.Tests;

public sealed class Phase6SchedulerAndHealthTests
{
    [Theory]
    [InlineData("@hourly", true)]
    [InlineData("@daily", true)]
    [InlineData("@weekly", true)]
    [InlineData("@every_15m", true)]
    [InlineData("*/10 9-17 * * 1-5", true)]
    [InlineData("0 0 1,15 * *", true)]
    [InlineData("bad cron", false)]
    [InlineData("65 * * * *", false)]
    public void CronScheduleParser_ValidatesExpressions(string expr, bool expectedValid)
    {
        var ok = CronScheduleParser.TryParse(expr, out var schedule, out var error);
        Assert.Equal(expectedValid, ok);
        if (expectedValid)
        {
            Assert.NotNull(schedule);
            Assert.Null(error);
        }
        else
        {
            Assert.Null(schedule);
            Assert.NotNull(error);
        }
    }

    [Fact]
    public void CronSchedule_EnumeratesMissedSlotsAcrossSleepGap()
    {
        Assert.True(CronScheduleParser.TryParse("*/15 * * * *", out var schedule, out _));
        var from = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 17, 10, 50, 0, DateTimeKind.Utc);

        var slots = schedule!.EnumerateOccurrencesUtc(from, to, TimeZoneInfo.Utc, 10);
        Assert.Equal(3, slots.Count); // 10:15, 10:30, 10:45
        Assert.Equal(new DateTime(2026, 11, 17, 10, 15, 0, DateTimeKind.Utc), slots[0]);
        Assert.Equal(new DateTime(2026, 11, 17, 10, 45, 0, DateTimeKind.Utc), slots[2]);
    }

    [Fact]
    public async Task FiveTierHealthSupervisor_TracksUiHeartbeat_AndRecoversDegradedTiers()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
        var readiness = new StubDatabaseReadiness { IsReady = false };
        var ollamaHttp = new HttpClient(new StubOllamaHandler { IsHealthy = false }) { BaseAddress = new Uri("http://127.0.0.1:11434") };
        var ollama = new OllamaDiscovery(ollamaHttp);
        var mcpHealth = new McpHealthSupervisor(clock);
        var mcpRegistry = new McpRegistry([], new StubDbFactory(), new StubJsonRpcClient(), mcpHealth, new Guildhall.AgentHost.Security.ExfiltrationGuard());
        var chatRunner = new ChatRunner(new StubDbFactory(), new BackingEngineRegistry([]), clock, NullLogger<ChatRunner>.Instance, new Lazy<IContextBuilder>(() => null!));
        var scheduler = new DurableTaskSchedulerService(new StubDbFactory(), chatRunner, new Lazy<IContextBuilder>(() => null!), clock, NullLogger<DurableTaskSchedulerService>.Instance);
        var config = new ConfigurationBuilder().Build();

        var supervisor = new FiveTierHealthSupervisor(
            new StubDbFactory(),
            readiness,
            ollama,
            mcpRegistry,
            mcpHealth,
            scheduler,
            clock,
            config,
            NullLogger<FiveTierHealthSupervisor>.Instance);

        var initial = supervisor.GetSnapshot();
        Assert.Equal("Idle", initial.Tiers.Single(t => t.TierId == "tauri-ui").Status);

        supervisor.RecordUiHeartbeat();
        await supervisor.ProbeAllAsync(force: true, CancellationToken.None);
        var degraded = supervisor.GetSnapshot();
        Assert.Equal("Healthy", degraded.Tiers.Single(t => t.TierId == "tauri-ui").Status);
        Assert.Equal("Degraded", degraded.OverallStatus);
        Assert.Equal("Unavailable", degraded.Tiers.Single(t => t.TierId == "sql-server").Status);
        Assert.Equal("Unavailable", degraded.Tiers.Single(t => t.TierId == "ollama").Status);

        readiness.IsReady = true;
        var recovered = await supervisor.RecoverTierAsync("sql-server", CancellationToken.None);
        Assert.Equal("Healthy", recovered.Tiers.Single(t => t.TierId == "sql-server").Status);
    }

    private sealed class FakeTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private sealed class StubDatabaseReadiness : IDatabaseReadiness
    {
        public bool IsReady { get; set; }
        public Task<ReadinessResponse> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(IsReady
                ? new ReadinessResponse("ready", null)
                : new ReadinessResponse("unavailable", "database_unavailable"));
    }

    private sealed class StubOllamaHandler : HttpMessageHandler
    {
        public bool IsHealthy { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!IsHealthy)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            var json = request.RequestUri?.AbsolutePath.Contains("ps") == true
                ? "{\"models\":[]}"
                : "{\"models\":[{\"name\":\"qwen3:4b\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubDbFactory : IDbContextFactory<GuildhallDbContext>
    {
        public GuildhallDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<GuildhallDbContext>().UseSqlServer("Server=localhost;Database=InvalidTestDb;Integrated Security=true;").Options);
    }

    private sealed class StubCredentialStore : ICredentialStore
    {
        public ValueTask<string?> GetApiKeyAsync(string providerId, CancellationToken ct = default) => ValueTask.FromResult<string?>(null);
        public ValueTask SetApiKeyAsync(string providerId, string apiKey, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask DeleteApiKeyAsync(string providerId, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<CredentialStatus> GetStatusAsync(string providerId, CancellationToken ct = default) =>
            ValueTask.FromResult(new CredentialStatus(false, null));
    }

    private sealed class StubJsonRpcClient : IMcpJsonRpcClient
    {
        public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(McpServerRegistration registration, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<McpToolDescriptor>>([]);
        public Task<McpToolCallResult> CallToolAsync(McpServerRegistration registration, string toolName, System.Text.Json.JsonElement arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new McpToolCallResult(false, "stub", "stub"));
    }
}
