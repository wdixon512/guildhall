using System.Net;
using System.Net.Http.Json;
using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Guildhall.AgentHost.Tests;

public sealed class MemoryNotesTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Rejects_Invalid_Memory_Scope_And_Body_Requests()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var badScope = await client.PostAsJsonAsync("/memory-notes", new CreateMemoryNoteRequest("Project", null, "Note"));
        Assert.Equal(HttpStatusCode.BadRequest, badScope.StatusCode);

        var sharedWithMember = await client.PostAsJsonAsync("/memory-notes", new CreateMemoryNoteRequest("AboutYou", Guid.NewGuid(), "Shared"));
        Assert.Equal(HttpStatusCode.BadRequest, sharedWithMember.StatusCode);

        var memberWithoutOwner = await client.PostAsJsonAsync("/memory-notes", new CreateMemoryNoteRequest("Member", null, "Private"));
        Assert.Equal(HttpStatusCode.BadRequest, memberWithoutOwner.StatusCode);

        var emptyBody = await client.PostAsJsonAsync("/memory-notes", new CreateMemoryNoteRequest("AboutYou", null, "   "));
        Assert.Equal(HttpStatusCode.BadRequest, emptyBody.StatusCode);

        var tooLongBody = await client.PostAsJsonAsync("/memory-notes", new CreateMemoryNoteRequest("AboutYou", null, new string('x', 2001)));
        Assert.Equal(HttpStatusCode.BadRequest, tooLongBody.StatusCode);

        var listMemberWithoutId = await client.GetAsync("/memory-notes?scope=Member");
        Assert.Equal(HttpStatusCode.BadRequest, listMemberWithoutId.StatusCode);
    }

    [Fact]
    [Trait("Category", "SqlIntegration")]
    public async Task MemoryNotes_Migration_Grants_Scope_Isolation_And_Crud_Work_On_Isolated_Sql()
    {
        var configured = Environment.GetEnvironmentVariable("GUILDHALL_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("SQL integration tests require an explicitly approved test connection.");
        var admin = new SqlConnectionStringBuilder(configured);
        if (admin.DataSource != "localhost" || admin.InitialCatalog != "master" || !admin.IntegratedSecurity)
            throw new InvalidOperationException("Tests require localhost/master with the approved Windows identity.");
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
            var runtimeLogin = "guildhall_test_" + Guid.NewGuid().ToString("N")[..8];
            var runtimePassword = "G!h" + Guid.NewGuid().ToString("N") + "9a";
            await ExecuteMaster($"CREATE LOGIN [{runtimeLogin}] WITH PASSWORD = '{runtimePassword}', CHECK_POLICY = OFF");

            try
            {
                await using (var before = new GuildhallDbContext(options))
                {
                    await before.Database.OpenConnectionAsync();
                    await using (var createUser = before.Database.GetDbConnection().CreateCommand())
                    {
                        createUser.CommandText = $"CREATE USER [guildhall_runtime] FOR LOGIN [{runtimeLogin}]; GRANT CONNECT TO [guildhall_runtime]; DENY ALTER, TAKE OWNERSHIP, CREATE TABLE, CREATE VIEW, CREATE PROCEDURE TO [guildhall_runtime];";
                        await createUser.ExecuteNonQueryAsync();
                    }
                    await before.Database.MigrateAsync("20261116140438_GuildhallBehaviorSettings");
                    await before.Database.ExecuteSqlRawAsync("GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.AgentDefinitions TO [guildhall_runtime]; GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Conversations TO [guildhall_runtime]; GRANT SELECT ON dbo.ChatRuns TO [guildhall_runtime]; GRANT SELECT ON dbo.ApplicationMetadata TO [guildhall_runtime]; GRANT SELECT ON dbo.__EFMigrationsHistory TO [guildhall_runtime];");
                    before.AgentDefinitions.Add(new AgentDefinition { Id = memberA, Name = "Maren", Role = "Steward", PortraitId = 0, Accent = "#71465f", Instructions = "A", Revision = 1 });
                    before.AgentDefinitions.Add(new AgentDefinition { Id = memberB, Name = "Scholar", Role = "Analyst", PortraitId = 1, Accent = "#536078", Instructions = "B", Revision = 1 });
                    var now = DateTime.UtcNow;
                    before.Conversations.Add(new Conversation { Id = conversationId, AgentDefinitionId = memberA, Title = "Source chat", CreatedUtc = now, UpdatedUtc = now, Revision = 1 });
                    await before.SaveChangesAsync();
                }

                await using (var upgrade = new GuildhallDbContext(options))
                {
                    await upgrade.Database.MigrateAsync();
                    await upgrade.Database.MigrateAsync();
                    Assert.Empty(await upgrade.Database.GetPendingMigrationsAsync());
                }

                await using (var sql = new SqlConnection(connection.ConnectionString))
                {
                    await sql.OpenAsync();
                    await using var permCmd = sql.CreateCommand();
                    permCmd.CommandText = "EXECUTE AS USER = 'guildhall_runtime'; SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER'), IS_ROLEMEMBER('db_owner'), HAS_PERMS_BY_NAME('dbo.MemoryNotes', 'OBJECT', 'SELECT'), HAS_PERMS_BY_NAME('dbo.MemoryNotes', 'OBJECT', 'INSERT'), HAS_PERMS_BY_NAME('dbo.MemoryNotes', 'OBJECT', 'UPDATE'), HAS_PERMS_BY_NAME('dbo.MemoryNotes', 'OBJECT', 'DELETE');";
                    await using var reader = await permCmd.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal(0, reader.GetInt32(0));
                    Assert.Equal(0, reader.GetInt32(1));
                    Assert.Equal(1, reader.GetInt32(2));
                    Assert.Equal(1, reader.GetInt32(3));
                    Assert.Equal(1, reader.GetInt32(4));
                    Assert.Equal(1, reader.GetInt32(5));
                }

                var runtimeConn = new SqlConnectionStringBuilder(connection.ConnectionString)
                {
                    IntegratedSecurity = false,
                    UserID = runtimeLogin,
                    Password = runtimePassword
                }.ConnectionString;

                await using var factory = CreateFactory(runtimeConn);
                using var client = CreateClient(factory);

                var sharedResp = await client.PostAsJsonAsync("/memory-notes", new CreateMemoryNoteRequest("AboutYou", null, "Prefer concise bullet summaries.", true, conversationId));
                Assert.Equal(HttpStatusCode.Created, sharedResp.StatusCode);
                var shared = await sharedResp.Content.ReadFromJsonAsync<MemoryNoteResponse>();
                Assert.NotNull(shared);

                var privateAResp = await client.PostAsJsonAsync("/memory-notes", new CreateMemoryNoteRequest("Member", memberA, "Track API contracts first.", true, conversationId));
                Assert.Equal(HttpStatusCode.Created, privateAResp.StatusCode);
                var privateA = await privateAResp.Content.ReadFromJsonAsync<MemoryNoteResponse>();
                Assert.NotNull(privateA);

                var listA = await client.GetFromJsonAsync<List<MemoryNoteResponse>>($"/memory-notes?agentDefinitionId={memberA}");
                Assert.NotNull(listA);
                Assert.Equal(2, listA.Count);

                var listB = await client.GetFromJsonAsync<List<MemoryNoteResponse>>($"/memory-notes?agentDefinitionId={memberB}");
                Assert.NotNull(listB);
                Assert.Single(listB);
                Assert.Equal(shared.Id, listB[0].Id);

                var search = await client.GetFromJsonAsync<List<MemoryNoteResponse>>($"/memory-notes?scope=AboutYou&query=bullet");
                Assert.NotNull(search);
                Assert.Single(search);

                var conflict = await client.PatchAsJsonAsync($"/memory-notes/{shared.Id}", new UpdateMemoryNoteRequest("Updated", null, 999));
                Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

                var updatedResp = await client.PatchAsJsonAsync($"/memory-notes/{shared.Id}", new UpdateMemoryNoteRequest(null, false, shared.Revision));
                Assert.Equal(HttpStatusCode.OK, updatedResp.StatusCode);
                var updated = await updatedResp.Content.ReadFromJsonAsync<MemoryNoteResponse>();
                Assert.NotNull(updated);
                Assert.False(updated.IsEnabled);
                Assert.Equal(2, updated.Revision);

                // Deleting source conversation preserves independently saved memory notes.
                var deleteConv = await client.DeleteAsync($"/conversations/{conversationId}?expectedRevision=1");
                Assert.Equal(HttpStatusCode.NoContent, deleteConv.StatusCode);
                var afterConvDelete = await client.GetFromJsonAsync<List<MemoryNoteResponse>>($"/memory-notes?agentDefinitionId={memberA}");
                Assert.NotNull(afterConvDelete);
                Assert.Equal(2, afterConvDelete.Count);

                // Deleting member cascades private member notes, leaving shared AboutYou notes intact.
                var deleteMemberA = await client.DeleteAsync($"/agents/{memberA}?expectedRevision=1");
                Assert.Equal(HttpStatusCode.NoContent, deleteMemberA.StatusCode);
                var remainingShared = await client.GetFromJsonAsync<List<MemoryNoteResponse>>("/memory-notes?scope=AboutYou");
                Assert.NotNull(remainingShared);
                Assert.Single(remainingShared);

                var forget = await client.DeleteAsync($"/memory-notes/{updated.Id}?expectedRevision={updated.Revision}");
                Assert.Equal(HttpStatusCode.NoContent, forget.StatusCode);
                var afterForget = await client.GetFromJsonAsync<List<MemoryNoteResponse>>("/memory-notes?scope=AboutYou");
                Assert.NotNull(afterForget);
                Assert.Empty(afterForget);
            }
            finally
            {
                SqlConnection.ClearAllPools();
                if (created)
                {
                    await ExecuteMaster($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
                    created = false;
                }
                await ExecuteMaster($"DROP LOGIN [{runtimeLogin}]");
            }
        }
        finally
        {
            if (created)
            {
                await ExecuteMaster($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
            }
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(string? sqlConnection = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GUILDHALL_SESSION_TOKEN"] = Token,
                    ["GUILDHALL_HOST_URL"] = "http://127.0.0.1:43120",
                    ["GUILDHALL_OLLAMA_URL"] = "http://127.0.0.1:11434",
                    ["ConnectionStrings:Guildhall"] = sqlConnection
                });
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "127.0.0.1:43120";
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        return client;
    }
}
