using System.Text;
using System.Text.Json;
using Guildhall.AgentHost.Configuration;

namespace Guildhall.AgentHost.Providers.Cli;

public sealed class CliHarnessEngine(
    CliHarnessDescriptor descriptor,
    ICliProcessRunner processRunner,
    EnvironmentScrubber environmentScrubber) : IBackingEngine
{
    public string ProviderId => descriptor.ProviderId;
    public string EngineKind => BackingEngineKinds.CliHarness;

    public async Task StreamTurnAsync(
        BackingEngineRequest request,
        Func<BackingEngineChunk, CancellationToken, ValueTask> onChunk,
        CancellationToken cancellationToken)
    {
        var executable = processRunner.ResolveExecutable(descriptor.ExecutableName)
            ?? throw new BackingEngineException("cli_not_installed", $"{descriptor.DisplayName} ('{descriptor.ExecutableName}') was not found on PATH.");

        var workDir = !string.IsNullOrWhiteSpace(request.WorkingDirectory) && Directory.Exists(request.WorkingDirectory)
            ? request.WorkingDirectory
            : Environment.CurrentDirectory;

        var prompt = BuildCompositePrompt(request);
        var usesInlinePrompt = string.Equals(ProviderId, "agy-cli", StringComparison.OrdinalIgnoreCase);
        var args = CliHarnessDescriptors.BuildRunArguments(ProviderId, request.ModelId, request.HarnessMode, workDir, usesInlinePrompt ? prompt : null);
        var env = environmentScrubber.BuildScrubbedEnvironment();
        var stderrBuffer = new StringBuilder();
        var sawDelta = false;
        int? inputTokens = null;
        int? outputTokens = null;

        var exitCode = await processRunner.StreamJsonLinesAsync(
            executable,
            args,
            workDir,
            env,
            usesInlinePrompt ? null : prompt,
            async (line, ct) =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                if (!line.TrimStart().StartsWith('{'))
                {
                    await onChunk(new BackingEngineChunk(DeltaText: line + "\n"), ct);
                    sawDelta = true;
                    return;
                }

                try
                {
                    using var json = JsonDocument.Parse(line);
                    var root = json.RootElement;
                    var delta = ExtractTextDelta(root);
                    if (!string.IsNullOrEmpty(delta))
                    {
                        sawDelta = true;
                        await onChunk(new BackingEngineChunk(DeltaText: delta), ct);
                    }
                    else if (!sawDelta &&
                             root.TryGetProperty("event", out var evProp) &&
                             evProp.GetString() == "result" &&
                             root.TryGetProperty("result", out var resProp) &&
                             resProp.ValueKind == JsonValueKind.Object &&
                             resProp.TryGetProperty("denied_actions", out var deniedProp) &&
                             deniedProp.ValueKind == JsonValueKind.Array &&
                             deniedProp.GetArrayLength() > 0)
                    {
                        throw new BackingEngineException(
                            "cli_permission_denied",
                            $"{descriptor.DisplayName} attempted a tool action that was denied in headless mode.");
                    }

                    if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                    {
                        if (usage.TryGetProperty("input_tokens", out var inEl) && inEl.TryGetInt32(out var inTok))
                            inputTokens = inTok;
                        if (usage.TryGetProperty("output_tokens", out var outEl) && outEl.TryGetInt32(out var outTok))
                            outputTokens = outTok;
                    }
                    else if (root.TryGetProperty("result", out var resultObj) &&
                             resultObj.ValueKind == JsonValueKind.Object &&
                             resultObj.TryGetProperty("usage", out var nestedUsage) &&
                             nestedUsage.ValueKind == JsonValueKind.Object)
                    {
                        if (nestedUsage.TryGetProperty("input_tokens", out var inEl) && inEl.TryGetInt32(out var inTok))
                            inputTokens = inTok;
                        if (nestedUsage.TryGetProperty("output_tokens", out var outEl) && outEl.TryGetInt32(out var outTok))
                            outputTokens = outTok;
                    }
                }
                catch (JsonException)
                {
                    // Ignore non-JSON diagnostic lines when mixed into stdout.
                }
            },
            (errLine, _) =>
            {
                if (stderrBuffer.Length < 8192) stderrBuffer.AppendLine(errLine);
                return ValueTask.CompletedTask;
            },
            cancellationToken);

        if (exitCode != 0 && !sawDelta)
        {
            var stderr = stderrBuffer.ToString();
            if (LooksLikeAuthError(stderr))
                throw new BackingEngineException("cli_needs_signin", $"{descriptor.DisplayName} requires sign-in. {descriptor.SignInHint}");

            throw new BackingEngineException("provider_error", $"{descriptor.DisplayName} exited with code {exitCode}.");
        }

        await onChunk(new BackingEngineChunk(
            Usage: new BackingEngineUsage(inputTokens, outputTokens, null, CostSources.SubscriptionCli),
            IsComplete: true), cancellationToken);
    }

    internal static string BuildCompositePrompt(BackingEngineRequest request)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            sb.AppendLine("[Guildhall System Context]");
            sb.AppendLine(request.SystemPrompt.Trim());
            sb.AppendLine();
        }

        if (request.Messages.Count > 1)
        {
            sb.AppendLine("[Conversation History]");
            for (var i = 0; i < request.Messages.Count - 1; i++)
            {
                var msg = request.Messages[i];
                var label = msg.Role == "assistant" ? "Assistant" : "User";
                sb.AppendLine($"{label}: {msg.Text}");
            }
            sb.AppendLine();
        }

        if (request.Messages.Count > 0)
        {
            sb.AppendLine("[Current Request]");
            sb.AppendLine(request.Messages[^1].Text);
        }

        return sb.ToString();
    }

    internal static string? ExtractTextDelta(JsonElement root)
    {
        var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

        if (root.TryGetProperty("event", out var evEl) &&
            evEl.GetString() == "result" &&
            root.TryGetProperty("result", out var resultEl) &&
            resultEl.ValueKind == JsonValueKind.Object &&
            resultEl.TryGetProperty("response", out var responseEl) &&
            responseEl.ValueKind == JsonValueKind.String)
        {
            return responseEl.GetString();
        }

        if (type == "item.completed" &&
            root.TryGetProperty("item", out var item) &&
            item.TryGetProperty("type", out var itemType) &&
            itemType.GetString() == "agent_message" &&
            item.TryGetProperty("text", out var itemText))
        {
            return itemText.GetString();
        }

        if (type == "assistant" &&
            root.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var contentArray) &&
            contentArray.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var block in contentArray.EnumerateArray())
            {
                if (block.TryGetProperty("text", out var blockText))
                    sb.Append(blockText.GetString());
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        if (root.TryGetProperty("delta", out var deltaEl))
        {
            if (deltaEl.ValueKind == JsonValueKind.String)
                return deltaEl.GetString();
            if (deltaEl.ValueKind == JsonValueKind.Object && deltaEl.TryGetProperty("text", out var nestedText))
                return nestedText.GetString();
        }

        if ((type == "content" || type == "text" || type == "message") &&
            root.TryGetProperty("content", out var contentEl) &&
            contentEl.ValueKind == JsonValueKind.String)
        {
            return contentEl.GetString();
        }

        return null;
    }

    private static bool LooksLikeAuthError(string stderr) =>
        stderr.Contains("login", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("API_KEY", StringComparison.OrdinalIgnoreCase);
}
