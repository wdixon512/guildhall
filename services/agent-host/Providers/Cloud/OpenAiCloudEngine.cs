using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;

namespace Guildhall.AgentHost.Providers.Cloud;

public sealed class OpenAiCloudEngine(
    IHttpClientFactory clients,
    ICredentialStore credentials) : IBackingEngine
{
    public string ProviderId => "openai-api";
    public string EngineKind => BackingEngineKinds.CloudApi;

    public async Task StreamTurnAsync(
        BackingEngineRequest request,
        Func<BackingEngineChunk, CancellationToken, ValueTask> onChunk,
        CancellationToken cancellationToken)
    {
        var apiKey = await credentials.GetApiKeyAsync(ProviderId, cancellationToken)
            ?? throw new BackingEngineException("provider_auth_failed", "OpenAI API key is not configured.");

        var messages = new List<object> { new { role = "system", content = request.SystemPrompt } };
        foreach (var message in request.Messages)
        {
            var hasImages = message.Attachments.Any(a => a.IsImage && a.Content.Length > 0);
            if (!hasImages)
            {
                messages.Add(new { role = message.Role, content = message.Text });
                continue;
            }

            var parts = new List<object> { new { type = "text", text = message.Text } };
            foreach (var attachment in message.Attachments)
            {
                if (attachment.IsImage && attachment.Content.Length > 0)
                {
                    var dataUri = $"data:{attachment.ContentType};base64,{Convert.ToBase64String(attachment.Content)}";
                    parts.Add(new { type = "image_url", image_url = new { url = dataUri } });
                }
            }
            messages.Add(new { role = message.Role, content = parts });
        }

        if (request.ToolTurns is { Count: > 0 })
        {
            foreach (var turn in request.ToolTurns)
            {
                messages.Add(new
                {
                    role = "assistant",
                    content = (string?)null,
                    tool_calls = turn.Calls.Select(c => new
                    {
                        id = c.CallId,
                        type = "function",
                        function = new { name = c.QualifiedName, arguments = c.Arguments.GetRawText() }
                    }).ToArray()
                });
                foreach (var res in turn.Results)
                {
                    messages.Add(new
                    {
                        role = "tool",
                        tool_call_id = res.CallId,
                        content = res.Output
                    });
                }
            }
        }

        var payload = new Dictionary<string, object>
        {
            ["model"] = string.IsNullOrWhiteSpace(request.ModelId) ? "gpt-4.1-mini" : request.ModelId,
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true },
            ["messages"] = messages
        };
        if (request.Tools is { Count: > 0 })
        {
            payload["tools"] = request.Tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.QualifiedName,
                    description = t.Description,
                    parameters = t.InputSchema
                }
            }).ToArray();
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await clients.CreateClient("openai-api")
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        SseStreamReader.EnsureSuccessOrThrow(response, "OpenAI API");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        int? inputTokens = null;
        int? outputTokens = null;
        var pendingCalls = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();

        await foreach (var data in SseStreamReader.ReadDataEventsAsync(stream, cancellationToken))
        {
            if (data == "[DONE]") break;
            using var json = JsonDocument.Parse(data);

            if (json.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("delta", out var deltaEl))
                {
                    if (deltaEl.TryGetProperty("content", out var contentEl))
                    {
                        var delta = contentEl.GetString();
                        if (!string.IsNullOrEmpty(delta))
                            await onChunk(new BackingEngineChunk(DeltaText: delta), cancellationToken);
                    }

                    if (deltaEl.TryGetProperty("tool_calls", out var tcArray) && tcArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in tcArray.EnumerateArray())
                        {
                            var idx = tc.TryGetProperty("index", out var idxEl) && idxEl.TryGetInt32(out var i) ? i : 0;
                            if (!pendingCalls.TryGetValue(idx, out var entry))
                                entry = (Guid.NewGuid().ToString("N"), string.Empty, new StringBuilder());

                            if (tc.TryGetProperty("id", out var idEl) && idEl.GetString() is { Length: > 0 } idStr)
                                entry.Id = idStr;
                            if (tc.TryGetProperty("function", out var fn))
                            {
                                if (fn.TryGetProperty("name", out var nEl) && nEl.GetString() is { Length: > 0 } nStr)
                                    entry.Name += nStr;
                                if (fn.TryGetProperty("arguments", out var aEl) && aEl.GetString() is { Length: > 0 } aStr)
                                    entry.Args.Append(aStr);
                            }
                            pendingCalls[idx] = entry;
                        }
                    }
                }
            }

            if (json.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var inEl) && inEl.TryGetInt32(out var inTok))
                    inputTokens = inTok;
                if (usage.TryGetProperty("completion_tokens", out var outEl) && outEl.TryGetInt32(out var outTok))
                    outputTokens = outTok;
            }
        }

        IReadOnlyList<BackingEngineToolCall>? finalizedCalls = null;
        if (pendingCalls.Count > 0)
        {
            finalizedCalls = pendingCalls.OrderBy(kv => kv.Key).Select(kv =>
            {
                var rawArgs = kv.Value.Args.Length > 0 ? kv.Value.Args.ToString() : "{}";
                var parsed = JsonDocument.Parse(rawArgs).RootElement.Clone();
                return new BackingEngineToolCall(kv.Value.Id, kv.Value.Name, parsed);
            }).ToArray();
        }

        await onChunk(new BackingEngineChunk(
            Usage: new BackingEngineUsage(inputTokens, outputTokens, null, CostSources.MeasuredApi),
            IsComplete: true,
            ToolCalls: finalizedCalls), cancellationToken);
    }
}
