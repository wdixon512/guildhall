using Guildhall.AgentHost.Mcp.Providers;

namespace Guildhall.AgentHost.Tests;

public sealed class SqlReadOnlyGuardTests
{
    private readonly SqlReadOnlyGuard _guard = new();

    [Theory]
    [InlineData("SELECT TOP 10 Id, Name FROM dbo.Orders")]
    [InlineData("WITH cte AS (SELECT Id FROM dbo.Items) SELECT * FROM cte;")]
    [InlineData("-- comment\nSELECT 1 AS Ok")]
    [InlineData("/* block comment with DELETE */ SELECT 'DROP TABLE x' AS TextVal FROM dbo.Notes")]
    public void AllowsSingleStatementReadOnlyQueries(string sql)
    {
        var result = _guard.ValidateQuery(sql);
        Assert.True(result.IsAllowed, result.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   -- only a comment")]
    [InlineData("DELETE FROM dbo.Orders")]
    [InlineData("UPDATE dbo.Orders SET Total = 0")]
    [InlineData("INSERT INTO dbo.Orders (Id) VALUES (1)")]
    [InlineData("DROP TABLE dbo.Orders")]
    [InlineData("ALTER TABLE dbo.Orders ADD x INT")]
    [InlineData("CREATE TABLE dbo.Evil (Id INT)")]
    [InlineData("TRUNCATE TABLE dbo.Orders")]
    [InlineData("EXEC sp_who2")]
    [InlineData("SELECT * FROM dbo.Orders; DROP TABLE dbo.Orders")]
    [InlineData("SELECT * INTO dbo.CopyOrders FROM dbo.Orders")]
    [InlineData("SELECT * FROM sys.sql_logins")]
    [InlineData("SELECT * FROM GuildhallDev.dbo.ChatRuns")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=x', 'SELECT 1')")]
    public void BlocksUnsafeOrMutatingQueries(string sql)
    {
        var result = _guard.ValidateQuery(sql);
        Assert.False(result.IsAllowed);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData("GuildhallDev", false)]
    [InlineData("guildhalldev", false)]
    [InlineData("", true)]
    [InlineData("*", true)]
    [InlineData("HomeAnalytics", true)]
    public void ValidateDatabaseTargetProtectsGuildhallDev(string dbName, bool expectedAllowed)
    {
        var result = _guard.ValidateDatabaseTarget(dbName);
        Assert.Equal(expectedAllowed, result.IsAllowed);
    }
}
