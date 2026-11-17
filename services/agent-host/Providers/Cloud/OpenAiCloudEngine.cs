using System.Net.Http.Headers;
using System.Net.Http.Json;
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

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(request.ModelId) ? "gpt-4.1-mini" : request.ModelId,
            stream = true,
            stream_options = new { include_usage = true },
            messages
        };

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

        await foreach (var data in SseStreamReader.ReadDataEventsAsync(stream, cancellationToken))
        {
            if (data == "[DONE]") break;
            using var json = JsonDocument.Parse(data);

            if (json.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("delta", out var deltaEl) &&
                    deltaEl.TryGetProperty("content", out var contentEl))
                {
                    var delta = contentEl.GetString();
                    if (!string.IsNullOrEmpty(delta))
                        await onChunk(new BackingEngineChunk(DeltaText: delta), cancellationToken);
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

        await onChunk(new BackingEngineChunk(
            Usage: new BackingEngineUsage(inputTokens, outputTokens, null, CostSources.MeasuredApi),
            IsComplete: true), cancellationToken);
    }
}
