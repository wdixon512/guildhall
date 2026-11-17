using System.Text;
using System.Text.Json;
using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Mcp.Providers;

public sealed class LocalFilesystemMcpProvider(WorkspacePathGuard pathGuard) : IMcpToolProvider
{
    private const int MaxListEntries = 200;
    private const int MaxReadBytes = 64 * 1024;
    private const int MaxSearchMatches = 50;

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", "dist", "target",
    };

    private static readonly IReadOnlyList<McpToolDescriptor> Tools =
    [
        CreateDescriptor(
            "fs_list_directory",
            "List files and subdirectories inside the authorized workspace folder (read-only).",
            """{"type":"object","properties":{"path":{"type":"string","description":"Relative directory path inside workspace (default '.')."}},"required":[]}"""),
        CreateDescriptor(
            "fs_read_file",
            "Read a UTF-8 text file inside the authorized workspace folder (read-only, capped at 64 KiB).",
            """{"type":"object","properties":{"path":{"type":"string","description":"Relative file path inside workspace."},"startLine":{"type":"integer","description":"Optional 1-based start line."},"endLine":{"type":"integer","description":"Optional 1-based inclusive end line."}},"required":["path"]}"""),
        CreateDescriptor(
            "fs_search_files",
            "Search for a plain case-insensitive substring (not regex) across text files in the authorized workspace folder (read-only).",
            """{"type":"object","properties":{"pattern":{"type":"string","description":"Plain substring to search for (do not regex-escape dots or slashes)."},"subPath":{"type":"string","description":"Optional directory relative to workspace root (e.g. 'docs/masterplan'; default '.')."}},"required":["pattern"]}"""),
    ];

    public string ServerId => "filesystem-mcp";
    public string DisplayName => "Local Filesystem (Read-Only)";
    public string Transport => "builtin";
    public string DefaultSecurityTier => ToolSecurityTiers.Tier1;

    public IReadOnlyList<McpToolDescriptor> GetTools() => Tools;

    public Task<(bool Healthy, string? Detail)> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult<(bool, string?)>((true, "Built-in read-only filesystem provider ready"));

    public Task<McpToolCallResult> InvokeAsync(McpToolCallRequest request, CancellationToken cancellationToken)
    {
        return request.ToolName switch
        {
            "fs_list_directory" => Task.FromResult(ListDirectory(request)),
            "fs_read_file" => ReadFileAsync(request, cancellationToken),
            "fs_search_files" => Task.FromResult(SearchFiles(request, cancellationToken)),
            _ => Task.FromResult(new McpToolCallResult(true, $"Unknown tool '{request.ToolName}'.", "tool_not_found")),
        };
    }

    private McpToolCallResult ListDirectory(McpToolCallRequest request)
    {
        var requestedPath = GetOptionalString(request.Arguments, "path") ?? ".";
        var check = pathGuard.ValidateReadPath(request.WorkspaceRootPath, requestedPath);
        if (!check.IsAllowed || check.FullPath is null)
            return new McpToolCallResult(true, check.ErrorMessage ?? "Path rejected.", check.ErrorCode);

        if (!Directory.Exists(check.FullPath))
            return new McpToolCallResult(true, "Directory not found.", "directory_not_found");

        var rootFull = Path.GetFullPath(request.WorkspaceRootPath!);
        var entries = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(check.FullPath).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            if (SkippedDirectories.Contains(name) || WorkspacePathGuard.IsSensitivePath(dir)) continue;
            entries.Add($"[dir]  {Path.GetRelativePath(rootFull, dir).Replace('\\', '/')}/");
            if (entries.Count >= MaxListEntries) break;
        }

        if (entries.Count < MaxListEntries)
        {
            foreach (var file in Directory.EnumerateFiles(check.FullPath).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (WorkspacePathGuard.IsSensitivePath(file)) continue;
                var info = new FileInfo(file);
                entries.Add($"[file] {Path.GetRelativePath(rootFull, file).Replace('\\', '/')} ({info.Length} B)");
                if (entries.Count >= MaxListEntries) break;
            }
        }

        return new McpToolCallResult(false, entries.Count == 0 ? "(empty directory)" : string.Join("\n", entries));
    }

    private async Task<McpToolCallResult> ReadFileAsync(McpToolCallRequest request, CancellationToken cancellationToken)
    {
        var requestedPath = GetOptionalString(request.Arguments, "path");
        if (string.IsNullOrWhiteSpace(requestedPath))
            return new McpToolCallResult(true, "Missing required 'path' argument.", "invalid_arguments");

        var check = pathGuard.ValidateReadPath(request.WorkspaceRootPath, requestedPath);
        if (!check.IsAllowed || check.FullPath is null)
            return new McpToolCallResult(true, check.ErrorMessage ?? "Path rejected.", check.ErrorCode);

        if (!File.Exists(check.FullPath))
            return new McpToolCallResult(true, "File not found.", "file_not_found");

        var startLine = GetOptionalInt(request.Arguments, "startLine");
        var endLine = GetOptionalInt(request.Arguments, "endLine");

        var lines = await File.ReadAllLinesAsync(check.FullPath, Encoding.UTF8, cancellationToken);
        var from = Math.Max(1, startLine ?? 1);
        var to = Math.Min(lines.Length, endLine ?? lines.Length);

        var sb = new StringBuilder();
        for (var i = from; i <= to; i++)
        {
            var line = $"{i}: {lines[i - 1]}";
            if (sb.Length + line.Length > MaxReadBytes)
            {
                sb.AppendLine("\n… [truncated at 64 KiB]");
                break;
            }
            sb.AppendLine(line);
        }

        return new McpToolCallResult(false, sb.Length == 0 ? "(empty file)" : sb.ToString());
    }

    private McpToolCallResult SearchFiles(McpToolCallRequest request, CancellationToken cancellationToken)
    {
        var pattern = GetOptionalString(request.Arguments, "pattern");
        if (string.IsNullOrWhiteSpace(pattern))
            return new McpToolCallResult(true, "Missing required 'pattern' argument.", "invalid_arguments");

        var subPath = GetOptionalString(request.Arguments, "subPath") ?? ".";
        var check = pathGuard.ValidateReadPath(request.WorkspaceRootPath, subPath);
        if (!check.IsAllowed || check.FullPath is null)
            return new McpToolCallResult(true, check.ErrorMessage ?? "Path rejected.", check.ErrorCode);

        if (!Directory.Exists(check.FullPath))
            return new McpToolCallResult(true, "Search directory not found.", "directory_not_found");

        var rootFull = Path.GetFullPath(request.WorkspaceRootPath!);
        var matches = new List<string>();
        var stack = new Stack<string>();
        stack.Push(check.FullPath);

        while (stack.Count > 0 && matches.Count < MaxSearchMatches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (SkippedDirectories.Contains(name)) continue;
                var attrs = File.GetAttributes(sub);
                if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                stack.Push(sub);
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (matches.Count >= MaxSearchMatches) break;
                if (WorkspacePathGuard.IsSensitivePath(file)) continue;
                var info = new FileInfo(file);
                if (info.Length > 512 * 1024) continue;

                try
                {
                    var lineNum = 0;
                    foreach (var line in File.ReadLines(file, Encoding.UTF8))
                    {
                        lineNum++;
                        if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        {
                            var rel = Path.GetRelativePath(rootFull, file).Replace('\\', '/');
                            var snippet = line.Length > 160 ? line[..160] + "…" : line;
                            matches.Add($"{rel}:{lineNum}: {snippet}");
                            if (matches.Count >= MaxSearchMatches) break;
                        }
                    }
                }
                catch
                {
                    // Skip unreadable/binary files.
                }
            }
        }

        return new McpToolCallResult(false, matches.Count == 0 ? "No matches found." : string.Join("\n", matches));
    }

    private static string? GetOptionalString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static int? GetOptionalInt(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var prop) && prop.TryGetInt32(out var value)
            ? value
            : null;

    private static McpToolDescriptor CreateDescriptor(string name, string description, string schemaJson) =>
        new("filesystem-mcp", name, description, ToolSecurityTiers.Tier1, JsonDocument.Parse(schemaJson).RootElement.Clone());
}
