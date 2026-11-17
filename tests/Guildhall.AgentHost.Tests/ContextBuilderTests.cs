using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Chat;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Providers.Ollama;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Guildhall.AgentHost.Tests;

public sealed class ContextBuilderTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void BuildStaticSystemMessage_Places_Behavior_Guidelines_Instructions_And_Scoped_Memories_In_Order()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "guildhall-ctx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var cache = new PreambleCacheService(
                new StaticOptionsMonitor<PreambleCacheOptions>(new PreambleCacheOptions { DirectoryPath = tempDir, EnableFileWatcher = false }),
                TimeProvider.System,
                new StubHostEnvironment(tempDir),
                NullLogger<PreambleCacheService>.Instance);

            var builder = new ContextBuilder(
                new StaticOptionsMonitor<CrossTurnBudgetOptions>(new CrossTurnBudgetOptions()),
                new Lazy<IPreambleCacheService>(() => cache));

            var system = builder.BuildStaticSystemMessage(
                "You are a capable member of the Guildhall council.",
                "Rely strictly on observed outputs.",
                "Focus on architecture.",
                [("AboutYou", "Prefer concise bullets."), ("Member", "Check SQL grants.")]);

            Assert.StartsWith("You are a capable member of the Guildhall council.", system, StringComparison.Ordinal);
            Assert.Contains("Rely strictly on observed outputs.", system, StringComparison.Ordinal);
            Assert.Contains("Member instructions:\nFocus on architecture.", system, StringComparison.Ordinal);
            Assert.Contains("Shared memory (About you", system, StringComparison.Ordinal);
            Assert.Contains("- Prefer concise bullets.", system, StringComparison.Ordinal);
            Assert.Contains("Member memory", system, StringComparison.Ordinal);
            Assert.Contains("- Check SQL grants.", system, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetRunningModelsAsync_Parses_Api_Ps_And_Degrades_When_Unavailable()
    {
        var loadedClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"models\":[{\"name\":\"qwen3:1.7b\",\"size\":1500000000,\"size_vram\":1200000000,\"expires_at\":\"2026-11-16T19:15:00Z\",\"context_length\":4096}]}",
                Encoding.UTF8,
                "application/json")
        }))
        { BaseAddress = new Uri("http://127.0.0.1:11434") };

        var loaded = await new OllamaDiscovery(loadedClient).GetRunningModelsAsync(useGpu: true, keepAliveSeconds: 900, default);
        Assert.Equal("loaded", loaded.Status);
        Assert.True(loaded.UseGpu);
        Assert.Equal(900, loaded.KeepAliveSeconds);
        Assert.Single(loaded.Models);
        Assert.Equal("qwen3:1.7b", loaded.Models[0].Name);
        Assert.Equal(1_200_000_000L, loaded.Models[0].SizeVramBytes);
        Assert.Equal(4096, loaded.Models[0].ContextLength);

        var downClient = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")))
        { BaseAddress = new Uri("http://127.0.0.1:11434") };

        var down = await new OllamaDiscovery(downClient).GetRunningModelsAsync(useGpu: false, keepAliveSeconds: 0, default);
        Assert.Equal("unavailable", down.Status);
        Assert.Empty(down.Models);
    }

    [Fact]
    [Trait("Category", "SqlIntegration")]
    public async Task ContextBuilder_Enforces_Memory_Scope_Budget_And_Inspector_On_Isolated_Sql()
    {
        var configured = Environment.GetEnvironmentVariable("GUILDHALL_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("SQL integration tests require an explicitly approved test connection.");
        var admin = new SqlConnectionStringBuilder(configured);
        admin.Encrypt = SqlConnectionEncryptOption.Mandatory;
        admin.TrustServerCertificate = true;
        admin.ConnectTimeout = 3;

        var databaseName = "GuildhallTest_" + Guid.NewGuid().ToString("N");
        await using var master = new SqlConnection(admin.ConnectionString);
        await master.OpenAsync();
        var created = false;
        async Task ExecuteMaster(string sql)
        {
            await using var command = new SqlCommand(sql, master);
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            await ExecuteMaster($"CREATE DATABASE [{databaseName}]");
            created = true;
            var connection = new SqlConnectionStringBuilder(admin.ConnectionString) { InitialCatalog = databaseName };
            var options = new DbContextOptionsBuilder<GuildhallDbContext>().UseSqlServer(connection.ConnectionString).Options;

            var memberA = Guid.NewGuid();
            var memberB = Guid.NewGuid();
            var conversationId = Guid.NewGuid();

            await using (var setup = new GuildhallDbContext(options))
            {
                await setup.Database.MigrateAsync();
                setup.AgentDefinitions.Add(new AgentDefinition { Id = memberA, Name = "Maren", Role = "Steward", PortraitId = 0, Accent = "#71465f", Instructions = "Be concise.", Revision = 1 });
                setup.AgentDefinitions.Add(new AgentDefinition { Id = memberB, Name = "Scholar", Role = "Analyst", PortraitId = 1, Accent = "#536078", Instructions = "Analyze deeply.", Revision = 1 });
                var now = DateTime.UtcNow;
                setup.Conversations.Add(new Conversation { Id = conversationId, AgentDefinitionId = memberA, Title = "Context test", SelectedModelId = "qwen3:1.7b", CreatedUtc = now, UpdatedUtc = now, Revision = 1 });
                setup.MemoryNotes.Add(new MemoryNote { Id = Guid.NewGuid(), Scope = MemoryNoteScope.AboutYou, Body = "Shared enabled note.", IsEnabled = true, CreatedUtc = now, UpdatedUtc = now, Revision = 1 });
                setup.MemoryNotes.Add(new MemoryNote { Id = Guid.NewGuid(), Scope = MemoryNoteScope.AboutYou, Body = "Shared disabled note.", IsEnabled = false, CreatedUtc = now, UpdatedUtc = now, Revision = 1 });
                setup.MemoryNotes.Add(new MemoryNote { Id = Guid.NewGuid(), Scope = MemoryNoteScope.Member, AgentDefinitionId = memberA, Body = "Maren private note.", IsEnabled = true, CreatedUtc = now, UpdatedUtc = now, Revision = 1 });
                setup.MemoryNotes.Add(new MemoryNote { Id = Guid.NewGuid(), Scope = MemoryNoteScope.Member, AgentDefinitionId = memberB, Body = "Scholar private note.", IsEnabled = true, CreatedUtc = now, UpdatedUtc = now, Revision = 1 });
                await setup.SaveChangesAsync();
            }

            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["GUILDHALL_SESSION_TOKEN"] = Token,
                        ["GUILDHALL_HOST_URL"] = "http://127.0.0.1:43120",
                        ["GUILDHALL_OLLAMA_URL"] = "http://127.0.0.1:11434",
                        ["CrossTurnBudget:MaxTurnContextTokens"] = "512",
                        ["CrossTurnBudget:MaxSessionTokens"] = "1024"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDbContextFactory<GuildhallDbContext>>();
                    services.AddSingleton<IDbContextFactory<GuildhallDbContext>>(new DirectFactory(options));
                    services.RemoveAll<OllamaDiscovery>();
                    services.AddSingleton(new OllamaDiscovery(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"models\":[{\"name\":\"qwen3:1.7b\",\"size\":1000,\"size_vram\":900}]}", Encoding.UTF8, "application/json")
                    })) { BaseAddress = new Uri("http://127.0.0.1:11434") }));
                });
            });

            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Host = "127.0.0.1:43120";
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

            var inspector = await client.GetFromJsonAsync<ContextInspectorResponse>($"/conversations/{conversationId}/context");
            Assert.NotNull(inspector);
            Assert.Equal("loaded", inspector.Vram.Status);
            Assert.Equal(2, inspector.MemoryNotes.Count);
            Assert.Contains(inspector.MemoryNotes, x => x.Preview.Contains("Shared enabled note.", StringComparison.Ordinal));
            Assert.Contains(inspector.MemoryNotes, x => x.Preview.Contains("Maren private note.", StringComparison.Ordinal));
            Assert.DoesNotContain(inspector.MemoryNotes, x => x.Preview.Contains("disabled", StringComparison.Ordinal));
            Assert.DoesNotContain(inspector.MemoryNotes, x => x.Preview.Contains("Scholar", StringComparison.Ordinal));
            Assert.Equal(512, inspector.Budget.MaxTurnContextTokens);
            Assert.Equal(1024, inspector.Budget.MaxSessionTokens);

            // Oversized message exceeds MaxTurnContextTokens (512) and must be rejected before creating a run.
            var oversized = await client.PostAsJsonAsync($"/conversations/{conversationId}/messages",
                new CreateMessageRequest(Guid.NewGuid().ToString(), 1, new string('w', 3000), "queue"));
            Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
            var err = await oversized.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.Equal("context_overflow", err?.Code);

            await using var verifyDb = new GuildhallDbContext(options);
            Assert.Empty(await verifyDb.ChatRuns.Where(x => x.ConversationId == conversationId).ToListAsync());
        }
        finally
        {
            SqlConnection.ClearAllPools();
            if (created)
            {
                await ExecuteMaster($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
            }
        }
    }

    private sealed class DirectFactory(DbContextOptions<GuildhallDbContext> options) : IDbContextFactory<GuildhallDbContext>
    {
        public GuildhallDbContext CreateDbContext() => new(options);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class StubHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Guildhall.AgentHost.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
