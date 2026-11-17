using System.Text.Json;
using Guildhall.AgentHost.Api;

namespace Guildhall.AgentHost.Providers.Ollama;

public sealed class OllamaDiscovery(HttpClient http)
{
    public const int MaxResponseBytes = 1_048_576;
    public async Task<ModelDiscoveryResponse> DiscoverAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var response = await http.GetAsync("api/tags", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return Result("unavailable");
            if (response.Content.Headers.ContentLength > MaxResponseBytes) return Result("invalid_response");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (buffer.Length + count > MaxResponseBytes) return Result("invalid_response");
                buffer.Write(bytes, 0, count);
            }
            using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("models", out var list) || list.ValueKind != JsonValueKind.Array)
                return Result("invalid_response");
            var models = new List<ModelDescriptor>();
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("name", out var name) ||
                    name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || name.GetString()!.Length > 256)
                    return Result("invalid_response");
                models.Add(new(name.GetString()!, name.GetString()!));
            }
            return new("ollama", models.Count > 0 ? "available" : "no_models", models.DistinctBy(x => x.Id).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Result("unavailable"); }
        catch (HttpRequestException) { return Result("unavailable"); }
        catch (JsonException) { return Result("invalid_response"); }
        catch (IOException) { return Result("unavailable"); }
    }

    public async Task<VramResidencyResponse> GetRunningModelsAsync(bool useGpu, int keepAliveSeconds, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var response = await http.GetAsync("api/ps", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return VramResult("unavailable", useGpu, keepAliveSeconds);
            if (response.Content.Headers.ContentLength > MaxResponseBytes) return VramResult("invalid_response", useGpu, keepAliveSeconds);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (buffer.Length + count > MaxResponseBytes) return VramResult("invalid_response", useGpu, keepAliveSeconds);
                buffer.Write(bytes, 0, count);
            }
            using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("models", out var list) || list.ValueKind != JsonValueKind.Array)
                return VramResult("invalid_response", useGpu, keepAliveSeconds);
            var models = new List<RunningModelDescriptor>();
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("name", out var nameProp) ||
                    nameProp.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nameProp.GetString()))
                    continue;
                var size = item.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var s) ? s : 0L;
                var vram = item.TryGetProperty("size_vram", out var vramProp) && vramProp.TryGetInt64(out var v) ? v : 0L;
                DateTimeOffset? expiresAt = item.TryGetProperty("expires_at", out var expProp) && expProp.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(expProp.GetString(), out var dt) ? dt : null;
                int? ctxLen = item.TryGetProperty("context_length", out var ctxProp) && ctxProp.TryGetInt32(out var cl) ? cl : null;
                models.Add(new RunningModelDescriptor(nameProp.GetString()!, size, vram, expiresAt, ctxLen));
            }
            return new("ollama", models.Count > 0 ? "loaded" : "idle", useGpu, keepAliveSeconds, models);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return VramResult("unavailable", useGpu, keepAliveSeconds); }
        catch (HttpRequestException) { return VramResult("unavailable", useGpu, keepAliveSeconds); }
        catch (JsonException) { return VramResult("invalid_response", useGpu, keepAliveSeconds); }
        catch (IOException) { return VramResult("unavailable", useGpu, keepAliveSeconds); }
    }

    private static ModelDiscoveryResponse Result(string status) => new("ollama", status, []);
    private static VramResidencyResponse VramResult(string status, bool useGpu, int keepAliveSeconds) => new("ollama", status, useGpu, keepAliveSeconds, []);
}
