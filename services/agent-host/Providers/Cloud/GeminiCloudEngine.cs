using System.Net.Http.Json;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;

namespace Guildhall.AgentHost.Providers.Cloud;

public sealed class GeminiCloudEngine(
    IHttpClientFactory clients,
    ICredentialStore credentials) : IBackingEngine
{
    public string ProviderId => "gemini-api";
    public string EngineKind => BackingEngineKinds.CloudApi;

    public async Task StreamTurnAsync(
        BackingEngineRequest request,
        Func<BackingEngineChunk, CancellationToken, ValueTask> onChunk,
        CancellationToken cancellationToken)
    {
        var apiKey = await credentials.GetApiKeyAsync(ProviderId, cancellationToken)
            ?? throw new BackingEngineException("provider_auth_failed", "Google Gemini API key is not configured.");

        var contents = new List<object>();
        foreach (var message in request.Messages)
        {
            var parts = new List<object> { new { text = message.Text } };
            foreach (var attachment in message.Attachments)
            {
                if (attachment.IsImage && attachment.Content.Length > 0)
                {
                    parts.Add(new
                    {
                        inlineData = new
                        {
                            mimeType = attachment.ContentType,
                            data = Convert.ToBase64String(attachment.Content)
                        }
                    });
                }
            }

            contents.Add(new
            {
                role = message.Role == "assistant" ? "model" : "user",
                parts
            });
        }

        if (request.ToolTurns is { Count: > 0 })
        {
            foreach (var turn in request.ToolTurns)
            {
                contents.Add(new
                {
                    role = "model",
                    parts = turn.Calls.Select(c => new
                    {
                        functionCall = new { name = c.QualifiedName, args = c.Arguments }
                    }).ToArray()
                });
                contents.Add(new
                {
                    role = "user",
                    parts = turn.Results.Select(r => new
                    {
                        functionResponse = new
                        {
                            name = r.QualifiedName,
                            response = new { content = r.Output, isError = r.IsError }
                        }
                    }).ToArray()
                });
            }
        }

        var payload = new Dictionary<string, object>
        {
            ["systemInstruction"] = new { parts = new[] { new { text = request.SystemPrompt } } },
            ["contents"] = contents
        };
        if (request.Tools is { Count: > 0 })
        {
            payload["tools"] = new[]
            {
                new
                {
                    functionDeclarations = request.Tools.Select(t => new
                    {
                        name = t.QualifiedName,
                        description = t.Description,
                        parameters = t.InputSchema
                    }).ToArray()
                }
            };
        }

        var model = Uri.EscapeDataString(string.IsNullOrWhiteSpace(request.ModelId) ? "gemini-2.5-flash" : request.ModelId);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1beta/models/{model}:streamGenerateContent?alt=sse")
        {
            Content = JsonContent.Create(payload)
        };
        httpRequest.Headers.Add("x-goog-api-key", apiKey);

        using var response = await clients.CreateClient("gemini-api")
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        SseStreamReader.EnsureSuccessOrThrow(response, "Google Gemini API");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        int? inputTokens = null;
        int? outputTokens = null;

        await foreach (var data in SseStreamReader.ReadDataEventsAsync(stream, cancellationToken))
        {
            if (data == "[DONE]") break;
            using var json = JsonDocument.Parse(data);

            if (json.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var textEl))
                        {
                            var delta = textEl.GetString();
                            if (!string.IsNullOrEmpty(delta))
                                await onChunk(new BackingEngineChunk(DeltaText: delta), cancellationToken);
                        }

                        if (part.TryGetProperty("functionCall", out var fc))
                        {
                            var name = fc.TryGetProperty("name", out var n) ? n.GetString() : null;
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                var args = fc.TryGetProperty("args", out var a)
                                    ? a.Clone()
                                    : JsonDocument.Parse("{}").RootElement.Clone();
                                await onChunk(new BackingEngineChunk(ToolCalls: [new BackingEngineToolCall(Guid.NewGuid().ToString("N"), name, args)]), cancellationToken);
                            }
                        }
                    }
                }
            }

            if (json.RootElement.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var inEl) && inEl.TryGetInt32(out var inTok))
                    inputTokens = inTok;
                if (usage.TryGetProperty("candidatesTokenCount", out var outEl) && outEl.TryGetInt32(out var outTok))
                    outputTokens = outTok;
            }
        }

        await onChunk(new BackingEngineChunk(
            Usage: new BackingEngineUsage(inputTokens, outputTokens, null, CostSources.MeasuredApi),
            IsComplete: true), cancellationToken);
    }
}
