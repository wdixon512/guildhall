using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Mcp.Providers;

public sealed class GmailMcpProvider(
    IHttpClientFactory httpClients,
    ICredentialStore credentialStore) : IMcpToolProvider
{
    private static readonly IReadOnlyList<McpToolDescriptor> Tools =
    [
        CreateDescriptor("gmail_search_messages", "Search Gmail messages by query (read-only).", ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{"query":{"type":"string"},"maxResults":{"type":"integer"}},"required":["query"]}"""),
        CreateDescriptor("gmail_get_message", "Fetch metadata and snippet/body for a Gmail message ID (read-only).", ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{"messageId":{"type":"string"}},"required":["messageId"]}"""),
        CreateDescriptor("gmail_create_draft", "Create a Gmail draft (Tier 3: requires human approval; never auto-sends).", ToolSecurityTiers.Tier3,
            """{"type":"object","properties":{"to":{"type":"string"},"subject":{"type":"string"},"body":{"type":"string"}},"required":["to","subject","body"]}"""),
    ];

    public string ServerId => "gmail-mcp";
    public string DisplayName => "Gmail";
    public string Transport => "builtin_http";
    public string DefaultSecurityTier => ToolSecurityTiers.Tier1;

    public IReadOnlyList<McpToolDescriptor> GetTools() => Tools;

    public async Task<(bool Healthy, string? Detail)> ProbeAsync(CancellationToken cancellationToken)
    {
        var token = await credentialStore.GetApiKeyAsync(ServerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return (false, "Add a Gmail OAuth access token (encrypted with Windows DPAPI)");

        using var req = new HttpRequestMessage(HttpMethod.Get, "/gmail/v1/users/me/profile");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await httpClients.CreateClient("gmail-api").SendAsync(req, cancellationToken);
        if (!res.IsSuccessStatusCode)
            return (false, $"Gmail returned HTTP {(int)res.StatusCode}");

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var email = doc.RootElement.TryGetProperty("emailAddress", out var e) ? e.GetString() : null;
        return (true, string.IsNullOrWhiteSpace(email) ? "Connected to Gmail" : $"Signed in as {email}");
    }

    public async Task<McpToolCallResult> InvokeAsync(McpToolCallRequest request, CancellationToken cancellationToken)
    {
        var token = await credentialStore.GetApiKeyAsync(ServerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return new McpToolCallResult(true, "Gmail token is not configured in Connections.", "provider_auth_failed");

        var client = httpClients.CreateClient("gmail-api");
        switch (request.ToolName)
        {
            case "gmail_search_messages":
            {
                var query = GetString(request.Arguments, "query") ?? string.Empty;
                var maxResults = request.Arguments.ValueKind == JsonValueKind.Object &&
                                 request.Arguments.TryGetProperty("maxResults", out var mr) &&
                                 mr.TryGetInt32(out var val)
                    ? Math.Clamp(val, 1, 20)
                    : 10;
                using var req = new HttpRequestMessage(HttpMethod.Get, $"/gmail/v1/users/me/messages?q={Uri.EscapeDataString(query)}&maxResults={maxResults}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var res = await client.SendAsync(req, cancellationToken);
                var body = await res.Content.ReadAsStringAsync(cancellationToken);
                return res.IsSuccessStatusCode
                    ? new McpToolCallResult(false, body)
                    : new McpToolCallResult(true, $"Gmail error {(int)res.StatusCode}: {body}", "gmail_api_error");
            }
            case "gmail_get_message":
            {
                var id = GetString(request.Arguments, "messageId");
                if (string.IsNullOrWhiteSpace(id))
                    return new McpToolCallResult(true, "Missing required 'messageId'.", "invalid_arguments");
                using var req = new HttpRequestMessage(HttpMethod.Get, $"/gmail/v1/users/me/messages/{Uri.EscapeDataString(id)}?format=full");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var res = await client.SendAsync(req, cancellationToken);
                var body = await res.Content.ReadAsStringAsync(cancellationToken);
                return res.IsSuccessStatusCode
                    ? new McpToolCallResult(false, body)
                    : new McpToolCallResult(true, $"Gmail error {(int)res.StatusCode}: {body}", "gmail_api_error");
            }
            case "gmail_create_draft":
            {
                var to = GetString(request.Arguments, "to");
                var subject = GetString(request.Arguments, "subject");
                var body = GetString(request.Arguments, "body");
                if (string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(subject) || body is null)
                    return new McpToolCallResult(true, "Missing required 'to', 'subject', or 'body'.", "invalid_arguments");

                var rawMime = $"To: {to}\r\nSubject: {subject}\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n{body}";
                var base64Url = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawMime))
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');

                using var req = new HttpRequestMessage(HttpMethod.Post, "/gmail/v1/users/me/drafts");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Content = new StringContent(JsonSerializer.Serialize(new { message = new { raw = base64Url } }), Encoding.UTF8, "application/json");
                using var res = await client.SendAsync(req, cancellationToken);
                var resBody = await res.Content.ReadAsStringAsync(cancellationToken);
                return res.IsSuccessStatusCode
                    ? new McpToolCallResult(false, resBody)
                    : new McpToolCallResult(true, $"Gmail draft error {(int)res.StatusCode}: {resBody}", "gmail_api_error");
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
        new("gmail-mcp", name, description, tier, JsonDocument.Parse(schemaJson).RootElement.Clone());
}
