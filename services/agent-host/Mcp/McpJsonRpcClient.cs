using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Mcp;

public interface IMcpJsonRpcClient
{
    Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(McpServerRegistration registration, CancellationToken cancellationToken);
    Task<McpToolCallResult> CallToolAsync(McpServerRegistration registration, string toolName, JsonElement arguments, CancellationToken cancellationToken);
}

public sealed class McpJsonRpcClient(
    IHttpClientFactory httpClients,
    ICredentialStore credentialStore,
    EnvironmentScrubber environmentScrubber,
    PrivateNetworkGuard privateNetworkGuard) : IMcpJsonRpcClient
{
    private const int MaxLineLength = 256 * 1024;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(15);
    private readonly ConcurrentDictionary<string, string> _sessionIds = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(McpServerRegistration registration, CancellationToken cancellationToken)
    {
        var result = await SendAsync(registration, "tools/list", new { }, cancellationToken);
        if (!result.TryGetProperty("tools", out var toolsEl) || toolsEl.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<McpToolDescriptor>();
        foreach (var item in toolsEl.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var description = item.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
            var schema = item.TryGetProperty("inputSchema", out var s) && s.ValueKind == JsonValueKind.Object
                ? s.Clone()
                : JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}").RootElement.Clone();

            list.Add(new McpToolDescriptor(
                registration.Id,
                name,
                description,
                registration.SecurityTier,
                schema));
        }

        return list;
    }

    public async Task<McpToolCallResult> CallToolAsync(
        McpServerRegistration registration,
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var result = await SendAsync(
            registration,
            "tools/call",
            new { name = toolName, arguments },
            cancellationToken);

        var isError = result.TryGetProperty("isError", out var errEl) && errEl.ValueKind == JsonValueKind.True;
        var text = ExtractContentText(result);
        return new McpToolCallResult(isError, text, isError ? "mcp_tool_error" : null);
    }

    private async Task<JsonElement> SendAsync(
        McpServerRegistration registration,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CallTimeout);

        if (string.Equals(registration.Transport, "streamable_http", StringComparison.OrdinalIgnoreCase))
            return await SendHttpAsync(registration, method, parameters, timeoutCts.Token);

        return await SendStdioAsync(registration, method, parameters, timeoutCts.Token);
    }

    private async Task<JsonElement> SendHttpAsync(
        McpServerRegistration registration,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registration.EndpointUrl) ||
            !Uri.TryCreate(registration.EndpointUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("MCP Streamable HTTP endpoint must be a valid absolute URI.");
        }

        var endpointValidation = privateNetworkGuard.ValidateMcpEndpoint(uri, registration.AllowPrivateTunnelHttp);
        if (!endpointValidation.IsAllowed)
            throw new InvalidOperationException(endpointValidation.Reason ?? "MCP Streamable HTTP endpoint is not allowed.");

        var client = httpClients.CreateClient("mcp-http");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (_sessionIds.TryGetValue(registration.Id, out var sessionId) && !string.IsNullOrWhiteSpace(sessionId))
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);

        var token = await credentialStore.GetApiKeyAsync($"mcp:{registration.Id}", cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var rpcPayload = new { jsonrpc = "2.0", id = 1, method, @params = parameters };
        request.Content = new StringContent(JsonSerializer.Serialize(rpcPayload), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues))
        {
            var newSessionId = sessionValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(newSessionId))
                _sessionIds[registration.Id] = newSessionId;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            JsonElement? lastResult = null;
            await foreach (var payload in Guildhall.AgentHost.Providers.Cloud.SseStreamReader.ReadDataEventsAsync(stream, cancellationToken))
            {
                using var sseDoc = JsonDocument.Parse(payload);
                lastResult = UnwrapJsonRpcResult(sseDoc.RootElement);
            }
            if (lastResult.HasValue)
                return lastResult.Value;

            throw new InvalidOperationException("MCP Streamable HTTP SSE stream completed without a JSON-RPC result.");
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return UnwrapJsonRpcResult(doc.RootElement);
    }

    private async Task<JsonElement> SendStdioAsync(
        McpServerRegistration registration,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registration.Command))
            throw new InvalidOperationException("MCP stdio registration requires a command.");

        var args = JsonSerializer.Deserialize<string[]>(registration.ArgumentsJson) ?? [];
        var startInfo = new ProcessStartInfo
        {
            FileName = registration.Command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        startInfo.Environment.Clear();
        foreach (var (key, value) in environmentScrubber.BuildScrubbedEnvironment())
            startInfo.Environment[key] = value;

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Failed to start MCP server '{registration.Command}'.");

        try
        {
            await WriteRpcLineAsync(process.StandardInput, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "Guildhall.AgentHost", version = "0.1.0" },
                },
            });
            _ = await ReadRpcResponseAsync(process.StandardOutput, expectedId: 1, cancellationToken);

            await WriteRpcLineAsync(process.StandardInput, new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
            });

            await WriteRpcLineAsync(process.StandardInput, new
            {
                jsonrpc = "2.0",
                id = 2,
                method,
                @params = parameters,
            });

            var response = await ReadRpcResponseAsync(process.StandardOutput, expectedId: 2, cancellationToken);
            return UnwrapJsonRpcResult(response);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort process cleanup.
            }
        }
    }

    private static async Task WriteRpcLineAsync(StreamWriter stdin, object payload)
    {
        await stdin.WriteLineAsync(JsonSerializer.Serialize(payload));
        await stdin.FlushAsync();
    }

    private static async Task<JsonElement> ReadRpcResponseAsync(StreamReader stdout, int expectedId, CancellationToken cancellationToken)
    {
        while (await stdout.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length > MaxLineLength)
                throw new InvalidOperationException("MCP server output line exceeded 256 KiB limit.");
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("id", out var idEl) &&
                idEl.ValueKind == JsonValueKind.Number &&
                idEl.TryGetInt32(out var id) &&
                id == expectedId)
            {
                return doc.RootElement.Clone();
            }
        }

        throw new InvalidOperationException("MCP server closed stdout before responding.");
    }

    private static JsonElement UnwrapJsonRpcResult(JsonElement root)
    {
        if (root.TryGetProperty("error", out var err))
        {
            var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "MCP error";
            throw new InvalidOperationException(msg ?? "MCP error");
        }

        if (root.TryGetProperty("result", out var result))
            return result.Clone();

        throw new InvalidOperationException("Malformed JSON-RPC response from MCP server.");
    }

    private static string ExtractContentText(JsonElement result)
    {
        if (!result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return result.GetRawText();

        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("text", out var textEl))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(textEl.GetString());
            }
        }

        return sb.Length > 0 ? sb.ToString() : result.GetRawText();
    }
}
