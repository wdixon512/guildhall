using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;

namespace Guildhall.AgentHost.Providers.Cloud;

public sealed class AnthropicCloudEngine(
    IHttpClientFactory clients,
    ICredentialStore credentials) : IBackingEngine
{
    public string ProviderId => "anthropic-api";
    public string EngineKind => BackingEngineKinds.CloudApi;

    public async Task StreamTurnAsync(
        BackingEngineRequest request,
        Func<BackingEngineChunk, CancellationToken, ValueTask> onChunk,
        CancellationToken cancellationToken)
    {
        var apiKey = await credentials.GetApiKeyAsync(ProviderId, cancellationToken)
            ?? throw new BackingEngineException("provider_auth_failed", "Anthropic API key is not configured.");

        var messages = new List<object>();
        foreach (var message in request.Messages)
        {
            var hasImages = message.Attachments.Any(a => a.IsImage && a.Content.Length > 0);
            if (!hasImages)
            {
                messages.Add(new { role = message.Role, content = message.Text });
                continue;
            }

            var blocks = new List<object> { new { type = "text", text = message.Text } };
            foreach (var attachment in message.Attachments)
            {
                if (attachment.IsImage && attachment.Content.Length > 0)
                {
                    blocks.Add(new
                    {
                        type = "image",
                        source = new
                        {
                            type = "base64",
                            media_type = attachment.ContentType,
                            data = Convert.ToBase64String(attachment.Content)
                        }
                    });
                }
            }
            messages.Add(new { role = message.Role, content = blocks });
        }

        if (request.ToolTurns is { Count: > 0 })
        {
            foreach (var turn in request.ToolTurns)
            {
                messages.Add(new
                {
                    role = "assistant",
                    content = turn.Calls.Select(c => new
                    {
                        type = "tool_use",
                        id = c.CallId,
                        name = c.QualifiedName,
                        input = c.Arguments
                    }).ToArray()
                });
                messages.Add(new
                {
                    role = "user",
                    content = turn.Results.Select(r => new
                    {
                        type = "tool_result",
                        tool_use_id = r.CallId,
                        content = r.Output,
                        is_error = r.IsError
                    }).ToArray()
                });
            }
        }

        var payload = new Dictionary<string, object>
        {
            ["model"] = string.IsNullOrWhiteSpace(request.ModelId) ? "claude-sonnet-4-20250514" : request.ModelId,
            ["max_tokens"] = 4096,
            ["stream"] = true,
            ["system"] = request.SystemPrompt,
            ["messages"] = messages
        };
        if (request.Tools is { Count: > 0 })
        {
            payload["tools"] = request.Tools.Select(t => new
            {
                name = t.QualifiedName,
                description = t.Description,
                input_schema = t.InputSchema
            }).ToArray();
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = JsonContent.Create(payload)
        };
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await clients.CreateClient("anthropic-api")
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        SseStreamReader.EnsureSuccessOrThrow(response, "Anthropic Claude API");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        int? inputTokens = null;
        int? outputTokens = null;
        var toolBlocks = new Dictionary<int, (string Id, string Name, StringBuilder Input)>();

        await foreach (var data in SseStreamReader.ReadDataEventsAsync(stream, cancellationToken))
        {
            if (data == "[DONE]") break;
            using var json = JsonDocument.Parse(data);
            var eventType = json.RootElement.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            if (eventType == "content_block_start" &&
                json.RootElement.TryGetProperty("index", out var startIdxEl) && startIdxEl.TryGetInt32(out var startIdx) &&
                json.RootElement.TryGetProperty("content_block", out var blockEl) &&
                blockEl.TryGetProperty("type", out var blockType) && blockType.GetString() == "tool_use")
            {
                var id = blockEl.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                var name = blockEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                toolBlocks[startIdx] = (id, name, new StringBuilder());
            }
            else if (eventType == "content_block_delta" &&
                json.RootElement.TryGetProperty("delta", out var deltaEl))
            {
                if (deltaEl.TryGetProperty("text", out var textEl))
                {
                    var delta = textEl.GetString();
                    if (!string.IsNullOrEmpty(delta))
                        await onChunk(new BackingEngineChunk(DeltaText: delta), cancellationToken);
                }
                else if (deltaEl.TryGetProperty("partial_json", out var partialJson) &&
                    json.RootElement.TryGetProperty("index", out var deltaIdxEl) && deltaIdxEl.TryGetInt32(out var deltaIdx) &&
                    toolBlocks.TryGetValue(deltaIdx, out var tb))
                {
                    tb.Input.Append(partialJson.GetString());
                }
            }
            else if (eventType == "message_start" &&
                json.RootElement.TryGetProperty("message", out var msgEl) &&
                msgEl.TryGetProperty("usage", out var startUsage) &&
                startUsage.TryGetProperty("input_tokens", out var inEl) && inEl.TryGetInt32(out var inTok))
            {
                inputTokens = inTok;
            }
            else if (eventType == "message_delta" &&
                json.RootElement.TryGetProperty("usage", out var deltaUsage) &&
                deltaUsage.TryGetProperty("output_tokens", out var outEl) && outEl.TryGetInt32(out var outTok))
            {
                outputTokens = outTok;
            }
        }

        IReadOnlyList<BackingEngineToolCall>? finalizedCalls = null;
        if (toolBlocks.Count > 0)
        {
            finalizedCalls = toolBlocks.OrderBy(kv => kv.Key).Select(kv =>
            {
                var raw = kv.Value.Input.Length > 0 ? kv.Value.Input.ToString() : "{}";
                var parsed = JsonDocument.Parse(raw).RootElement.Clone();
                return new BackingEngineToolCall(kv.Value.Id, kv.Value.Name, parsed);
            }).ToArray();
        }

        await onChunk(new BackingEngineChunk(
            Usage: new BackingEngineUsage(inputTokens, outputTokens, null, CostSources.MeasuredApi),
            IsComplete: true,
            ToolCalls: finalizedCalls), cancellationToken);
    }
}
