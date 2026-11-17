using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace Guildhall.AgentHost.Providers.Cloud;

public static class SseStreamReader
{
    public static async IAsyncEnumerable<string> ReadDataEventsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var dataLines = new List<string>();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length > 512 * 1024)
                throw new BackingEngineException("provider_record_too_large", "SSE event line exceeded size limit.");

            if (line.Length == 0)
            {
                if (dataLines.Count > 0)
                {
                    yield return string.Join("\n", dataLines);
                    dataLines.Clear();
                }
                continue;
            }

            if (line.StartsWith(':'))
                continue;

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var value = line.Length > 5 && line[5] == ' ' ? line[6..] : line[5..];
                dataLines.Add(value);
            }
        }

        if (dataLines.Count > 0)
            yield return string.Join("\n", dataLines);
    }

    public static void EnsureSuccessOrThrow(HttpResponseMessage response, string providerLabel)
    {
        if (response.IsSuccessStatusCode) return;

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new BackingEngineException("provider_auth_failed", $"{providerLabel} rejected the configured API key."),
            HttpStatusCode.TooManyRequests =>
                new BackingEngineException("provider_rate_limited", $"{providerLabel} rate limit exceeded."),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                new BackingEngineException("provider_bad_request", $"{providerLabel} rejected the request payload ({(int)response.StatusCode})."),
            _ =>
                new BackingEngineException("provider_unavailable", $"{providerLabel} returned HTTP {(int)response.StatusCode}.")
        };
    }
}
