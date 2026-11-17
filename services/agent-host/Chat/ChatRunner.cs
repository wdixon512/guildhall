using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Guildhall.AgentHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Guildhall.AgentHost.Chat;

public sealed class ChatRunner(IDbContextFactory<GuildhallDbContext> factory, IHttpClientFactory clients, ILogger<ChatRunner> logger, IConfiguration configuration, Lazy<IContextBuilder> contextBuilder, IOptionsMonitor<CrossTurnBudgetOptions> budgetOptions) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> active = new();
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    public SemaphoreSlim MutationGate { get; } = new(1, 1);
    private CancellationToken hostStopping;
    private readonly DateTime hostStartedUtc = DateTime.UtcNow;

    public void Start(Guid runId)
    {
        wake.Writer.TryWrite(true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        hostStopping = stoppingToken;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ChatRecovery.ReconcileAsync(factory, hostStartedUtc, stoppingToken); break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { logger.LogWarning("Chat recovery is waiting for the database."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        while (await wake.Reader.WaitToReadAsync(stoppingToken))
        {
            while (wake.Reader.TryRead(out _)) { }
            while (!stoppingToken.IsCancellationRequested)
            {
                Guid? runId = null;
                try
                {
                    await MutationGate.WaitAsync(stoppingToken);
                    try
                    {
                        await using var db = await factory.CreateDbContextAsync(stoppingToken);
                        runId = await db.ChatRuns.Where(x => x.Status == ChatRunStatus.Queued && !db.ChatRuns.Any(prior => prior.ConversationId == x.ConversationId && prior.Status == ChatRunStatus.Queued && prior.TurnNumber < x.TurnNumber))
                            .OrderBy(x => x.StartedUtc).ThenBy(x => x.TurnNumber).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(stoppingToken);
                        if (runId is null) break;
                        var claimed = await db.ChatRuns.Where(x => x.Id == runId && x.Status == ChatRunStatus.Queued)
                            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, ChatRunStatus.Running)
                                .SetProperty(x => x.Sequence, x => x.Sequence + 1).SetProperty(x => x.UpdatedUtc, DateTime.UtcNow), stoppingToken);
                        if (claimed == 0) continue;
                    }
                    finally { MutationGate.Release(); }
                    using var source = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    source.CancelAfter(TimeSpan.FromMinutes(5));
                    active[runId.Value] = source;
                    try { await RunAsync(runId.Value, source.Token); }
                    finally { active.TryRemove(runId.Value, out _); }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch
                {
                    logger.LogWarning("Queued chat processing is waiting for the database.");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }
    }

    public async Task<bool> CancelAsync(Guid runId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var removed = await db.ChatRuns.Where(x => x.Id == runId && x.Status == ChatRunStatus.Queued)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, ChatRunStatus.Cancelled)
                .SetProperty(x => x.ErrorCode, "queue_cancelled").SetProperty(x => x.FinishedUtc, DateTime.UtcNow)
                .SetProperty(x => x.UpdatedUtc, DateTime.UtcNow).SetProperty(x => x.Sequence, x => x.Sequence + 1), ct);
        if (removed > 0) return true;
        // A committed terminal state wins over a later cancellation request.
        var changed = await db.ChatRuns.Where(x => x.Id == runId && x.Status == ChatRunStatus.Running)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, ChatRunStatus.Cancelling)
                .SetProperty(x => x.Sequence, x => x.Sequence + 1).SetProperty(x => x.UpdatedUtc, DateTime.UtcNow), ct);
        if (active.TryGetValue(runId, out var source))
        {
            try { source.Cancel(); } catch (ObjectDisposedException) { }
        }
        else
        {
            // This host has no worker for the persisted run; there is nothing to resume.
            await db.ChatRuns.Where(x => x.Id == runId && x.Status == ChatRunStatus.Cancelling)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, ChatRunStatus.Cancelled)
                    .SetProperty(x => x.Sequence, x => x.Sequence + 1).SetProperty(x => x.FinishedUtc, DateTime.UtcNow)
                    .SetProperty(x => x.UpdatedUtc, DateTime.UtcNow), ct);
        }
        return changed > 0 || await db.ChatRuns.AnyAsync(x => x.Id == runId, ct);
    }

    public async Task RunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var answer = string.Empty;
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var run = await db.ChatRuns.Include(x => x.Messages).ThenInclude(x => x.Attachments).SingleAsync(x => x.Id == runId, cancellationToken);
            if (run.Status != ChatRunStatus.Running) return;
            var user = run.Messages.Single(x => x.Role == ChatMessageRole.User);
            Guid? interruptedRunId = null;
            var instructions = run.InstructionsSnapshot;
            if (run.SettingsSnapshotJson is { } settings)
            {
                using var snapshot = JsonDocument.Parse(settings);
                if (snapshot.RootElement.TryGetProperty("interruptedRunId", out var value) && value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id)) interruptedRunId = id;
                string? behaviorText = snapshot.RootElement.TryGetProperty("guildhallBehavior", out var behavior) && behavior.ValueKind == JsonValueKind.String ? behavior.GetString() : null;
                string? guidelinesText = snapshot.RootElement.TryGetProperty("globalGuidelines", out var guidelines) && guidelines.ValueKind == JsonValueKind.String ? guidelines.GetString() : null;
                var memoryNotes = new List<(string Scope, string Body)>();
                if (snapshot.RootElement.TryGetProperty("memoryNotes", out var notesProp) && notesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var noteEl in notesProp.EnumerateArray())
                    {
                        var scope = noteEl.TryGetProperty("Scope", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                        var body = noteEl.TryGetProperty("Body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(scope) && !string.IsNullOrWhiteSpace(body))
                            memoryNotes.Add((scope, body));
                    }
                }
                if (!string.IsNullOrWhiteSpace(behaviorText) || !string.IsNullOrWhiteSpace(guidelinesText) || memoryNotes.Count > 0)
                    instructions = contextBuilder.Value.BuildStaticSystemMessage(behaviorText, guidelinesText, instructions, memoryNotes);
            }
            var priorRuns = await db.ChatRuns.AsNoTracking()
                .Where(x => x.ConversationId == run.ConversationId && x.TurnNumber < run.TurnNumber && (x.Status == ChatRunStatus.Completed || x.Id == interruptedRunId))
                .OrderBy(x => x.TurnNumber)
                .Select(x => new { x.TurnNumber, Messages = x.Messages.Where(m => x.Status == ChatRunStatus.Completed || m.Role == ChatMessageRole.User).Select(m => new { m.Role, m.Text }).ToList() })
                .ToListAsync(cancellationToken);
            var messages = new List<object> { new { role = "system", content = instructions } };
            foreach (var prior in priorRuns.OrderBy(x => x.TurnNumber))
            foreach (var message in prior.Messages.OrderBy(x => x.Role == ChatMessageRole.User ? 0 : 1))
                messages.Add(new { role = message.Role == ChatMessageRole.User ? "user" : "assistant", content = message.Text });
            var userContent = new System.Text.StringBuilder(user.Text);
            var imageData = new List<string>();
            var extractedChars = 0;
            foreach (var attachment in user.Attachments)
            {
                if (TryReadTextAttachment(attachment, out var extracted))
                {
                    var remaining = 256 * 1024 - extractedChars;
                    if (remaining <= 0) { userContent.AppendLine($"\n[Text extraction limit reached; '{attachment.FileName}' was not included.]"); continue; }
                    var included = extracted.Length > remaining ? extracted[..remaining] + "\n[File text truncated at 256 KB total.]" : extracted;
                    userContent.AppendLine($"\n\n--- Attached file: {attachment.FileName} ---\n{included}");
                    extractedChars += included.Length;
                }
                else if (IsSupportedImage(attachment.Content))
                {
                    imageData.Add(Convert.ToBase64String(attachment.Content));
                    userContent.AppendLine($"\n[Image attached: {attachment.FileName}]");
                }
                else userContent.AppendLine($"\n[Attached file: {attachment.FileName} ({attachment.ContentType}, {attachment.Length} bytes). This provider adapter sends text files and images only; this file's contents were not sent to the model.]");
            }
            messages.Add(new { role = "user", content = userContent.ToString(), images = imageData });
            if (imageData.Count > 0)
            {
                using var modelResponse = await clients.CreateClient("ollama-chat").PostAsJsonAsync("/api/show", new { model = run.ModelId }, cancellationToken);
                modelResponse.EnsureSuccessStatusCode();
                using var modelDetails = await JsonDocument.ParseAsync(await modelResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (modelDetails.RootElement.TryGetProperty("capabilities", out var capabilities) &&
                    !capabilities.EnumerateArray().Any(capability => capability.GetString() == "vision"))
                    throw new InvalidOperationException("model_does_not_support_images");
            }
            var useGpu = configuration.GetValue<bool>("GUILDHALL_USE_GPU");
            var options = new Dictionary<string, object> { ["num_thread"] = 4, ["num_ctx"] = budgetOptions.CurrentValue.MaxTurnContextTokens };
            if (!useGpu) options["num_gpu"] = 0;
            // Request-local settings: do not change the shared Ollama service or other clients.
            // Omit `think` so thinking-capable models emit `message.thinking` by default while non-thinking models remain compatible.
            var keepAliveSeconds = Math.Clamp(configuration.GetValue<int>("GUILDHALL_MODEL_KEEP_ALIVE_SECONDS"), 0, 900);
            var payload = new { model = run.ModelId, stream = true, messages, options, keep_alive = keepAliveSeconds };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(payload) };
            using var response = await clients.CreateClient("ollama-chat").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            var completed = false;
            var inThinking = false;
            var lastCheckpoint = DateTime.UtcNow;
            var checkpointLength = 0;
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (line.Length > 256 * 1024) throw new InvalidOperationException("provider_record_too_large");
                using var json = JsonDocument.Parse(line);
                if (json.RootElement.TryGetProperty("error", out _)) throw new InvalidOperationException("provider_error");
                if (json.RootElement.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("thinking", out var thinkingProp) && thinkingProp.GetString() is { Length: > 0 } thinkingChunk)
                    {
                        if (!inThinking) { answer += "<think>"; inThinking = true; }
                        answer += thinkingChunk;
                    }
                    if (message.TryGetProperty("content", out var contentProp) && contentProp.GetString() is { Length: > 0 } contentChunk)
                    {
                        if (inThinking) { answer += "</think>"; inThinking = false; }
                        answer += contentChunk;
                    }
                }
                if (answer.Length > 131072) throw new InvalidOperationException("output_limit");
                if (answer.Length - checkpointLength >= 1024 || (answer.Length > checkpointLength && DateTime.UtcNow - lastCheckpoint >= TimeSpan.FromMilliseconds(400)))
                {
                    await SaveCheckpointAsync(runId, answer, cancellationToken);
                    checkpointLength = answer.Length;
                    lastCheckpoint = DateTime.UtcNow;
                }
                if (json.RootElement.TryGetProperty("done", out var done) && done.GetBoolean()) { completed = true; break; }
            }
            if (inThinking) answer += "</think>";
            if (!completed) throw new InvalidOperationException("invalid_provider_response");
            await FinishAsync(runId, answer, ChatRunStatus.Completed, null);
        }
        catch (Exception ex)
        {
            var code = hostStopping.IsCancellationRequested ? "host_stopped" : ex is OperationCanceledException ? "generation_timeout" : ex is InvalidOperationException { Message: "model_does_not_support_images" } ? "model_does_not_support_images" : "provider_error";
            logger.LogWarning("Chat run {RunId} failed with {Code}", runId, code);
            try { await FinishAsync(runId, answer, hostStopping.IsCancellationRequested ? ChatRunStatus.Interrupted : ChatRunStatus.Failed, code); }
            catch { logger.LogWarning("Could not persist terminal state for chat run {RunId}", runId); }
        }
    }

    private async Task SaveCheckpointAsync(Guid id, string text, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var changed = await db.ChatRuns.Where(x => x.Id == id && x.Status == ChatRunStatus.Running)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Sequence, x => x.Sequence + 1)
                .SetProperty(x => x.UpdatedUtc, now), cancellationToken);
        if (changed > 0)
            await db.ChatMessages.Where(x => x.RunId == id && x.Role == ChatMessageRole.Assistant)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Text, text), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool TryReadTextAttachment(ChatAttachment attachment, out string text)
    {
        text = string.Empty;
        var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant();
        if (!new[] { ".txt", ".md", ".markdown", ".json", ".csv", ".tsv", ".xml", ".yaml", ".yml", ".html", ".htm", ".css", ".js", ".ts", ".tsx", ".jsx", ".cs", ".py", ".sh", ".ps1", ".sql", ".log", ".toml", ".ini", ".config", ".csproj", ".sln", ".slnx", ".properties", ".env", ".diff", ".patch" }.Contains(extension)) return false;
        try { text = new System.Text.UTF8Encoding(false, true).GetString(attachment.Content); }
        catch (System.Text.DecoderFallbackException) { text = string.Empty; return false; }
        if (text.Contains('\0')) { text = string.Empty; return false; }
        return true;
    }

    private static bool IsSupportedImage(byte[] content) =>
        content.Length >= 12 &&
        ((content.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) ||
         (content[0] == 0xFF && content[1] == 0xD8 && content[^2] == 0xFF && content[^1] == 0xD9) ||
         System.Text.Encoding.ASCII.GetString(content, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(content, 8, 4) == "WEBP" ||
         System.Text.Encoding.ASCII.GetString(content, 0, 3) == "GIF");

    private async Task FinishAsync(Guid id, string text, ChatRunStatus outcome, string? code)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var db = await factory.CreateDbContextAsync(deadline.Token);
        await using var transaction = await db.Database.BeginTransactionAsync(deadline.Token);
        var now = DateTime.UtcNow;
        var changed = await db.ChatRuns.Where(x => x.Id == id && (x.Status == ChatRunStatus.Running || x.Status == ChatRunStatus.Cancelling))
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.Status, x => x.Status == ChatRunStatus.Cancelling ? ChatRunStatus.Cancelled : outcome)
                .SetProperty(x => x.ErrorCode, x => x.Status == ChatRunStatus.Cancelling ? "cancelled" : code)
                .SetProperty(x => x.Sequence, x => x.Sequence + 1)
                .SetProperty(x => x.UpdatedUtc, now).SetProperty(x => x.FinishedUtc, now), deadline.Token);
        if (changed > 0)
            await db.ChatMessages.Where(x => x.RunId == id && x.Role == ChatMessageRole.Assistant)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Text, text.Length > 131072 ? text.Substring(0, 131072) : text), deadline.Token);
        await transaction.CommitAsync(deadline.Token);
    }
}
