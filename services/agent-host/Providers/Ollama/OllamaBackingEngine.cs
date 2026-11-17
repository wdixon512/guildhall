using System.Net.Http.Json;
using System.Text.Json;
using Guildhall.AgentHost.Chat;
using Microsoft.Extensions.Options;

namespace Guildhall.AgentHost.Providers.Ollama;

public sealed class OllamaBackingEngine(
    IHttpClientFactory clients,
    IConfiguration configuration,
    IOptionsMonitor<CrossTurnBudgetOptions> budgetOptions) : IBackingEngine
{
    public string ProviderId => "ollama";
    public string EngineKind => BackingEngineKinds.Local;

    public async Task StreamTurnAsync(
        BackingEngineRequest request,
        Func<BackingEngineChunk, CancellationToken, ValueTask> onChunk,
        CancellationToken cancellationToken)
    {
        var messages = new List<object> { new { role = "system", content = request.SystemPrompt } };
        var hasImages = false;

        foreach (var message in request.Messages)
        {
            var imageData = new List<string>();
            foreach (var attachment in message.Attachments)
            {
                if (attachment.IsImage && attachment.Content.Length > 0)
                {
                    imageData.Add(Convert.ToBase64String(attachment.Content));
                    hasImages = true;
                }
            }

            if (imageData.Count > 0)
                messages.Add(new { role = message.Role, content = message.Text, images = imageData });
            else
                messages.Add(new { role = message.Role, content = message.Text });
        }

        if (request.ToolTurns is { Count: > 0 })
        {
            foreach (var turn in request.ToolTurns)
            {
                messages.Add(new
                {
                    role = "assistant",
                    content = string.Empty,
                    tool_calls = turn.Calls.Select(c => new
                    {
                        function = new { name = c.QualifiedName, arguments = c.Arguments }
                    }).ToArray()
                });
                foreach (var res in turn.Results)
                {
                    messages.Add(new { role = "tool", content = res.Output });
                }
            }
        }

        if (hasImages)
        {
            using var modelResponse = await clients.CreateClient("ollama-chat")
                .PostAsJsonAsync("/api/show", new { model = request.ModelId }, cancellationToken);
            modelResponse.EnsureSuccessStatusCode();
            using var modelDetails = await JsonDocument.ParseAsync(
                await modelResponse.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (modelDetails.RootElement.TryGetProperty("capabilities", out var capabilities) &&
                !capabilities.EnumerateArray().Any(capability => capability.GetString() == "vision"))
            {
                throw new BackingEngineException("model_does_not_support_images", "This model cannot read images.");
            }
        }

        var useGpu = configuration.GetValue<bool>("GUILDHALL_USE_GPU");
        var options = new Dictionary<string, object>
        {
            ["num_thread"] = 4,
            ["num_ctx"] = budgetOptions.CurrentValue.MaxTurnContextTokens
        };
        if (!useGpu) options["num_gpu"] = 0;

        var keepAliveSeconds = Math.Clamp(configuration.GetValue<int>("GUILDHALL_MODEL_KEEP_ALIVE_SECONDS"), 0, 900);
        var payload = new Dictionary<string, object>
        {
            ["model"] = request.ModelId,
            ["stream"] = true,
            ["messages"] = messages,
            ["options"] = options,
            ["keep_alive"] = keepAliveSeconds
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

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(payload)
        };
        using var response = await clients.CreateClient("ollama-chat")
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var completed = false;
        var inThinking = false;
        int? inputTokens = null;
        int? outputTokens = null;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length > 256 * 1024)
                throw new BackingEngineException("provider_record_too_large", "Provider response line exceeded limit.");

            using var json = JsonDocument.Parse(line);
            if (json.RootElement.TryGetProperty("error", out _))
                throw new BackingEngineException("provider_error", "Ollama returned an error.");

            if (json.RootElement.TryGetProperty("message", out var message))
            {
                if (message.TryGetProperty("thinking", out var thinkingProp) && thinkingProp.GetString() is { Length: > 0 } thinkingChunk)
                {
                    var prefix = inThinking ? string.Empty : "<think>";
                    inThinking = true;
                    await onChunk(new BackingEngineChunk(DeltaText: prefix + thinkingChunk), cancellationToken);
                }

                if (message.TryGetProperty("content", out var content) && content.GetString() is { Length: > 0 } delta)
                {
                    var prefix = inThinking ? "</think>" : string.Empty;
                    inThinking = false;
                    await onChunk(new BackingEngineChunk(DeltaText: prefix + delta), cancellationToken);
                }

                if (message.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
                {
                    var calls = new List<BackingEngineToolCall>();
                    foreach (var tc in toolCallsEl.EnumerateArray())
                    {
                        if (!tc.TryGetProperty("function", out var fn)) continue;
                        var name = fn.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var args = fn.TryGetProperty("arguments", out var a)
                            ? a.Clone()
                            : JsonDocument.Parse("{}").RootElement.Clone();
                        calls.Add(new BackingEngineToolCall(Guid.NewGuid().ToString("N"), name, args));
                    }
                    if (calls.Count > 0)
                        await onChunk(new BackingEngineChunk(ToolCalls: calls), cancellationToken);
                }
            }

            if (json.RootElement.TryGetProperty("done", out var done) && done.GetBoolean())
            {
                if (inThinking)
                {
                    inThinking = false;
                    await onChunk(new BackingEngineChunk(DeltaText: "</think>"), cancellationToken);
                }
                if (json.RootElement.TryGetProperty("prompt_eval_count", out var promptCount) && promptCount.TryGetInt32(out var inTok))
                    inputTokens = inTok;
                if (json.RootElement.TryGetProperty("eval_count", out var evalCount) && evalCount.TryGetInt32(out var outTok))
                    outputTokens = outTok;

                completed = true;
                await onChunk(new BackingEngineChunk(
                    Usage: new BackingEngineUsage(inputTokens, outputTokens, null, CostSources.LocalOllama),
                    IsComplete: true), cancellationToken);
                break;
            }
        }

        if (!completed)
            throw new BackingEngineException("invalid_provider_response", "Ollama stream ended before completion.");
    }
}
