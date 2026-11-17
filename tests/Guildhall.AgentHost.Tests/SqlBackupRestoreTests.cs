using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Mcp.Providers;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Tests;

public sealed class SqlBackupRestoreTests
{
    [Fact]
    [Trait("Category", "SqlIntegration")]
    public async Task IsolatedCopyOnlyBackupAndRestorePreservesMigrationsAndRows()
    {
        var configured = Environment.GetEnvironmentVariable("GUILDHALL_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("SQL integration tests require an explicitly approved test connection.");
        var admin = new SqlConnectionStringBuilder(configured);
        if (admin.DataSource != "localhost" || admin.InitialCatalog != "master" || !admin.IntegratedSecurity)
            throw new InvalidOperationException("Tests require localhost/master with the approved Windows identity.");
        admin.Encrypt = SqlConnectionEncryptOption.Mandatory;
        admin.TrustServerCertificate = true;
        admin.ConnectTimeout = 5;

        var id = Guid.NewGuid().ToString("N")[..8];
        var sourceDb = $"GuildhallBackupTest_{id}";
        var restoreDb = $"GuildhallRestoreVerify_{id}";

        await using var master = new SqlConnection(admin.ConnectionString);
        await master.OpenAsync();

        string? sqlBackupDir = null;
        string? sqlDataDir = null;
        string? sqlLogDir = null;
        await using (var propCmd = new SqlCommand("SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(500)), CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(500)), CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(500));", master))
        await using (var propReader = await propCmd.ExecuteReaderAsync())
        {
            if (await propReader.ReadAsync())
            {
                sqlBackupDir = propReader.IsDBNull(0) ? null : propReader.GetString(0);
                sqlDataDir = propReader.IsDBNull(1) ? null : propReader.GetString(1);
                sqlLogDir = propReader.IsDBNull(2) ? null : propReader.GetString(2);
            }
        }

        var tempDir = sqlBackupDir ?? Path.GetTempPath();
        var dataDir = sqlDataDir ?? tempDir;
        var logDir = sqlLogDir ?? dataDir;
        var bakPath = Path.Combine(tempDir, $"{sourceDb}.bak");
        var restoreMdf = Path.Combine(dataDir, $"{restoreDb}.mdf");
        var restoreLdf = Path.Combine(logDir, $"{restoreDb}_log.ldf");

        async Task ExecMaster(string sql)
        {
            await using var cmd = new SqlCommand(sql, master) { CommandTimeout = 60 };
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            await ExecMaster($"CREATE DATABASE [{sourceDb}];");
            var srcConn = new SqlConnectionStringBuilder(admin.ConnectionString) { InitialCatalog = sourceDb };
            var options = new DbContextOptionsBuilder<GuildhallDbContext>().UseSqlServer(srcConn.ConnectionString).Options;

            await using (var db = new GuildhallDbContext(options))
            {
                await db.Database.MigrateAsync();
                db.SqlMcpProfiles.Add(new SqlMcpProfile
                {
                    Id = Guid.NewGuid(),
                    Name = "backup-fixture",
                    Server = "localhost",
                    Database = "SampleDb",
                    AuthMode = "integrated",
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow,
                    Revision = 1
                });
                await db.SaveChangesAsync();
            }

            await ExecMaster($"BACKUP DATABASE [{sourceDb}] TO DISK = N'{bakPath}' WITH COPY_ONLY, CHECKSUM, INIT;");
            await ExecMaster($"RESTORE VERIFYONLY FROM DISK = N'{bakPath}' WITH CHECKSUM;");

            string? logicalData = null;
            string? logicalLog = null;
            await using (var fileListCmd = new SqlCommand($"RESTORE FILELISTONLY FROM DISK = N'{bakPath}';", master))
            await using (var reader = await fileListCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var type = reader["Type"].ToString();
                    var logicalName = reader["LogicalName"].ToString();
                    if (type == "D" && logicalData is null) logicalData = logicalName;
                    else if (type == "L" && logicalLog is null) logicalLog = logicalName;
                }
            }

            Assert.NotNull(logicalData);
            Assert.NotNull(logicalLog);

            await ExecMaster($"""
                RESTORE DATABASE [{restoreDb}]
                FROM DISK = N'{bakPath}'
                WITH MOVE N'{logicalData}' TO N'{restoreMdf}',
                     MOVE N'{logicalLog}' TO N'{restoreLdf}',
                     RECOVERY, REPLACE;
                """);

            var dstConn = new SqlConnectionStringBuilder(admin.ConnectionString) { InitialCatalog = restoreDb };
            var dstOptions = new DbContextOptionsBuilder<GuildhallDbContext>().UseSqlServer(dstConn.ConnectionString).Options;
            await using var restored = new GuildhallDbContext(dstOptions);

            var pending = await restored.Database.GetPendingMigrationsAsync();
            Assert.Empty(pending);
            var restoredProfile = await restored.SqlMcpProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Name == "backup-fixture");
            Assert.NotNull(restoredProfile);
            Assert.Equal("SampleDb", restoredProfile.Database);
        }
        finally
        {
            foreach (var dbName in new[] { restoreDb, sourceDb })
            {
                try
                {
                    await ExecMaster($"IF DB_ID(N'{dbName}') IS NOT NULL BEGIN ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]; END");
                }
                catch { /* best effort cleanup */ }
            }
            try { if (File.Exists(bakPath)) File.Delete(bakPath); } catch { }
        }
    }

    [Fact]
    [Trait("Category", "SqlIntegration")]
    public async Task SqlServerMcpProviderEnforcesReadOnlyViewsAndRowCapsOnIsolatedDb()
    {
        var configured = Environment.GetEnvironmentVariable("GUILDHALL_TEST_SQL_CONNECTION")
            ?? throw new InvalidOperationException("SQL integration tests require an explicitly approved test connection.");
        var admin = new SqlConnectionStringBuilder(configured);
        if (admin.DataSource != "localhost" || admin.InitialCatalog != "master" || !admin.IntegratedSecurity)
            throw new InvalidOperationException("Tests require localhost/master with the approved Windows identity.");
        admin.Encrypt = SqlConnectionEncryptOption.Mandatory;
        admin.TrustServerCertificate = true;
        admin.ConnectTimeout = 5;

        var id = Guid.NewGuid().ToString("N")[..8];
        var metaDb = $"GuildhallMeta_{id}";
        var targetDb = $"GuildhallTarget_{id}";

        await using var master = new SqlConnection(admin.ConnectionString);
        await master.OpenAsync();
        async Task ExecMaster(string sql)
        {
            await using var cmd = new SqlCommand(sql, master);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            await ExecMaster($"CREATE DATABASE [{metaDb}]; CREATE DATABASE [{targetDb}];");

            var metaConn = new SqlConnectionStringBuilder(admin.ConnectionString) { InitialCatalog = metaDb };
            var metaOptions = new DbContextOptionsBuilder<GuildhallDbContext>().UseSqlServer(metaConn.ConnectionString).Options;
            var factory = new SimpleDbFactory(metaOptions);

            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.MigrateAsync();
                db.SqlMcpProfiles.Add(new SqlMcpProfile
                {
                    Id = Guid.NewGuid(),
                    Name = "home-sales",
                    Server = "localhost",
                    Database = targetDb,
                    AuthMode = "integrated",
                    AllowedSchemasJson = "[\"dbo\"]",
                    AllowedObjectsJson = "[\"dbo.v_SalesSummary\"]",
                    MaxRows = 2,
                    CommandTimeoutSeconds = 5,
                    IsEnabled = true,
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow,
                    Revision = 1
                });
                await db.SaveChangesAsync();
            }

            var targetConn = new SqlConnectionStringBuilder(admin.ConnectionString) { InitialCatalog = targetDb };
            await using (var targetSql = new SqlConnection(targetConn.ConnectionString))
            {
                await targetSql.OpenAsync();
                await using var setup = new SqlCommand("""
                    CREATE TABLE dbo.Sales (Id INT PRIMARY KEY, Customer NVARCHAR(50), Total DECIMAL(10,2));
                    INSERT INTO dbo.Sales VALUES (1, 'Alice', 10.50), (2, 'Bob', 20.00), (3, 'Cara', 30.00);
                    EXEC('CREATE VIEW dbo.v_SalesSummary AS SELECT Id, Customer, Total FROM dbo.Sales');
                    """, targetSql);
                await setup.ExecuteNonQueryAsync();
            }

            var provider = new SqlServerMcpProvider(factory, new NoopCreds(), new SqlReadOnlyGuard(), new ExfiltrationGuard());
            var (healthy, detail) = await provider.ProbeAsync(CancellationToken.None);
            Assert.True(healthy, detail);

            var listRes = await provider.InvokeAsync(
                new McpToolCallRequest("sql-server-mcp", "sql_list_objects", JsonDocument.Parse("{\"profileName\":\"home-sales\"}").RootElement, null),
                CancellationToken.None);
            Assert.False(listRes.IsError);
            Assert.Contains("v_SalesSummary", listRes.Content);
            Assert.DoesNotContain("\"Sales\"", listRes.Content);

            var queryRes = await provider.InvokeAsync(
                new McpToolCallRequest("sql-server-mcp", "sql_query", JsonDocument.Parse("{\"profileName\":\"home-sales\",\"sql\":\"SELECT Id, Customer, Total FROM dbo.v_SalesSummary ORDER BY Id\"}").RootElement, null),
                CancellationToken.None);
            Assert.False(queryRes.IsError);
            Assert.Contains("\"rowCount\":2", queryRes.Content);

            var blockedRes = await provider.InvokeAsync(
                new McpToolCallRequest("sql-server-mcp", "sql_query", JsonDocument.Parse("{\"profileName\":\"home-sales\",\"sql\":\"DELETE FROM dbo.Sales\"}").RootElement, null),
                CancellationToken.None);
            Assert.True(blockedRes.IsError);
            Assert.Equal("sql_readonly_violation", blockedRes.ErrorCode);
        }
        finally
        {
            foreach (var dbName in new[] { targetDb, metaDb })
            {
                try
                {
                    await ExecMaster($"IF DB_ID(N'{dbName}') IS NOT NULL BEGIN ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]; END");
                }
                catch { }
            }
        }
    }

    private sealed class SimpleDbFactory(DbContextOptions<GuildhallDbContext> options) : IDbContextFactory<GuildhallDbContext>
    {
        public GuildhallDbContext CreateDbContext() => new(options);
        public Task<GuildhallDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new GuildhallDbContext(options));
    }

    private sealed class NoopCreds : ICredentialStore
    {
        public ValueTask<string?> GetApiKeyAsync(string providerId, CancellationToken ct = default) => ValueTask.FromResult<string?>(null);
        public ValueTask SetApiKeyAsync(string providerId, string apiKey, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask DeleteApiKeyAsync(string providerId, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<CredentialStatus> GetStatusAsync(string providerId, CancellationToken ct = default) => ValueTask.FromResult(new CredentialStatus(false, null));
    }
}
