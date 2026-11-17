using System.Data;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Mcp.Providers;

public sealed class SqlServerMcpProvider(
    IDbContextFactory<GuildhallDbContext> dbFactory,
    ICredentialStore credentialStore,
    SqlReadOnlyGuard readOnlyGuard,
    ExfiltrationGuard exfiltrationGuard) : IMcpToolProvider
{
    public const string ProviderId = "sql-server-mcp";

    private static readonly IReadOnlyList<McpToolDescriptor> Tools =
    [
        CreateDescriptor(
            "sql_list_profiles",
            "List configured read-only SQL Server connection profiles (never returns credentials).",
            ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{},"required":[]}"""),
        CreateDescriptor(
            "sql_list_databases",
            "List online user databases available to a SQL MCP profile (excludes protected GuildhallDev).",
            ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{"profileName":{"type":"string","description":"Name of the configured SQL MCP profile."}},"required":["profileName"]}"""),
        CreateDescriptor(
            "sql_list_objects",
            "List authorized read-only SQL views and tables for a SQL MCP profile.",
            ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{"profileName":{"type":"string","description":"Name of the configured SQL MCP profile."},"database":{"type":"string","description":"Target database name (required when profile is server-wide)."},"objectType":{"type":"string","enum":["all","view","table"],"description":"Filter by VIEW, BASE TABLE, or all (default all)."}},"required":["profileName"]}"""),
        CreateDescriptor(
            "sql_describe_object",
            "Inspect columns, data types, and nullability of an authorized SQL view or table.",
            ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{"profileName":{"type":"string"},"database":{"type":"string","description":"Target database name (required when profile is server-wide)."},"schemaName":{"type":"string","description":"Schema name (default dbo)."},"objectName":{"type":"string","description":"View or table name."}},"required":["profileName","objectName"]}"""),
        CreateDescriptor(
            "sql_query",
            "Execute a bounded read-only SELECT or WITH query against an authorized SQL MCP profile.",
            ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{"profileName":{"type":"string"},"database":{"type":"string","description":"Optional target database name when profile is server-wide."},"sql":{"type":"string","description":"Single-statement SELECT or WITH query."},"maxRows":{"type":"integer","description":"Optional row cap (clamped to profile MaxRows)."}},"required":["profileName","sql"]}"""),
    ];

    public string ServerId => ProviderId;
    public string DisplayName => "SQL Server MCP (Read-Only)";
    public string Transport => "builtin_sql";
    public string DefaultSecurityTier => ToolSecurityTiers.Tier1;

    public IReadOnlyList<McpToolDescriptor> GetTools() => Tools;

    public async Task<(bool Healthy, string? Detail)> ProbeAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var profiles = await db.SqlMcpProfiles.AsNoTracking()
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
            return (false, "No read-only SQL Server profiles configured yet.");

        var healthyCount = 0;
        string? lastError = null;

        foreach (var profile in profiles)
        {
            var (ok, error) = await ProbeProfileAsync(profile, cancellationToken);
            if (ok) healthyCount++;
            else lastError = $"{profile.Name}: {error}";
        }

        if (healthyCount == profiles.Count)
            return (true, $"{healthyCount} read-only SQL profile(s) connected");

        if (healthyCount > 0)
            return (true, $"{healthyCount}/{profiles.Count} SQL profiles healthy ({lastError})");

        return (false, lastError ?? "SQL Server MCP probe failed.");
    }

    public async Task<(bool Healthy, string? Detail)> ProbeProfileAsync(SqlMcpProfile profile, CancellationToken cancellationToken)
    {
        var dbCheck = readOnlyGuard.ValidateDatabaseTarget(profile.Database);
        if (!dbCheck.IsAllowed)
            return (false, dbCheck.Reason);

        try
        {
            var connStr = await BuildConnectionStringAsync(profile, databaseOverride: null, connectTimeoutSeconds: 3, cancellationToken);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand("SELECT DB_NAME(), @@VERSION;", conn) { CommandTimeout = 3 };
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var scopeLabel = string.IsNullOrWhiteSpace(profile.Database)
                    ? "all user databases (excluding GuildhallDev)"
                    : $"[{(reader.IsDBNull(0) ? profile.Database : reader.GetString(0))}]";
                return (true, $"Connected read-only to {scopeLabel} on {profile.Server}");
            }
            return (true, $"Connected to {profile.Server}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, exfiltrationGuard.RedactSecrets(ex.Message));
        }
    }

    public async Task<McpToolCallResult> InvokeAsync(McpToolCallRequest request, CancellationToken cancellationToken)
    {
        if (request.ToolName == "sql_list_profiles")
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var profiles = await db.SqlMcpProfiles.AsNoTracking()
                .Where(x => x.IsEnabled)
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    x.Name,
                    x.Server,
                    x.Database,
                    x.AuthMode,
                    AllowedSchemas = DeserializeList(x.AllowedSchemasJson),
                    AllowedObjects = DeserializeList(x.AllowedObjectsJson),
                    x.MaxRows,
                    x.CommandTimeoutSeconds,
                })
                .ToListAsync(cancellationToken);

            return new McpToolCallResult(false, JsonSerializer.Serialize(new { profiles }), null);
        }

        var profileName = GetString(request.Arguments, "profileName");
        if (string.IsNullOrWhiteSpace(profileName))
            return new McpToolCallResult(true, "Missing required 'profileName' argument.", "invalid_arguments");

        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken);
        var profile = await context.SqlMcpProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Name == profileName.Trim() && x.IsEnabled, cancellationToken);

        if (profile is null)
            return new McpToolCallResult(true, $"Enabled SQL MCP profile '{profileName}' was not found.", "profile_not_found");

        var dbValidation = readOnlyGuard.ValidateDatabaseTarget(profile.Database);
        if (!dbValidation.IsAllowed)
            return new McpToolCallResult(true, dbValidation.Reason!, "protected_database_blocked");

        return request.ToolName switch
        {
            "sql_list_databases" => await ListDatabasesAsync(profile, cancellationToken),
            "sql_list_objects" => await ListObjectsAsync(profile, request.Arguments, cancellationToken),
            "sql_describe_object" => await DescribeObjectAsync(profile, request.Arguments, cancellationToken),
            "sql_query" => await ExecuteQueryAsync(profile, request.Arguments, cancellationToken),
            _ => new McpToolCallResult(true, $"Unknown SQL MCP tool '{request.ToolName}'.", "tool_not_found")
        };
    }

    private async Task<McpToolCallResult> ListDatabasesAsync(
        SqlMcpProfile profile,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(profile.Database))
        {
            return new McpToolCallResult(
                false,
                JsonSerializer.Serialize(new { profile = profile.Name, databases = new[] { profile.Database } }),
                null);
        }

        var connStr = await BuildConnectionStringAsync(profile, databaseOverride: null, connectTimeoutSeconds: 5, cancellationToken);
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(cancellationToken);

        const string sql = """
            SELECT name
            FROM sys.databases
            WHERE database_id > 4 AND state_desc = 'ONLINE' AND name <> 'GuildhallDev'
            ORDER BY name;
            """;

        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = profile.CommandTimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var databases = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
            databases.Add(reader.GetString(0));

        return new McpToolCallResult(false, JsonSerializer.Serialize(new { profile = profile.Name, databases }), null);
    }

    private async Task<McpToolCallResult> ListObjectsAsync(
        SqlMcpProfile profile,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var targetDb = ResolveTargetDatabase(profile, arguments, requireForServerProfile: true, out var dbError);
        if (dbError is not null)
            return dbError;

        var objectType = GetString(arguments, "objectType") ?? "all";
        var allowedSchemas = DeserializeList(profile.AllowedSchemasJson);
        var allowedObjects = DeserializeList(profile.AllowedObjectsJson);

        var connStr = await BuildConnectionStringAsync(profile, targetDb, connectTimeoutSeconds: 5, cancellationToken);
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(cancellationToken);

        const string sql = """
            SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW')
            ORDER BY TABLE_SCHEMA, TABLE_NAME;
            """;

        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = profile.CommandTimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var items = new List<object>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var schema = reader.GetString(0);
            var name = reader.GetString(1);
            var kind = reader.GetString(2);

            if (!IsObjectAuthorized(schema, name, allowedSchemas, allowedObjects))
                continue;

            if (string.Equals(objectType, "view", StringComparison.OrdinalIgnoreCase) && kind != "VIEW")
                continue;
            if (string.Equals(objectType, "table", StringComparison.OrdinalIgnoreCase) && kind != "BASE TABLE")
                continue;

            items.Add(new { schema, name, type = kind == "VIEW" ? "view" : "table" });
        }

        return new McpToolCallResult(false, JsonSerializer.Serialize(new { profile = profile.Name, database = targetDb, objects = items }), null);
    }

    private async Task<McpToolCallResult> DescribeObjectAsync(
        SqlMcpProfile profile,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var targetDb = ResolveTargetDatabase(profile, arguments, requireForServerProfile: true, out var dbError);
        if (dbError is not null)
            return dbError;

        var schemaName = GetString(arguments, "schemaName") ?? "dbo";
        var objectName = GetString(arguments, "objectName");
        if (string.IsNullOrWhiteSpace(objectName))
            return new McpToolCallResult(true, "Missing required 'objectName' argument.", "invalid_arguments");

        var allowedSchemas = DeserializeList(profile.AllowedSchemasJson);
        var allowedObjects = DeserializeList(profile.AllowedObjectsJson);
        if (!IsObjectAuthorized(schemaName, objectName, allowedSchemas, allowedObjects))
            return new McpToolCallResult(true, $"Object '{schemaName}.{objectName}' is not in this profile's allowlist.", "object_not_authorized");

        var connStr = await BuildConnectionStringAsync(profile, targetDb, connectTimeoutSeconds: 5, cancellationToken);
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(cancellationToken);

        const string sql = """
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH, ORDINAL_POSITION
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @name
            ORDER BY ORDINAL_POSITION;
            """;

        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = profile.CommandTimeoutSeconds };
        cmd.Parameters.AddWithValue("@schema", schemaName.Trim());
        cmd.Parameters.AddWithValue("@name", objectName.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var columns = new List<object>();
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new
            {
                name = reader.GetString(0),
                dataType = reader.GetString(1),
                isNullable = string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                maxLength = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                ordinal = reader.GetInt32(4),
            });
        }

        if (columns.Count == 0)
            return new McpToolCallResult(true, $"Object '{schemaName}.{objectName}' was not found.", "object_not_found");

        return new McpToolCallResult(false, JsonSerializer.Serialize(new { profile = profile.Name, schema = schemaName, objectName, columns }), null);
    }

    private async Task<McpToolCallResult> ExecuteQueryAsync(
        SqlMcpProfile profile,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var targetDb = ResolveTargetDatabase(profile, arguments, requireForServerProfile: false, out var dbError);
        if (dbError is not null)
            return dbError;

        var sql = GetString(arguments, "sql");
        var allowedSchemas = DeserializeList(profile.AllowedSchemasJson);
        var validation = readOnlyGuard.ValidateQuery(sql, allowedSchemas);
        if (!validation.IsAllowed)
            return new McpToolCallResult(true, validation.Reason!, "sql_readonly_violation");

        var requestedMaxRows = TryGetInt(arguments, "maxRows", out var parsedMax) ? parsedMax : profile.MaxRows;
        var effectiveMaxRows = Math.Clamp(requestedMaxRows, 1, Math.Clamp(profile.MaxRows, 1, 500));
        var timeoutSeconds = Math.Clamp(profile.CommandTimeoutSeconds, 1, 30);

        var connStr = await BuildConnectionStringAsync(profile, targetDb, connectTimeoutSeconds: 5, cancellationToken);
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(cancellationToken);

        await using (var preamble = new SqlCommand($"SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SET ROWCOUNT {effectiveMaxRows};", conn))
        {
            preamble.CommandTimeout = timeoutSeconds;
            await preamble.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var cmd = new SqlCommand(sql!, conn) { CommandTimeout = timeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);

        var rows = new List<Dictionary<string, object?>>();
        var columnNames = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();

        while (rows.Count < effectiveMaxRows && await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(columnNames.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columnNames.Length; i++)
            {
                if (await reader.IsDBNullAsync(i, cancellationToken))
                {
                    row[columnNames[i]] = null;
                }
                else
                {
                    var val = reader.GetValue(i);
                    row[columnNames[i]] = val is byte[] bytes ? $"<binary {bytes.Length} bytes>" : val;
                }
            }
            rows.Add(row);
        }

        var json = JsonSerializer.Serialize(new
        {
            profile = profile.Name,
            database = targetDb ?? "(server-wide)",
            rowCount = rows.Count,
            maxRowsApplied = effectiveMaxRows,
            rows
        });

        return new McpToolCallResult(false, exfiltrationGuard.TruncateAndRedact(json), null);
    }

    private string? ResolveTargetDatabase(
        SqlMcpProfile profile,
        JsonElement arguments,
        bool requireForServerProfile,
        out McpToolCallResult? error)
    {
        error = null;
        var requestedDb = GetString(arguments, "database")?.Trim();

        if (!string.IsNullOrWhiteSpace(profile.Database))
        {
            if (!string.IsNullOrWhiteSpace(requestedDb) &&
                !string.Equals(requestedDb, profile.Database, StringComparison.OrdinalIgnoreCase))
            {
                error = new McpToolCallResult(true, $"Profile '{profile.Name}' is pinned to database '{profile.Database}'.", "database_not_authorized");
                return null;
            }
            return profile.Database;
        }

        if (string.IsNullOrWhiteSpace(requestedDb))
        {
            if (requireForServerProfile)
            {
                error = new McpToolCallResult(true, "Specify 'database' for this server-wide SQL profile (call sql_list_databases first).", "invalid_arguments");
                return null;
            }
            return null;
        }

        var dbCheck = readOnlyGuard.ValidateDatabaseTarget(requestedDb);
        if (!dbCheck.IsAllowed)
        {
            error = new McpToolCallResult(true, dbCheck.Reason!, "protected_database_blocked");
            return null;
        }

        return requestedDb;
    }

    private async Task<string> BuildConnectionStringAsync(
        SqlMcpProfile profile,
        string? databaseOverride,
        int connectTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var initialCatalog = !string.IsNullOrWhiteSpace(databaseOverride)
            ? databaseOverride
            : !string.IsNullOrWhiteSpace(profile.Database)
                ? profile.Database
                : "master";

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.Server,
            InitialCatalog = initialCatalog,
            ApplicationIntent = ApplicationIntent.ReadOnly,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = true,
            ConnectTimeout = connectTimeoutSeconds,
            Pooling = false,
        };

        if (string.Equals(profile.AuthMode, "sql_login", StringComparison.OrdinalIgnoreCase))
        {
            var secretJson = await credentialStore.GetApiKeyAsync(GetCredentialKey(profile.Id), cancellationToken);
            if (string.IsNullOrWhiteSpace(secretJson))
                throw new InvalidOperationException($"SQL login credentials for profile '{profile.Name}' are missing from DPAPI storage.");

            using var doc = JsonDocument.Parse(secretJson);
            var username = doc.RootElement.TryGetProperty("username", out var u) ? u.GetString() : null;
            var password = doc.RootElement.TryGetProperty("password", out var p) ? p.GetString() : null;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException($"SQL login credentials for profile '{profile.Name}' are incomplete.");

            builder.IntegratedSecurity = false;
            builder.UserID = username;
            builder.Password = password;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    public static string GetCredentialKey(Guid profileId) => $"sql-mcp-{profileId:N}";

    private static bool IsObjectAuthorized(
        string schema,
        string objectName,
        IReadOnlyList<string> allowedSchemas,
        IReadOnlyList<string> allowedObjects)
    {
        if (allowedSchemas.Count > 0 && !allowedSchemas.Contains(schema, StringComparer.OrdinalIgnoreCase))
            return false;

        if (allowedObjects.Count == 0)
            return true;

        var qualified = $"{schema}.{objectName}";
        return allowedObjects.Contains(qualified, StringComparer.OrdinalIgnoreCase) ||
               allowedObjects.Contains(objectName, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static McpToolDescriptor CreateDescriptor(string name, string description, string tier, string schemaJson)
    {
        using var doc = JsonDocument.Parse(schemaJson);
        return new McpToolDescriptor(ProviderId, name, description, tier, doc.RootElement.Clone());
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var prop) &&
        prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var prop) &&
               prop.ValueKind == JsonValueKind.Number &&
               prop.TryGetInt32(out value);
    }
}
