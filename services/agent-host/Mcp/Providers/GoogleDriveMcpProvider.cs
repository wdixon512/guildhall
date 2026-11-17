using System.Net.Http.Headers;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Mcp.Providers;

public sealed class GoogleDriveMcpProvider(
    IHttpClientFactory httpClients,
    ICredentialStore credentialStore) : IMcpToolProvider
{
    private const int MaxReadChars = 64 * 1024;

    private static readonly IReadOnlyList<McpToolDescriptor> Tools =
    [
        CreateDescriptor("gdrive_search_files", "Search files in Google Drive (read-only).", ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{"query":{"type":"string","description":"Search term for file name."},"pageSize":{"type":"integer"}},"required":["query"]}"""),
        CreateDescriptor("gdrive_read_text_file", "Read or export a Google Drive text/Doc file (read-only, capped at 64 KiB).", ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{"fileId":{"type":"string"}},"required":["fileId"]}"""),
    ];

    public string ServerId => "google-drive-mcp";
    public string DisplayName => "Google Drive";
    public string Transport => "builtin_http";
    public string DefaultSecurityTier => ToolSecurityTiers.Tier1;

    public IReadOnlyList<McpToolDescriptor> GetTools() => Tools;

    public async Task<(bool Healthy, string? Detail)> ProbeAsync(CancellationToken cancellationToken)
    {
        var token = await credentialStore.GetApiKeyAsync(ServerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return (false, "Add a Google Drive OAuth access token (encrypted with Windows DPAPI)");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/drive/v3/about?fields=user(emailAddress)");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await httpClients.CreateClient("google-drive-api").SendAsync(req, cancellationToken);
        if (!res.IsSuccessStatusCode)
            return (false, $"Google Drive returned HTTP {(int)res.StatusCode}");

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var email = doc.RootElement.TryGetProperty("user", out var u) && u.TryGetProperty("emailAddress", out var e)
            ? e.GetString()
            : null;
        return (true, string.IsNullOrWhiteSpace(email) ? "Connected to Google Drive" : $"Signed in as {email}");
    }

    public async Task<McpToolCallResult> InvokeAsync(McpToolCallRequest request, CancellationToken cancellationToken)
    {
        var token = await credentialStore.GetApiKeyAsync(ServerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return new McpToolCallResult(true, "Google Drive token is not configured in Connections.", "provider_auth_failed");

        var client = httpClients.CreateClient("google-drive-api");
        switch (request.ToolName)
        {
            case "gdrive_search_files":
            {
                var query = GetString(request.Arguments, "query") ?? string.Empty;
                var pageSize = request.Arguments.ValueKind == JsonValueKind.Object &&
                               request.Arguments.TryGetProperty("pageSize", out var ps) &&
                               ps.TryGetInt32(out var val)
                    ? Math.Clamp(val, 1, 25)
                    : 10;
                var escaped = query.Replace("'", "\\'");
                var qParam = Uri.EscapeDataString($"name contains '{escaped}' and trashed = false");
                using var req = new HttpRequestMessage(HttpMethod.Get, $"/drive/v3/files?q={qParam}&pageSize={pageSize}&fields=files(id,name,mimeType,modifiedTime)");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var res = await client.SendAsync(req, cancellationToken);
                var body = await res.Content.ReadAsStringAsync(cancellationToken);
                return res.IsSuccessStatusCode
                    ? new McpToolCallResult(false, body)
                    : new McpToolCallResult(true, $"Google Drive error {(int)res.StatusCode}: {body}", "gdrive_api_error");
            }
            case "gdrive_read_text_file":
            {
                var fileId = GetString(request.Arguments, "fileId");
                if (string.IsNullOrWhiteSpace(fileId))
                    return new McpToolCallResult(true, "Missing required 'fileId'.", "invalid_arguments");

                var safeId = Uri.EscapeDataString(fileId);
                using var metaReq = new HttpRequestMessage(HttpMethod.Get, $"/drive/v3/files/{safeId}?fields=id,name,mimeType");
                metaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var metaRes = await client.SendAsync(metaReq, cancellationToken);
                if (!metaRes.IsSuccessStatusCode)
                    return new McpToolCallResult(true, $"Google Drive metadata error {(int)metaRes.StatusCode}.", "gdrive_api_error");

                using var metaDoc = await JsonDocument.ParseAsync(await metaRes.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                var mime = metaDoc.RootElement.TryGetProperty("mimeType", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                var path = mime.StartsWith("application/vnd.google-apps.document", StringComparison.OrdinalIgnoreCase)
                    ? $"/drive/v3/files/{safeId}/export?mimeType=text/plain"
                    : $"/drive/v3/files/{safeId}?alt=media";

                using var dlReq = new HttpRequestMessage(HttpMethod.Get, path);
                dlReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var dlRes = await client.SendAsync(dlReq, cancellationToken);
                var text = await dlRes.Content.ReadAsStringAsync(cancellationToken);
                if (!dlRes.IsSuccessStatusCode)
                    return new McpToolCallResult(true, $"Google Drive read error {(int)dlRes.StatusCode}: {text}", "gdrive_api_error");

                if (text.Length > MaxReadChars)
                    text = text[..MaxReadChars] + "\n… [truncated at 64 KiB]";
                return new McpToolCallResult(false, text);
            }
            default:
                return new McpToolCallResult(true, $"Unknown tool '{request.ToolName}'.", "tool_not_found");
        }
    }

    private static string? GetString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static McpToolDescriptor CreateDescriptor(string name, string description, string tier, string schemaJson) =>
        new("google-drive-mcp", name, description, tier, JsonDocument.Parse(schemaJson).RootElement.Clone());
}
