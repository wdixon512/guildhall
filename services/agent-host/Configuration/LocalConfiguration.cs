using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Guildhall.AgentHost.Configuration;

internal sealed class Lazier<T>(IServiceProvider provider)
    : Lazy<T>(provider.GetRequiredService<T>, LazyThreadSafetyMode.ExecutionAndPublication)
    where T : class;

public static class LocalConfiguration
{
    public static readonly string[] Origins = ["http://127.0.0.1:1420", "http://tauri.localhost", "tauri://localhost"];

    public static string[] AllowedOrigins(string? frontendUrl = null)
    {
        if (string.IsNullOrWhiteSpace(frontendUrl)) return Origins;
        var uri = LoopbackUri(frontendUrl);
        var origin = $"http://{uri.Host}:{uri.Port}";
        return Origins.Contains(origin, StringComparer.Ordinal) ? Origins : [.. Origins, origin];
    }

    public static Uri LoopbackUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            !IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address) ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("A numeric loopback HTTP base URL is required.");
        return uri;
    }

    public static string? RuntimeConnection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var connection = new SqlConnectionStringBuilder(value);
        ValidateSql(connection);
        if (connection.IntegratedSecurity || string.IsNullOrWhiteSpace(connection.UserID) || string.IsNullOrWhiteSpace(connection.Password))
            throw new InvalidOperationException("Runtime SQL must use its dedicated SQL login.");
        return connection.ConnectionString;
    }

    public static void ValidateSql(SqlConnectionStringBuilder connection)
    {
        if (!string.Equals(connection.DataSource, "localhost", StringComparison.OrdinalIgnoreCase) ||
            !(connection.InitialCatalog == "GuildhallDev" || connection.InitialCatalog.StartsWith("GuildhallTest_", StringComparison.Ordinal)))
            throw new InvalidOperationException("Only the approved local Guildhall SQL targets are allowed in Phase 0.");
        connection.Encrypt = SqlConnectionEncryptOption.Mandatory;
        connection.TrustServerCertificate = true; // Explicit, approved local-instance exception only.
        connection.ConnectTimeout = 3;
    }
}
