using Guildhall.AgentHost.Chat;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Providers;
using Guildhall.AgentHost.Scheduling;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Guildhall.AgentHost.Tests;

[Trait("Category", "SqlIntegration")]
public sealed class Phase6SqlIntegrationTests
{
    [Fact]
    public async Task Migration_And_RuntimeGrants_And_MissedRunRecovery_WorkEndToEnd()
    {
        var baseConn = Environment.GetEnvironmentVariable("GUILDHALL_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(baseConn))
            return;

        var dbName = $"GuildhallTest_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(baseConn) { InitialCatalog = "master" };
        await using (var master = new SqlConnection(builder.ConnectionString))
        {
            await master.OpenAsync();
            await using var cmd = master.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{dbName}];";
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            var testConn = new SqlConnectionStringBuilder(baseConn) { InitialCatalog = dbName }.ConnectionString;
            var options = new DbContextOptionsBuilder<GuildhallDbContext>().UseSqlServer(testConn).Options;
            await using (var migrateDb = new GuildhallDbContext(options))
            {
                await migrateDb.Database.MigrateAsync();
            }

            var clock = new FakeClock(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
            var factory = new TestDbFactory(options);
            var preambleCache = new PreambleCacheService(new StaticMonitor<PreambleCacheOptions>(new PreambleCacheOptions()), clock, new StubEnv(), NullLogger<PreambleCacheService>.Instance);
            var contextBuilder = new ContextBuilder(new StaticMonitor<CrossTurnBudgetOptions>(new CrossTurnBudgetOptions()), new Lazy<IPreambleCacheService>(() => preambleCache));
            var chatRunner = new ChatRunner(factory, new BackingEngineRegistry([]), clock, NullLogger<ChatRunner>.Instance, new Lazy<IContextBuilder>(() => contextBuilder));
            var scheduler = new DurableTaskSchedulerService(factory, chatRunner, new Lazy<IContextBuilder>(() => contextBuilder), clock, NullLogger<DurableTaskSchedulerService>.Instance);

            var agentId = Guid.NewGuid();
            var scheduleId = Guid.NewGuid();
            await using (var seed = new GuildhallDbContext(options))
            {
                if (!await seed.GuildhallBehaviorSettings.AnyAsync(x => x.Id == 1))
                    seed.GuildhallBehaviorSettings.Add(new GuildhallBehaviorSettings { Id = 1, Instructions = "Be helpful.", Revision = 1 });
                seed.AgentDefinitions.Add(new AgentDefinition { Id = agentId, Name = "Maren", Role = "Steward", Accent = "#71465f", Instructions = "Stay concise.", DefaultModelId = "qwen3:4b", Revision = 1 });
                seed.ScheduledAutomations.Add(new ScheduledAutomation
                {
                    Id = scheduleId,
                    Name = "Quarter-hour check",
                    AgentDefinitionId = agentId,
                    Prompt = "Summarize status.",
                    CronExpression = "*/15 * * * *",
                    TimezoneId = "UTC",
                    MisfirePolicy = "RunOnce",
                    LastEvaluatedUtc = clock.GetUtcNow().UtcDateTime,
                    NextRunUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(15),
                    CreatedUtc = clock.GetUtcNow().UtcDateTime,
                    UpdatedUtc = clock.GetUtcNow().UtcDateTime,
                    Revision = 1,
                });
                await seed.SaveChangesAsync();
            }

            // Simulate 50-minute PC sleep gap (misses 10:15, 10:30, 10:45) -> RunOnce enqueues 1 CatchUp run
            clock.Advance(TimeSpan.FromMinutes(50));
            await scheduler.TickAsync(CancellationToken.None);

            await using var verify = new GuildhallDbContext(options);
            var runs = await verify.DurableTaskRuns.AsNoTracking().Where(x => x.ScheduledAutomationId == scheduleId).ToListAsync();
            Assert.Single(runs);
            Assert.Equal("CatchUp", runs[0].TriggerKind);
            Assert.NotNull(runs[0].ChatRunId);

            var updatedSchedule = await verify.ScheduledAutomations.AsNoTracking().SingleAsync(x => x.Id == scheduleId);
            Assert.Equal(new DateTime(2026, 11, 17, 11, 0, 0, DateTimeKind.Utc), updatedSchedule.NextRunUtc);
        }
        finally
        {
            await using var master = new SqlConnection(builder.ConnectionString);
            await master.OpenAsync();
            await using var cmd = master.CreateCommand();
            cmd.CommandText = $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}];";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private sealed class TestDbFactory(DbContextOptions<GuildhallDbContext> options) : IDbContextFactory<GuildhallDbContext>
    {
        public GuildhallDbContext CreateDbContext() => new(options);
    }

    private sealed class FakeClock(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class StubEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Guildhall.AgentHost.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
