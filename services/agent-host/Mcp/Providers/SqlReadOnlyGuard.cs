using System.Text;
using System.Text.RegularExpressions;

namespace Guildhall.AgentHost.Mcp.Providers;

public sealed record SqlValidationResult(bool IsAllowed, string? Reason);

public sealed partial class SqlReadOnlyGuard
{
    private static readonly string[] BlockedKeywords =
    [
        "INSERT", "UPDATE", "DELETE", "MERGE", "DROP", "ALTER", "CREATE", "TRUNCATE",
        "EXEC", "EXECUTE", "INTO", "GRANT", "REVOKE", "DENY", "BACKUP", "RESTORE",
        "DBCC", "OPENROWSET", "OPENDATASOURCE", "OPENQUERY", "BULK", "WAITFOR",
        "SHUTDOWN", "KILL", "RECONFIGURE", "WRITETEXT", "UPDATETEXT", "READTEXT"
    ];

    private static readonly string[] BlockedDatabases = ["GuildhallDev"];

    [GeneratedRegex(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();

    public SqlValidationResult ValidateDatabaseTarget(string? databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName) || string.Equals(databaseName.Trim(), "*", StringComparison.Ordinal))
            return new SqlValidationResult(true, null);

        var trimmed = databaseName.Trim();
        if (BlockedDatabases.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return new SqlValidationResult(false, $"Database '{trimmed}' is protected and cannot be targeted by SQL Server MCP.");

        return new SqlValidationResult(true, null);
    }

    public SqlValidationResult ValidateQuery(string? sql, IReadOnlyCollection<string>? allowedSchemas = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return new SqlValidationResult(false, "SQL query cannot be empty.");

        var stripped = StripCommentsAndLiterals(sql).Trim();
        if (string.IsNullOrWhiteSpace(stripped))
            return new SqlValidationResult(false, "SQL query contains no executable tokens.");

        // Reject multi-statement batches (any semicolon other than a single trailing semicolon).
        var withoutTrailingSemicolon = stripped.TrimEnd();
        if (withoutTrailingSemicolon.EndsWith(';'))
            withoutTrailingSemicolon = withoutTrailingSemicolon[..^1].TrimEnd();

        if (withoutTrailingSemicolon.Contains(';'))
            return new SqlValidationResult(false, "Multi-statement SQL batches are not permitted.");

        var tokens = IdentifierRegex()
            .Matches(withoutTrailingSemicolon)
            .Select(m => m.Value)
            .ToArray();

        if (tokens.Length == 0)
            return new SqlValidationResult(false, "SQL query contains no valid SQL statement.");

        var firstToken = tokens[0].ToUpperInvariant();
        if (firstToken is not ("SELECT" or "WITH"))
            return new SqlValidationResult(false, "Only single-statement SELECT or WITH (CTE) read-only queries are permitted.");

        foreach (var token in tokens)
        {
            var upper = token.ToUpperInvariant();
            if (BlockedKeywords.Contains(upper, StringComparer.Ordinal))
                return new SqlValidationResult(false, $"Keyword '{upper}' is blocked in read-only SQL Server MCP.");

            if (upper.StartsWith("XP_", StringComparison.Ordinal) || upper.StartsWith("SP_", StringComparison.Ordinal))
                return new SqlValidationResult(false, $"Stored procedure prefix '{token}' is blocked in read-only SQL Server MCP.");

            if (string.Equals(upper, "SQL_LOGINS", StringComparison.Ordinal) ||
                string.Equals(upper, "SYSLOGINS", StringComparison.Ordinal) ||
                string.Equals(upper, "CREDENTIALS", StringComparison.Ordinal))
                return new SqlValidationResult(false, $"System security view '{token}' is blocked.");
        }

        // Reject SELECT ... INTO ... (which creates a table) even if INTO wasn't caught above.
        if (tokens.Any(t => string.Equals(t, "INTO", StringComparison.OrdinalIgnoreCase)))
            return new SqlValidationResult(false, "SELECT INTO is not permitted.");

        // Reject cross-database three-part names referencing GuildhallDev.
        if (withoutTrailingSemicolon.Contains("GuildhallDev", StringComparison.OrdinalIgnoreCase))
            return new SqlValidationResult(false, "References to protected database 'GuildhallDev' are not permitted.");

        return new SqlValidationResult(true, null);
    }

    public static string StripCommentsAndLiterals(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var i = 0;
        while (i < sql.Length)
        {
            // Line comment --
            if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            {
                i += 2;
                while (i < sql.Length && sql[i] != '\n' && sql[i] != '\r')
                    i++;
                sb.Append(' ');
                continue;
            }

            // Block comment /* ... */
            if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                    i++;
                i = Math.Min(i + 2, sql.Length);
                sb.Append(' ');
                continue;
            }

            // Single-quoted string literal '...' (handling escaped '')
            if (sql[i] == '\'')
            {
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '\'')
                    {
                        i++;
                        break;
                    }
                    i++;
                }
                sb.Append(" '' ");
                continue;
            }

            sb.Append(sql[i]);
            i++;
        }

        return sb.ToString();
    }
}
