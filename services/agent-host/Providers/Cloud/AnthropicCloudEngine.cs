using System.Net.Http.Json;
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

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(request.ModelId) ? "claude-sonnet-4-20250514" : request.ModelId,
            max_tokens = 4096,
            stream = true,
            system = request.SystemPrompt,
            messages
        };

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

        await foreach (var data in SseStreamReader.ReadDataEventsAsync(stream, cancellationToken))
        {
            if (data == "[DONE]") break;
            using var json = JsonDocument.Parse(data);
            var eventType = json.RootElement.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            if (eventType == "content_block_delta" &&
                json.RootElement.TryGetProperty("delta", out var deltaEl) &&
                deltaEl.TryGetProperty("text", out var textEl))
            {
                var delta = textEl.GetString();
                if (!string.IsNullOrEmpty(delta))
                    await onChunk(new BackingEngineChunk(DeltaText: delta), cancellationToken);
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

        await onChunk(new BackingEngineChunk(
            Usage: new BackingEngineUsage(inputTokens, outputTokens, null, CostSources.MeasuredApi),
            IsComplete: true), cancellationToken);
    }
}
