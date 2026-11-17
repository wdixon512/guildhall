using Guildhall.AgentHost.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Guildhall.AgentHost.Chat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Guildhall.AgentHost.Tests;

public sealed class PersistenceTests
{
    [Fact]
    [Trait("Category", "SqlIntegration")]
    public async Task PersistenceMigrationReadinessAndPermissionBoundaries()
    {
        var configured = Environment.GetEnvironmentVariable("GUILDHALL_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("SQL integration tests require an explicitly approved test connection.");
        var admin = new SqlConnectionStringBuilder(configured);
        if (admin.DataSource != "localhost" || admin.InitialCatalog != "master" || !admin.IntegratedSecurity)
            throw new InvalidOperationException("Tests require localhost/master with the approved Windows identity.");
        admin.Encrypt = SqlConnectionEncryptOption.Mandatory; admin.TrustServerCertificate = true;
        admin.ConnectTimeout = 3;
        var databaseName = "GuildhallTest_" + Guid.NewGuid().ToString("N");
        await using var master = new SqlConnection(admin.ConnectionString);
        await master.OpenAsync();
        var created = false;
        async Task ExecuteMaster(string sql) { await using var command = new SqlCommand(sql, master); await command.ExecuteNonQueryAsync(); }
        try
        {
            await ExecuteMaster($"CREATE DATABASE [{databaseName}]"); created = true;
            var connection = new SqlConnectionStringBuilder(admin.ConnectionString) { InitialCatalog = databaseName };
            var options = new DbContextOptionsBuilder<GuildhallDbContext>().UseSqlServer(connection.ConnectionString).Options;
            var readiness = new DatabaseReadiness(connection.ConnectionString);
            Assert.Equal("migration_required", (await readiness.CheckAsync(default)).Reason);
            Guid installationId;
            var legacyConversationIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
            await using (var db = new GuildhallDbContext(options))
            {
                await db.Database.ExecuteSqlRawAsync("CREATE USER [guildhall_runtime] WITHOUT LOGIN; GRANT CONNECT TO [guildhall_runtime]; DENY ALTER, TAKE OWNERSHIP, CREATE TABLE, CREATE VIEW, CREATE PROCEDURE TO [guildhall_runtime];");
                await db.Database.MigrateAsync("20261115231118_ChatAttachments");
                foreach (var id in legacyConversationIds)
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [dbo].[Conversations] ([Id],[Title],[AgentDefinitionId],[SelectedModelId],[CreatedUtc],[UpdatedUtc],[IsArchived],[ArchivedUtc],[Revision]) VALUES ({id},{"legacy session"},{Guid.Parse("7e3b38f2-2a51-4d15-93c2-0f8f7b8cf1c2")},{"model-legacy"},{DateTime.UtcNow},{DateTime.UtcNow},{false},{(DateTime?)null},{1L})");
                await db.Database.MigrateAsync();
                await db.Database.ExecuteSqlRawAsync("GRANT SELECT, UPDATE ON dbo.AgentDefinitions TO [guildhall_runtime]; GRANT SELECT ON dbo.Conversations TO [guildhall_runtime];");
                installationId = (await db.ApplicationMetadata.SingleAsync()).InstallationId;
                Assert.NotEqual(Guid.Empty, installationId);
                await db.Database.MigrateAsync();
                Assert.Equal(15, (await db.Database.GetAppliedMigrationsAsync()).Count());
            }
            // New context proves persisted data, not a tracked entity/cache.
            await using (var restarted = new GuildhallDbContext(options))
            {
                Assert.Equal(installationId, (await restarted.ApplicationMetadata.SingleAsync()).InstallationId);
                Assert.Equal("ready", (await readiness.CheckAsync(default)).Status);
                var migratedSessions = await restarted.Conversations.AsNoTracking().Where(x => legacyConversationIds.Contains(x.Id)).ToListAsync();
                Assert.Equal(2, migratedSessions.Count);
                Assert.Equal(2, migratedSessions.Select(x => x.AgentDefinitionId).Distinct().Count());
                Assert.DoesNotContain(migratedSessions, x => x.AgentDefinitionId == Guid.Parse("7e3b38f2-2a51-4d15-93c2-0f8f7b8cf1c2"));
                var migratedProfiles = await restarted.AgentDefinitions.AsNoTracking().Where(x => migratedSessions.Select(s => s.AgentDefinitionId).Contains(x.Id)).ToListAsync();
                Assert.All(migratedProfiles, profile => { Assert.Equal("Steward", profile.Role); Assert.Equal("#71465f", profile.Accent); Assert.False(profile.IsArchived); });
                await using (var permissionConnection = new SqlConnection(connection.ConnectionString))
                {
                    await permissionConnection.OpenAsync();
                    await using var permission = permissionConnection.CreateCommand();
                    permission.CommandText = "EXECUTE AS USER = 'guildhall_runtime'; SELECT HAS_PERMS_BY_NAME('dbo.Conversations', 'OBJECT', 'DELETE'), HAS_PERMS_BY_NAME('dbo.ChatRuns', 'OBJECT', 'DELETE'), HAS_PERMS_BY_NAME('dbo.ChatAttachments', 'OBJECT', 'SELECT'), HAS_PERMS_BY_NAME('dbo.ChatAttachments', 'OBJECT', 'INSERT'), HAS_PERMS_BY_NAME('dbo.ChatAttachments', 'OBJECT', 'UPDATE'), HAS_PERMS_BY_NAME('dbo.ChatAttachments', 'OBJECT', 'DELETE'), HAS_PERMS_BY_NAME('dbo.AgentDefinitions', 'OBJECT', 'SELECT'), HAS_PERMS_BY_NAME('dbo.AgentDefinitions', 'OBJECT', 'INSERT'), HAS_PERMS_BY_NAME('dbo.AgentDefinitions', 'OBJECT', 'UPDATE'), HAS_PERMS_BY_NAME('dbo.AgentDefinitions', 'OBJECT', 'DELETE');";
                    await using var reader = await permission.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal(1, reader.GetInt32(0));
                    Assert.Equal(0, reader.GetInt32(1));
                    Assert.Equal(1, reader.GetInt32(2));
                    Assert.Equal(1, reader.GetInt32(3));
                    Assert.Equal(1, reader.GetInt32(4));
                    Assert.Equal(0, reader.GetInt32(5));
                    Assert.Equal(1, reader.GetInt32(6));
                    Assert.Equal(1, reader.GetInt32(7));
                    Assert.Equal(1, reader.GetInt32(8));
                    Assert.Equal(1, reader.GetInt32(9));
                    reader.Close();
                    await using var revert = permissionConnection.CreateCommand();
                    revert.CommandText = "REVERT";
                    await revert.ExecuteNonQueryAsync();
                }
                var conversationId = Guid.NewGuid();
                var runId = Guid.NewGuid();
                await using (var seed = new GuildhallDbContext(options))
                {
                    var now = DateTime.UtcNow;
                    seed.Conversations.Add(new Conversation { Id = conversationId, Title = "isolated-delete-check", AgentDefinitionId = Guid.Parse("7e3b38f2-2a51-4d15-93c2-0f8f7b8cf1c2"), CreatedUtc = now, UpdatedUtc = now, Revision = 1 });
                    var run = new ChatRun { Id = runId, ConversationId = conversationId, TurnNumber = 1, ClientRequestId = "delete-check", RequestFingerprint = "test", Status = ChatRunStatus.Completed, StartedUtc = now, UpdatedUtc = now, FinishedUtc = now, InstructionsSnapshot = "test", ModelId = "test" };
                    run.Messages.Add(new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.User, Text = "test" });
                    run.Messages.Add(new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.Assistant, Text = "test" });
                    seed.ChatRuns.Add(run);
                    await seed.SaveChangesAsync();
                }
                var runner = new ChatRunner(new TestContextFactory(options), new Guildhall.AgentHost.Providers.BackingEngineRegistry([]), TimeProvider.System, NullLogger<ChatRunner>.Instance, new Lazy<Guildhall.AgentHost.Chat.IContextBuilder>(() => null!));
                Assert.True(await runner.CancelAsync(runId, default));
                await using (var completed = new GuildhallDbContext(options))
                    Assert.Equal(ChatRunStatus.Completed, (await completed.ChatRuns.SingleAsync(x => x.Id == runId)).Status);
                await using (var abandoned = new GuildhallDbContext(options))
                    await abandoned.ChatRuns.Where(x => x.Id == runId).ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, ChatRunStatus.Running));
                Assert.True(await runner.CancelAsync(runId, default));
                Assert.True(await runner.CancelAsync(runId, default));
                await using (var cancelled = new GuildhallDbContext(options))
                {
                    Assert.Equal(ChatRunStatus.Cancelled, (await cancelled.ChatRuns.SingleAsync(x => x.Id == runId)).Status);
                    Assert.Equal(2, await cancelled.ChatMessages.CountAsync(x => x.RunId == runId));
                }
                await using (var delete = new SqlConnection(connection.ConnectionString))
                {
                    await delete.OpenAsync();
                    await using var command = new SqlCommand("EXECUTE AS USER = 'guildhall_runtime'; DELETE FROM dbo.Conversations WHERE Id = @id; REVERT;", delete);
                    command.Parameters.AddWithValue("@id", conversationId);
                    await command.ExecuteNonQueryAsync();
                }
                await using (var deniedDelete = new SqlConnection(connection.ConnectionString))
                {
                    await deniedDelete.OpenAsync();
                    await using var executeAs = new SqlCommand("EXECUTE AS USER = 'guildhall_runtime';", deniedDelete);
                    await executeAs.ExecuteNonQueryAsync();
                    await using var directDelete = new SqlCommand("DELETE FROM dbo.ChatRuns WHERE Id = @id;", deniedDelete);
                    directDelete.Parameters.AddWithValue("@id", runId);
                    var denied = await Assert.ThrowsAsync<SqlException>(() => directDelete.ExecuteNonQueryAsync());
                    Assert.Contains("permission", denied.Message, StringComparison.OrdinalIgnoreCase);
                    await using var revert = new SqlCommand("REVERT;", deniedDelete);
                    await revert.ExecuteNonQueryAsync();
                }
                await using (var verifyCascade = new GuildhallDbContext(options))
                {
                    Assert.False(await verifyCascade.Conversations.AnyAsync(x => x.Id == conversationId));
                    Assert.False(await verifyCascade.ChatRuns.AnyAsync(x => x.Id == runId));
                    Assert.False(await verifyCascade.ChatMessages.AnyAsync(x => x.RunId == runId));
                }
                await restarted.Database.ExecuteSqlRawAsync("CREATE USER [guildhall_test_runtime] WITHOUT LOGIN; GRANT CONNECT TO [guildhall_test_runtime]; GRANT SELECT ON dbo.ApplicationMetadata TO [guildhall_test_runtime]; GRANT SELECT ON dbo.__EFMigrationsHistory TO [guildhall_test_runtime]; DENY ALTER, TAKE OWNERSHIP, CREATE TABLE, CREATE VIEW, CREATE PROCEDURE TO [guildhall_test_runtime];");
                await using var restricted = new SqlConnection(connection.ConnectionString);
                await restricted.OpenAsync();
                await using var scoped = restricted.CreateCommand();
                scoped.CommandText = "EXECUTE AS USER = 'guildhall_test_runtime'; SELECT COUNT(*) FROM dbo.ApplicationMetadata;";
                Assert.Equal(1, (int)(await scoped.ExecuteScalarAsync())!);
                scoped.CommandText = "ALTER TABLE dbo.ApplicationMetadata ADD UnauthorizedColumn int NULL";
                await Assert.ThrowsAsync<SqlException>(() => scoped.ExecuteNonQueryAsync());
                scoped.CommandText = "UPDATE dbo.ApplicationMetadata SET InstallationId = NEWID()";
                await Assert.ThrowsAsync<SqlException>(() => scoped.ExecuteNonQueryAsync());
                scoped.CommandText = "REVERT";
                await scoped.ExecuteNonQueryAsync();
            }
            await QueueSqlChecks.VerifyAsync(options);
            await QueueSqlChecks.VerifyAsync(options, 300);
            await AgentProfileSqlChecks.VerifyAsync(options);
            // Exercise EF's migration executor against a deliberately failing transactional batch.
            await using (var db = new GuildhallDbContext(options))
            {
                var generator = db.GetService<IMigrationsSqlGenerator>();
                var commands = generator.Generate([
                    new SqlOperation { Sql = "CREATE TABLE dbo.ShouldRollback (Id int NOT NULL);" },
                    new SqlOperation { Sql = "THROW 51000, 'Deliberate migration failure', 1;" },
                    new SqlOperation { Sql = "INSERT INTO dbo.__EFMigrationsHistory VALUES ('99999999999999_Failed', '10.0.15');" }
                ]);
                var executor = db.GetService<IMigrationCommandExecutor>();
                var state = new MigrationExecutionState();
                try
                {
                    var failure = await Assert.ThrowsAsync<SqlException>(() => executor.ExecuteNonQueryAsync(commands, db.GetService<IRelationalConnection>(), state, commitTransaction: true));
                    Assert.Equal(51000, failure.Number);
                }
                finally { if (state.Transaction is not null) await state.Transaction.DisposeAsync(); }
            }
            await using (var db = new GuildhallDbContext(options))
            {
                Assert.Equal(15, (await db.Database.GetAppliedMigrationsAsync()).Count());
                await using var sql = new SqlConnection(connection.ConnectionString); await sql.OpenAsync();
                await using var command = new SqlCommand("SELECT OBJECT_ID(N'dbo.ShouldRollback')", sql);
                Assert.Equal(DBNull.Value, await command.ExecuteScalarAsync());
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
            if (created) await ExecuteMaster($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
        }
    }

    private sealed class TestContextFactory(DbContextOptions<GuildhallDbContext> options) : IDbContextFactory<GuildhallDbContext>
    {
        public GuildhallDbContext CreateDbContext() => new(options);
    }
}
