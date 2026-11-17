using System.Text.Json;
using Guildhall.AgentHost.Chat;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Mcp.Providers;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Providers;
using Guildhall.AgentHost.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Guildhall.AgentHost.Tests;

[Trait("Category", "SqlIntegration")]
public sealed class ToolLoopChatRunnerTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "guildhall-tool-loop-" + Guid.NewGuid().ToString("N"));
    private readonly string? _masterConnection = Environment.GetEnvironmentVariable("GUILDHALL_TEST_SQL_CONNECTION");

    public ToolLoopChatRunnerTests()
    {
        Directory.CreateDirectory(_workspaceRoot);
        File.WriteAllText(Path.Combine(_workspaceRoot, "hello.txt"), "Hello from Phase 3 MCP!");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
            Directory.Delete(_workspaceRoot, recursive: true);
    }

    private sealed class Factory(DbContextOptions<GuildhallDbContext> options) : IDbContextFactory<GuildhallDbContext>
    {
        public GuildhallDbContext CreateDbContext() => new(options);
    }

    private sealed class StubEngine : IBackingEngine
    {
        public string ProviderId => "stub-tool-engine";
        public string EngineKind => BackingEngineKinds.Local;
        public int Calls { get; private set; }

        public async Task StreamTurnAsync(
            BackingEngineRequest request,
            Func<BackingEngineChunk, CancellationToken, ValueTask> onChunk,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1)
            {
                await onChunk(new BackingEngineChunk(
                    ToolCalls:
                    [
                        new BackingEngineToolCall(
                            "call-1",
                            "filesystem-mcp__fs_read_file",
                            JsonDocument.Parse("""{"path":"hello.txt"}""").RootElement.Clone())
                    ]), cancellationToken);
                await onChunk(new BackingEngineChunk(IsComplete: true), cancellationToken);
                return;
            }

            var toolOut = request.ToolTurns![0].Results[0].Output;
            await onChunk(new BackingEngineChunk(DeltaText: $"Read result: {toolOut}"), cancellationToken);
            await onChunk(new BackingEngineChunk(
                Usage: new BackingEngineUsage(10, 5, null, CostSources.LocalOllama),
                IsComplete: true), cancellationToken);
        }
    }

    private sealed class StubPreambleCache : IPreambleCacheService
    {
        public PreambleEntry? Get(string key) => null;
        public PreambleEntry? GetGlobalGuidelines() => null;
        public PreambleEntry? GetMemberPreamble(string memberName) => null;
        public IReadOnlyCollection<PreambleEntry> ListAll() => [];
        public void ReloadAll() { }
        public long CurrentCacheBytes => 0;
        public long MaxCacheBytes => 1024;
        public int Count => 0;
    }

    [Fact]
    public async Task ToolLoopExecutesTier1ReadAndPausesForAlwaysAskApproval()
    {
        if (string.IsNullOrWhiteSpace(_masterConnection)) return;

        var dbName = $"GuildhallTest_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(_masterConnection) { InitialCatalog = dbName };
        var dbCreated = false;
        try
        {
            await using (var master = new SqlConnection(_masterConnection))
            {
                await master.OpenAsync();
                await using var cmd = new SqlCommand($"CREATE DATABASE [{dbName}];", master);
                await cmd.ExecuteNonQueryAsync();
                dbCreated = true;
            }

            var options = new DbContextOptionsBuilder<GuildhallDbContext>().UseSqlServer(builder.ConnectionString).Options;
            await using (var migrate = new GuildhallDbContext(options))
                await migrate.Database.MigrateAsync();

            var factory = new Factory(options);
            var pathGuard = new WorkspacePathGuard();
            var exfilGuard = new ExfiltrationGuard();
            var gate = new ToolPermissionGate(exfilGuard);
            var health = new McpHealthSupervisor(TimeProvider.System);
            var registry = new McpRegistry(
                [new LocalFilesystemMcpProvider(pathGuard)],
                factory,
                new McpJsonRpcClient(new StubHttpFactory(), new StubCreds(), new Configuration.EnvironmentScrubber(), new PrivateNetworkGuard()),
                health,
                exfilGuard);
            var contextBuilder = new ContextBuilder(
                new StaticOptions<CrossTurnBudgetOptions>(new CrossTurnBudgetOptions()),
                new Lazy<IPreambleCacheService>(() => new StubPreambleCache()),
                new Lazy<McpRegistry>(() => registry));

            var agentId = Guid.NewGuid();
            var convId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            await using (var seed = new GuildhallDbContext(options))
            {
                var now = DateTime.UtcNow;
                seed.AgentDefinitions.Add(new AgentDefinition
                {
                    Id = agentId,
                    Name = "ToolTester",
                    Role = "Tester",
                    Instructions = "Test",
                    EnabledToolIdsJson = """["filesystem-mcp"]""",
                    PermissionPolicy = PermissionPolicies.AlwaysAsk,
                    Revision = 1,
                });
                seed.Conversations.Add(new Conversation
                {
                    Id = convId,
                    Title = "Tool test",
                    AgentDefinitionId = agentId,
                    WorkspaceRootPath = _workspaceRoot,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                    Revision = 1,
                });
                var run = new ChatRun
                {
                    Id = runId,
                    ConversationId = convId,
                    TurnNumber = 1,
                    ClientRequestId = "req-1",
                    RequestFingerprint = "fp-1",
                    Status = ChatRunStatus.Running,
                    StartedUtc = now,
                    UpdatedUtc = now,
                    ProviderId = "stub-tool-engine",
                    ModelId = "stub",
                    InstructionsSnapshot = "Test",
                };
                run.Messages.Add(new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.User, Text = "Read hello.txt" });
                run.Messages.Add(new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.Assistant, Text = "" });
                seed.ChatRuns.Add(run);
                await seed.SaveChangesAsync();
            }

            var engine = new StubEngine();
            var runner = new ChatRunner(
                factory,
                new BackingEngineRegistry([engine]),
                TimeProvider.System,
                NullLogger<ChatRunner>.Instance,
                new Lazy<IContextBuilder>(() => contextBuilder),
                registry,
                gate,
                exfilGuard);

            var runTask = runner.RunAsync(runId, default);

            Guid pendingEventId = Guid.Empty;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                await using var check = new GuildhallDbContext(options);
                var ev = await check.ChatRunEvents.AsNoTracking().FirstOrDefaultAsync(x => x.RunId == runId);
                if (ev?.Status == "PendingApproval")
                {
                    pendingEventId = ev.Id;
                    break;
                }
                await Task.Delay(30);
            }

            Assert.NotEqual(Guid.Empty, pendingEventId);
            Assert.True(await runner.ResolveEventDecisionAsync(runId, pendingEventId, approved: true, default));

            await runTask;

            await using var verify = new GuildhallDbContext(options);
            var completedRun = await verify.ChatRuns.Include(x => x.Messages).Include(x => x.Events).SingleAsync(x => x.Id == runId);
            Assert.Equal(ChatRunStatus.Completed, completedRun.Status);
            var assistantText = completedRun.Messages.Single(x => x.Role == ChatMessageRole.Assistant).Text;
            Assert.Contains("Hello from Phase 3 MCP!", assistantText);
            var trace = Assert.Single(completedRun.Events);
            Assert.Equal("Completed", trace.Status);
            Assert.Equal("fs_read_file", trace.ToolName);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            if (dbCreated)
            {
                await using var master = new SqlConnection(_masterConnection);
                await master.OpenAsync();
                await using var drop = new SqlCommand($"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}];", master);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }

    private sealed class StaticOptions<T>(T value) : IOptionsMonitor<T> where T : class
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class StubHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubCreds : Configuration.ICredentialStore
    {
        public ValueTask<string?> GetApiKeyAsync(string providerId, CancellationToken ct = default) => ValueTask.FromResult<string?>(null);
        public ValueTask SetApiKeyAsync(string providerId, string apiKey, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask DeleteApiKeyAsync(string providerId, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<Configuration.CredentialStatus> GetStatusAsync(string providerId, CancellationToken ct = default) =>
            ValueTask.FromResult(new Configuration.CredentialStatus(false, null));
    }
}
