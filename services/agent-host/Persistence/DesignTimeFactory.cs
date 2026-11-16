using Guildhall.AgentHost.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Guildhall.AgentHost.Persistence;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<GuildhallDbContext>
{
    public GuildhallDbContext CreateDbContext(string[] args)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ConnectionStrings__GuildhallMigrations")
            ?? "Server=localhost;Database=GuildhallDev;Integrated Security=true");
        LocalConfiguration.ValidateSql(connection);
        if (!connection.IntegratedSecurity) throw new InvalidOperationException("Migrations require the Windows identity.");
        return new GuildhallDbContext(new DbContextOptionsBuilder<GuildhallDbContext>()
            .UseSqlServer(connection.ConnectionString, sql => sql.CommandTimeout(10)).Options);
    }
}
