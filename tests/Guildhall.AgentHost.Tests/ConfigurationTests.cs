using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public void AllowedOriginsIncludesCustomLoopbackFrontendPort()
    {
        var origins = LocalConfiguration.AllowedOrigins("http://127.0.0.1:1425");
        Assert.Contains("http://127.0.0.1:1420", origins);
        Assert.Contains("http://127.0.0.1:1425", origins);
    }

    [Fact]
    public void ServiceProviderResolvesTimeProviderAndLazyServices()
    {
        using var app = new ApiFactory();
        Assert.Same(TimeProvider.System, app.Services.GetRequiredService<TimeProvider>());
        var lazyReadiness = app.Services.GetRequiredService<Lazy<IDatabaseReadiness>>();
        Assert.False(lazyReadiness.IsValueCreated);
        Assert.NotNull(lazyReadiness.Value);
        Assert.True(lazyReadiness.IsValueCreated);
    }
}
