using Guildhall.AgentHost.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Tests;

public sealed class CouncilMemberArchiveSqlTests
{
    [Fact]
    [Trait("Category", "SqlIntegration")]
    public async Task ArchiveUpgradePreservesExistingHistoryAndRestrictedPermissions()
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
            var readiness = new DatabaseReadiness(connection.ConnectionString);
            var memberId = Guid.NewGuid();
            var conversationId = Guid.NewGuid();
            var runId = Guid.NewGuid();
            var messageId = Guid.NewGuid();
            byte[] attachmentBytes = [0, 1, 127, 255];
            Guid installationId;
            await using (var before = new GuildhallDbContext(options))
            {
                await before.Database.ExecuteSqlRawAsync("CREATE USER [guildhall_runtime] WITHOUT LOGIN; GRANT CONNECT TO [guildhall_runtime]; DENY ALTER, TAKE OWNERSHIP, CREATE TABLE, CREATE VIEW, CREATE PROCEDURE TO [guildhall_runtime];");
                await before.Database.MigrateAsync("20261116012111_CouncilProfiles");
                await before.Database.ExecuteSqlRawAsync("GRANT SELECT, UPDATE ON dbo.AgentDefinitions TO [guildhall_runtime]; GRANT SELECT ON dbo.ApplicationMetadata TO [guildhall_runtime]; GRANT SELECT ON dbo.__EFMigrationsHistory TO [guildhall_runtime];");
                // The pre-upgrade table deliberately has no IsArchived column.
                await before.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.AgentDefinitions (Id, Name, Role, PortraitId, CustomPortrait, Accent, Instructions, DefaultModelId, Revision) VALUES ({memberId}, {"Archive fixture"}, {"Reviewer"}, {2}, {"fixture portrait"}, {"#536078"}, {"# Member rules"}, {"fixture-model"}, {9L})");
                var now = DateTime.UtcNow;
                before.Conversations.Add(new Conversation { Id = conversationId, AgentDefinitionId = memberId, Title = "Retained session", SelectedModelId = "session-model", CreatedUtc = now, UpdatedUtc = now, Revision = 4 });
                var run = new ChatRun { Id = runId, ConversationId = conversationId, TurnNumber = 1, ClientRequestId = "archive-upgrade", RequestFingerprint = "fixture", Status = ChatRunStatus.Completed, StartedUtc = now, UpdatedUtc = now, FinishedUtc = now, Sequence = 3, InstructionsSnapshot = "Original run rules", ModelId = "original-model", SettingsSnapshotJson = "{\"fixture\":true}" };
                var user = new ChatMessage { Id = messageId, Role = ChatMessageRole.User, Text = "Retain this question — Unicode ✓" };
                user.Attachments.Add(new ChatAttachment { Id = Guid.NewGuid(), FileName = "fixture.bin", ContentType = "application/octet-stream", Content = attachmentBytes, Length = attachmentBytes.Length });
                run.Messages.Add(user);
                run.Messages.Add(new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.Assistant, Text = "Retain this answer" });
                before.ChatRuns.Add(run);
                await before.SaveChangesAsync();
                installationId = (await before.ApplicationMetadata.SingleAsync()).InstallationId;
            }
            Assert.Equal("migration_required", (await readiness.CheckAsync(default)).Reason);
            // Compare every pre-existing column/value across all application tables.
            var tables = new[] { "ApplicationMetadata", "AgentDefinitions", "Conversations", "ChatRuns", "ChatMessages", "ChatAttachments" };
            await using var sql = new SqlConnection(connection.ConnectionString);
            await sql.OpenAsync();
            async Task<string> Snapshot(string table)
            {
                await using var columns = new SqlCommand("SELECT STRING_AGG(CONVERT(nvarchar(max), QUOTENAME(name)), ',') WITHIN GROUP (ORDER BY column_id) FROM sys.columns WHERE object_id = OBJECT_ID(@table) AND NOT (@table = 'dbo.AgentDefinitions' AND name = 'IsArchived')", sql);
                columns.Parameters.AddWithValue("@table", "dbo." + table);
                var projection = (string)(await columns.ExecuteScalarAsync())!;
                await using var query = new SqlCommand($"SELECT {projection} FROM dbo.[{table}] ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES", sql);
                await using var reader = await query.ExecuteReaderAsync();
                var result = new System.Text.StringBuilder();
                while (await reader.ReadAsync()) result.Append(reader.GetString(0));
                return result.ToString();
            }
            var snapshots = new Dictionary<string, string>();
            foreach (var table in tables) snapshots.Add(table, await Snapshot(table));
            await using (var upgrade = new GuildhallDbContext(options))
            {
                Assert.Equal(new[] { "20261116021040_CouncilMemberArchive", "20261116134707_CouncilMemberDeletionPermission", "20261116140438_GuildhallBehaviorSettings", "20261116232856_MemoryNotes" }, await upgrade.Database.GetPendingMigrationsAsync());
                await upgrade.Database.MigrateAsync();
                await upgrade.Database.MigrateAsync();
                Assert.Empty(await upgrade.Database.GetPendingMigrationsAsync());
                Assert.All(await upgrade.AgentDefinitions.AsNoTracking().ToListAsync(), x => Assert.False(x.IsArchived));
                Assert.Equal(installationId, (await upgrade.ApplicationMetadata.SingleAsync()).InstallationId);
            }
            await AgentProfileSqlChecks.VerifyProtectedSeedDeleteAsync(options);
            foreach (var table in tables) Assert.Equal(snapshots[table], await Snapshot(table));
            Assert.Equal("ready", (await readiness.CheckAsync(default)).Status);

            // Existing table-specific runtime grants cover the new column, without DDL/delete rights.
            await using var runtime = sql.CreateCommand();
            runtime.CommandText = "EXECUTE AS USER = 'guildhall_runtime'; SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER'), IS_ROLEMEMBER('db_owner'), IS_ROLEMEMBER('db_datawriter'), HAS_PERMS_BY_NAME('dbo.Conversations', 'OBJECT', 'DELETE'), HAS_PERMS_BY_NAME('dbo.AgentDefinitions', 'OBJECT', 'DELETE'), HAS_PERMS_BY_NAME('dbo.ChatRuns', 'OBJECT', 'DELETE'), HAS_PERMS_BY_NAME('dbo.ChatMessages', 'OBJECT', 'DELETE'), HAS_PERMS_BY_NAME('dbo.ChatAttachments', 'OBJECT', 'DELETE');";
            await using (var permissions = await runtime.ExecuteReaderAsync())
            {
                Assert.True(await permissions.ReadAsync());
                for (var i = 0; i < 3; i++) Assert.Equal(0, permissions.GetInt32(i));
                Assert.Equal(1, permissions.GetInt32(3)); // Existing conversation lifecycle grant is preserved.
                Assert.Equal(1, permissions.GetInt32(4)); // New, narrowly scoped member-delete grant.
                for (var i = 5; i < 8; i++) Assert.Equal(0, permissions.GetInt32(i));
            }
            runtime.CommandText = "ALTER TABLE dbo.AgentDefinitions ADD UnauthorizedColumn int NULL";
            await Assert.ThrowsAsync<SqlException>(() => runtime.ExecuteNonQueryAsync());
            runtime.Parameters.AddWithValue("@runId", runId);
            runtime.CommandText = "DELETE FROM dbo.ChatRuns WHERE Id = @runId;";
            await Assert.ThrowsAsync<SqlException>(() => runtime.ExecuteNonQueryAsync());
            runtime.Parameters.AddWithValue("@id", memberId);
            runtime.CommandText = "UPDATE dbo.AgentDefinitions SET IsArchived = 1 WHERE Id = @id; SELECT IsArchived FROM dbo.AgentDefinitions WHERE Id = @id;";
            Assert.True((bool)(await runtime.ExecuteScalarAsync())!);
            runtime.CommandText = "UPDATE dbo.AgentDefinitions SET IsArchived = 0 WHERE Id = @id; SELECT IsArchived FROM dbo.AgentDefinitions WHERE Id = @id;";
            Assert.False((bool)(await runtime.ExecuteScalarAsync())!);
            runtime.CommandText = "REVERT";
            await runtime.ExecuteNonQueryAsync();
            foreach (var table in tables) Assert.Equal(snapshots[table], await Snapshot(table));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            if (created) await ExecuteMaster($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
        }
    }
}
