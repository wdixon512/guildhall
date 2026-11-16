using Guildhall.AgentHost.Configuration;
using Microsoft.Data.SqlClient;

namespace Guildhall.AgentHost.Tests;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("Server=remote.example;Database=GuildhallDev;User ID=runtime;Password=test")]
    [InlineData("Server=localhost;Database=master;User ID=runtime;Password=test")]
    [InlineData("Server=localhost;Database=GuildhallDev;Integrated Security=true")]
    public void LocalAuthRejectsUnapprovedSqlTargetsAndRuntimeWindowsIdentity(string connection) =>
        Assert.Throws<InvalidOperationException>(() => LocalConfiguration.RuntimeConnection(connection));

    [Fact]
    public void LocalSqlExceptionIsExplicitAndStillEncrypted()
    {
        var value = LocalConfiguration.RuntimeConnection("Server=localhost;Database=GuildhallDev;User ID=runtime;Password=test;Encrypt=false");
        var connection = new SqlConnectionStringBuilder(value);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, connection.Encrypt);
        Assert.True(connection.TrustServerCertificate);
        Assert.False(connection.IntegratedSecurity);
    }
}
