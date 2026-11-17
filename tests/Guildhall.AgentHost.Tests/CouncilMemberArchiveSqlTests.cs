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
                await before.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.Conversations (Id, AgentDefinitionId, Title, SelectedModelId, CreatedUtc, UpdatedUtc, IsArchived, ArchivedUtc, Revision) VALUES ({conversationId}, {memberId}, {"Retained session"}, {"session-model"}, {now}, {now}, {false}, {(DateTime?)null}, {4L})");
                await before.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.ChatRuns (Id, ConversationId, TurnNumber, ClientRequestId, RequestFingerprint, Status, StartedUtc, UpdatedUtc, FinishedUtc, Sequence, ErrorCode, InstructionsSnapshot, ProviderId, ModelId, SettingsSnapshotJson) VALUES ({runId}, {conversationId}, {1}, {"archive-upgrade"}, {"fixture"}, {"Completed"}, {now}, {now}, {now}, {3L}, {(string?)null}, {"Original run rules"}, {"ollama"}, {"original-model"}, {"{\"fixture\":true}"})");
                await before.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.ChatMessages (Id, RunId, Role, Text) VALUES ({messageId}, {runId}, {"User"}, {"Retain this question — Unicode ✓"})");
                await before.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.ChatAttachments (Id, MessageId, FileName, ContentType, Length, Content) VALUES ({Guid.NewGuid()}, {messageId}, {"fixture.bin"}, {"application/octet-stream"}, {attachmentBytes.LongLength}, {attachmentBytes})");
                await before.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO dbo.ChatMessages (Id, RunId, Role, Text) VALUES ({Guid.NewGuid()}, {runId}, {"Assistant"}, {"Retain this answer"})");
                installationId = (await before.ApplicationMetadata.SingleAsync()).InstallationId;
            }
            Assert.Equal("migration_required", (await readiness.CheckAsync(default)).Reason);
            // Compare every pre-existing column/value across all application tables.
            var tables = new[] { "ApplicationMetadata", "AgentDefinitions", "Conversations", "ChatRuns", "ChatMessages", "ChatAttachments" };
            await using var sql = new SqlConnection(connection.ConnectionString);
            await sql.OpenAsync();
            async Task<string> Snapshot(string table)
            {
                await using var columns = new SqlCommand("SELECT STRING_AGG(CONVERT(nvarchar(max), QUOTENAME(name)), ',') WITHIN GROUP (ORDER BY column_id) FROM sys.columns WHERE object_id = OBJECT_ID(@table) AND NOT (@table = 'dbo.AgentDefinitions' AND name IN ('IsArchived', 'DefaultProviderId', 'DefaultHarnessMode', 'EnabledToolIdsJson', 'PermissionPolicy')) AND NOT (@table = 'dbo.Conversations' AND name IN ('SelectedProviderId', 'SelectedHarnessMode', 'WorkspaceRootPath', 'PermissionPolicyOverride', 'ProjectId', 'ActiveWorktreeId')) AND NOT (@table = 'dbo.ChatRuns' AND name IN ('EngineKind', 'InputTokens', 'OutputTokens', 'EstimatedCostUsd', 'CostSource', 'DurationMs'))", sql);
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
                Assert.Equal(new[] { "20261116021040_CouncilMemberArchive", "20261116134707_CouncilMemberDeletionPermission", "20261116140438_GuildhallBehaviorSettings", "20261116232856_MemoryNotes", "20261117002946_Phase2BackingEngines", "20261117024435_Phase3McpToolsAndTraces", "20261117044509_Phase4ProjectsWorktreesAndRollbacks" }, await upgrade.Database.GetPendingMigrationsAsync());
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
