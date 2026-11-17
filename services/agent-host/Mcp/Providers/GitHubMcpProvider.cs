using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Mcp.Providers;

public sealed class GitHubMcpProvider(
    IHttpClientFactory httpClients,
    ICredentialStore credentialStore) : IMcpToolProvider
{
    private static readonly IReadOnlyList<McpToolDescriptor> Tools =
    [
        CreateDescriptor("github_get_repository", "Get metadata for a GitHub repository (read-only).", ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{"owner":{"type":"string"},"repo":{"type":"string"}},"required":["owner","repo"]}"""),
        CreateDescriptor("github_list_issues", "List open issues in a GitHub repository (read-only).", ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{"owner":{"type":"string"},"repo":{"type":"string"},"state":{"type":"string"}},"required":["owner","repo"]}"""),
        CreateDescriptor("github_list_pull_requests", "List pull requests in a GitHub repository (read-only).", ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{"owner":{"type":"string"},"repo":{"type":"string"},"state":{"type":"string"}},"required":["owner","repo"]}"""),
        CreateDescriptor("github_get_pull_request_files", "List changed files and patches in a pull request (read-only).", ToolSecurityTiers.Tier1,
            """{"type":"object","properties":{"owner":{"type":"string"},"repo":{"type":"string"},"pullNumber":{"type":"integer"}},"required":["owner","repo","pullNumber"]}"""),
        CreateDescriptor("github_create_issue", "Create a new GitHub issue (Tier 3: requires human approval).", ToolSecurityTiers.Tier3,
            """{"type":"object","properties":{"owner":{"type":"string"},"repo":{"type":"string"},"title":{"type":"string"},"body":{"type":"string"}},"required":["owner","repo","title"]}"""),
        CreateDescriptor("github_create_issue_comment", "Post a comment on a GitHub issue or PR (Tier 3: requires human approval).", ToolSecurityTiers.Tier3,
            """{"type":"object","properties":{"owner":{"type":"string"},"repo":{"type":"string"},"issueNumber":{"type":"integer"},"body":{"type":"string"}},"required":["owner","repo","issueNumber","body"]}"""),
    ];

    public string ServerId => "github-mcp";
    public string DisplayName => "GitHub";
    public string Transport => "builtin_http";
    public string DefaultSecurityTier => ToolSecurityTiers.Tier1;

    public IReadOnlyList<McpToolDescriptor> GetTools() => Tools;

    public async Task<(bool Healthy, string? Detail)> ProbeAsync(CancellationToken cancellationToken)
    {
        var token = await credentialStore.GetApiKeyAsync(ServerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return (false, "Add a GitHub Personal Access Token (encrypted with Windows DPAPI)");

        using var request = CreateRequest(HttpMethod.Get, "/user", token);
        using var response = await httpClients.CreateClient("github-api").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return (false, $"GitHub returned HTTP {(int)response.StatusCode}");

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var login = doc.RootElement.TryGetProperty("login", out var l) ? l.GetString() : null;
        return (true, string.IsNullOrWhiteSpace(login) ? "Connected to GitHub" : $"Signed in as @{login}");
    }

    public async Task<McpToolCallResult> InvokeAsync(McpToolCallRequest request, CancellationToken cancellationToken)
    {
        var token = await credentialStore.GetApiKeyAsync(ServerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return new McpToolCallResult(true, "GitHub token is not configured in Connections.", "provider_auth_failed");

        var owner = GetString(request.Arguments, "owner");
        var repo = GetString(request.Arguments, "repo");
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return new McpToolCallResult(true, "Missing required 'owner' or 'repo' argument.", "invalid_arguments");

        var safeOwner = Uri.EscapeDataString(owner);
        var safeRepo = Uri.EscapeDataString(repo);
        var client = httpClients.CreateClient("github-api");

        switch (request.ToolName)
        {
            case "github_get_repository":
                return await ExecuteJsonAsync(client, HttpMethod.Get, $"/repos/{safeOwner}/{safeRepo}", token, null, cancellationToken);
            case "github_list_issues":
            {
                var state = Uri.EscapeDataString(GetString(request.Arguments, "state") ?? "open");
                return await ExecuteJsonAsync(client, HttpMethod.Get, $"/repos/{safeOwner}/{safeRepo}/issues?state={state}&per_page=15", token, null, cancellationToken);
            }
            case "github_list_pull_requests":
            {
                var state = Uri.EscapeDataString(GetString(request.Arguments, "state") ?? "open");
                return await ExecuteJsonAsync(client, HttpMethod.Get, $"/repos/{safeOwner}/{safeRepo}/pulls?state={state}&per_page=15", token, null, cancellationToken);
            }
            case "github_get_pull_request_files":
            {
                if (!TryGetInt(request.Arguments, "pullNumber", out var prNum))
                    return new McpToolCallResult(true, "Missing required 'pullNumber'.", "invalid_arguments");
                return await ExecuteJsonAsync(client, HttpMethod.Get, $"/repos/{safeOwner}/{safeRepo}/pulls/{prNum}/files?per_page=30", token, null, cancellationToken);
            }
            case "github_create_issue":
            {
                var title = GetString(request.Arguments, "title");
                if (string.IsNullOrWhiteSpace(title))
                    return new McpToolCallResult(true, "Missing required 'title'.", "invalid_arguments");
                var body = GetString(request.Arguments, "body") ?? string.Empty;
                return await ExecuteJsonAsync(client, HttpMethod.Post, $"/repos/{safeOwner}/{safeRepo}/issues", token, new { title, body }, cancellationToken);
            }
            case "github_create_issue_comment":
            {
                if (!TryGetInt(request.Arguments, "issueNumber", out var issueNumber))
                    return new McpToolCallResult(true, "Missing required 'issueNumber'.", "invalid_arguments");
                var body = GetString(request.Arguments, "body");
                if (string.IsNullOrWhiteSpace(body))
                    return new McpToolCallResult(true, "Missing required 'body'.", "invalid_arguments");
                return await ExecuteJsonAsync(client, HttpMethod.Post, $"/repos/{safeOwner}/{safeRepo}/issues/{issueNumber}/comments", token, new { body }, cancellationToken);
            }
            default:
                return new McpToolCallResult(true, $"Unknown tool '{request.ToolName}'.", "tool_not_found");
        }
    }

    private static async Task<McpToolCallResult> ExecuteJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string token,
        object? payload,
        CancellationToken cancellationToken)
    {
        using var req = CreateRequest(method, path, token);
        if (payload is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var res = await client.SendAsync(req, cancellationToken);
        var text = await res.Content.ReadAsStringAsync(cancellationToken);
        if (!res.IsSuccessStatusCode)
            return new McpToolCallResult(true, $"GitHub API error {(int)res.StatusCode}: {text}", "github_api_error");

        return new McpToolCallResult(false, text);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string token)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.UserAgent.ParseAdd("Guildhall-AgentHost/0.1");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return req;
    }

    private static string? GetString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static bool TryGetInt(JsonElement args, string name, out int value)
    {
        value = 0;
        return args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var prop) && prop.TryGetInt32(out value);
    }

    private static McpToolDescriptor CreateDescriptor(string name, string description, string tier, string schemaJson) =>
        new("github-mcp", name, description, tier, JsonDocument.Parse(schemaJson).RootElement.Clone());
}
