namespace Guildhall.AgentHost.Security;

public sealed record PathValidationResult(
    bool IsAllowed,
    string? FullPath,
    string? ErrorCode,
    string? ErrorMessage);

public sealed class WorkspacePathGuard
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static readonly string[] BlockedExactNames =
    [
        ".env",
        "runtime.credential.xml",
        "secrets.json",
        ".npmrc",
        ".pypirc",
        ".netrc",
        "id_rsa",
        "id_ed25519",
    ];

    private static readonly string[] BlockedExtensions =
    [
        ".dpapi",
        ".pfx",
        ".pem",
        ".key",
        ".p12",
        ".kdbx",
    ];

    public PathValidationResult ValidateReadPath(string? workspaceRoot, string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return new PathValidationResult(
                false,
                null,
                "workspace_not_configured",
                "No authorized workspace folder is configured for this conversation.");
        }

        var candidate = string.IsNullOrWhiteSpace(requestedPath) ? "." : requestedPath.Trim();
        if (candidate.Contains('\0') ||
            candidate.StartsWith(@"\\", StringComparison.Ordinal) ||
            candidate.StartsWith("//", StringComparison.Ordinal))
        {
            return new PathValidationResult(
                false,
                null,
                "path_outside_workspace",
                "UNC, device, or malformed paths are not permitted.");
        }

        string normalizedRoot;
        string fullTarget;
        try
        {
            normalizedRoot = Path.GetFullPath(workspaceRoot.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            fullTarget = Path.GetFullPath(candidate, normalizedRoot);
        }
        catch (Exception)
        {
            return new PathValidationResult(
                false,
                null,
                "path_outside_workspace",
                "The requested path is invalid.");
        }

        var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
        var insideRoot = string.Equals(fullTarget, normalizedRoot, PathComparison) ||
                         fullTarget.StartsWith(rootPrefix, PathComparison);
        if (!insideRoot)
        {
            return new PathValidationResult(
                false,
                null,
                "path_outside_workspace",
                "Access denied: path escapes the authorized workspace root.");
        }

        if (HasReparsePointInSubpath(normalizedRoot, fullTarget))
        {
            return new PathValidationResult(
                false,
                null,
                "path_outside_workspace",
                "Access denied: symbolic links or junctions inside the workspace are not followed.");
        }

        if (IsSensitivePath(fullTarget))
        {
            return new PathValidationResult(
                false,
                null,
                "sensitive_file_blocked",
                "Access denied: reading credential, key, or environment secret files is blocked.");
        }

        return new PathValidationResult(true, fullTarget, null, null);
    }

    public static bool IsSensitivePath(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(fileName)) return false;

        if (fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fileName.StartsWith("id_rsa.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("id_ed25519.", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var exact in BlockedExactNames)
        {
            if (string.Equals(fileName, exact, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var ext in BlockedExtensions)
        {
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var normalized = fullPath.Replace('\\', '/');
        if (normalized.EndsWith("/.git/credentials", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/.git/credentials/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool HasReparsePointInSubpath(string normalizedRoot, string fullTarget)
    {
        var current = fullTarget;
        while (current.Length > normalizedRoot.Length)
        {
            try
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    var attrs = File.GetAttributes(current);
                    if ((attrs & FileAttributes.ReparsePoint) != 0)
                        return true;
                }
            }
            catch
            {
                return true;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, PathComparison))
                break;
            current = parent;
        }

        return false;
    }
}
