using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Providers;
using Guildhall.AgentHost.Security;
using Guildhall.AgentHost.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Chat;

public sealed class ChatRunner(
    IDbContextFactory<GuildhallDbContext> factory,
    IBackingEngineRegistry engineRegistry,
    TimeProvider timeProvider,
    ILogger<ChatRunner> logger,
    Lazy<IContextBuilder> contextBuilder,
    McpRegistry? mcpRegistry = null,
    ToolPermissionGate? permissionGate = null,
    ExfiltrationGuard? exfiltrationGuard = null,
    ToolRollbackService? rollbackService = null) : BackgroundService
{
    private const int MaxToolRounds = 6;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> active = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> pendingApprovals = new();
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

    public async Task<bool> ResolveEventDecisionAsync(Guid runId, Guid eventId, bool approved, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await db.ChatRunEvents
            .Where(x => x.Id == eventId && x.RunId == runId && x.Status == "PendingApproval")
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.Status, approved ? "Approved" : "Denied")
                .SetProperty(x => x.EventType, "approval.resolved")
                .SetProperty(x => x.CompletedUtc, approved ? null : now), ct);

        if (updated == 0)
            return false;

        if (pendingApprovals.TryRemove(eventId, out var tcs))
            tcs.TrySetResult(approved);

        return true;
    }

    public async Task RunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var answer = string.Empty;
        var startedAt = timeProvider.GetTimestamp();
        var engineKind = BackingEngineKinds.Local;
        BackingEngineUsage? usage = null;
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var run = await db.ChatRuns
                .Include(x => x.Conversation)
                .ThenInclude(x => x.AgentDefinition)
                .Include(x => x.Messages)
                .ThenInclude(x => x.Attachments)
                .SingleAsync(x => x.Id == runId, cancellationToken);
            if (run.Status != ChatRunStatus.Running) return;
            var user = run.Messages.Single(x => x.Role == ChatMessageRole.User);
            Guid? interruptedRunId = null;
            string? harnessMode = run.Conversation.SelectedHarnessMode ?? run.Conversation.AgentDefinition.DefaultHarnessMode;
            string? workingDirectory = run.Conversation.WorkspaceRootPath;
            if (run.Conversation.ActiveWorktreeId is { } activeWtId)
            {
                var wtPath = await db.ProjectWorktrees.AsNoTracking()
                    .Where(w => w.Id == activeWtId && w.Status == "Active")
                    .Select(w => w.WorktreePath)
                    .FirstOrDefaultAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(wtPath) && Directory.Exists(wtPath))
                    workingDirectory = wtPath;
            }
            var workspaceOverride = await db.GuildhallBehaviorSettings.AsNoTracking()
                .Where(s => s.Id == 1)
                .Select(s => s.WorkspacePolicyOverride)
                .FirstOrDefaultAsync(cancellationToken);
            var permissionPolicy = PermissionPolicies.ResolveEffective(
                workspaceOverride,
                run.Conversation.PermissionPolicyOverride,
                run.Conversation.AgentDefinition.PermissionPolicy);
            if (string.IsNullOrWhiteSpace(harnessMode))
                harnessMode = permissionPolicy == PermissionPolicies.Unrestricted ? "unrestricted" : "read_only";
            var enabledServers = ParseToolIds(run.Conversation.AgentDefinition.EnabledToolIdsJson);
            var instructions = run.InstructionsSnapshot;
            if (run.SettingsSnapshotJson is { } settings)
            {
                using var snapshot = JsonDocument.Parse(settings);
                if (snapshot.RootElement.TryGetProperty("interruptedRunId", out var value) && value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id)) interruptedRunId = id;
                if (snapshot.RootElement.TryGetProperty("harnessMode", out var modeEl) && modeEl.ValueKind == JsonValueKind.String) harnessMode = modeEl.GetString();
                if (snapshot.RootElement.TryGetProperty("workingDirectory", out var dirEl) && dirEl.ValueKind == JsonValueKind.String) workingDirectory = dirEl.GetString();
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

            var engineMessages = new List<BackingEngineMessage>();
            foreach (var prior in priorRuns.OrderBy(x => x.TurnNumber))
            foreach (var message in prior.Messages.OrderBy(x => x.Role == ChatMessageRole.User ? 0 : 1))
                engineMessages.Add(new BackingEngineMessage(message.Role == ChatMessageRole.User ? "user" : "assistant", message.Text, []));

            var userContent = new StringBuilder(user.Text);
            var engineAttachments = new List<BackingEngineAttachment>();
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
                    engineAttachments.Add(new BackingEngineAttachment(attachment.FileName, attachment.ContentType, [], included, IsImage: false));
                }
                else if (IsSupportedImage(attachment.Content))
                {
                    engineAttachments.Add(new BackingEngineAttachment(attachment.FileName, attachment.ContentType, attachment.Content, null, IsImage: true));
                    userContent.AppendLine($"\n[Image attached: {attachment.FileName}]");
                }
                else
                {
                    userContent.AppendLine($"\n[Attached file: {attachment.FileName} ({attachment.ContentType}, {attachment.Length} bytes). This provider adapter sends text files and images only; this file's contents were not sent to the model.]");
                }
            }
            engineMessages.Add(new BackingEngineMessage("user", userContent.ToString(), engineAttachments));

            var engine = engineRegistry.Resolve(run.ProviderId)
                ?? throw new BackingEngineException("provider_unavailable", $"Provider '{run.ProviderId}' is not registered.");
            engineKind = engine.EngineKind;

            var enabledTools = mcpRegistry is not null
                ? await mcpRegistry.GetEnabledToolsForMemberAsync(enabledServers, cancellationToken)
                : [];
            var toolByQualifiedName = enabledTools.ToDictionary(t => t.QualifiedName, StringComparer.OrdinalIgnoreCase);
            var toolTurns = new List<BackingEngineToolTurn>();
            var eventSequence = 0;

            var lastCheckpoint = timeProvider.GetUtcNow().UtcDateTime;
            var checkpointLength = 0;

            for (var round = 0; round < MaxToolRounds; round++)
            {
                IReadOnlyList<BackingEngineToolCall>? requestedCalls = null;

                var engineRequest = new BackingEngineRequest(
                    run.Id,
                    run.ConversationId,
                    run.ProviderId,
                    run.ModelId,
                    harnessMode,
                    instructions,
                    engineMessages,
                    workingDirectory,
                    enabledTools,
                    toolTurns);

                await engine.StreamTurnAsync(engineRequest, async (chunk, ct) =>
                {
                    if (!string.IsNullOrEmpty(chunk.DeltaText))
                    {
                        answer += chunk.DeltaText;
                        if (answer.Length > 131072)
                            throw new BackingEngineException("output_limit", "Assistant output exceeded maximum length.");

                        var now = timeProvider.GetUtcNow().UtcDateTime;
                        if (answer.Length - checkpointLength >= 1024 || (answer.Length > checkpointLength && now - lastCheckpoint >= TimeSpan.FromMilliseconds(400)))
                        {
                            await SaveCheckpointAsync(runId, answer, ct);
                            checkpointLength = answer.Length;
                            lastCheckpoint = now;
                        }
                    }

                    if (chunk.ToolCalls is { Count: > 0 })
                        requestedCalls = chunk.ToolCalls;

                    if (chunk.Usage is not null)
                        usage = chunk.Usage;
                }, cancellationToken);

                if (requestedCalls is null || requestedCalls.Count == 0 || mcpRegistry is null || permissionGate is null)
                    break;

                var roundResults = new List<BackingEngineToolResult>(requestedCalls.Count);
                foreach (var call in requestedCalls)
                {
                    eventSequence++;
                    var toolResult = await ExecuteToolCallWithGateAsync(
                        runId,
                        run.ConversationId,
                        run.Conversation.ActiveWorktreeId,
                        eventSequence,
                        call,
                        toolByQualifiedName,
                        enabledServers,
                        permissionPolicy,
                        workingDirectory,
                        cancellationToken);
                    roundResults.Add(toolResult);
                }

                toolTurns.Add(new BackingEngineToolTurn(requestedCalls, roundResults));
            }

            var elapsedMs = (long)timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
            await FinishAsync(runId, answer, ChatRunStatus.Completed, null, engineKind, usage, elapsedMs);
        }
        catch (Exception ex)
        {
            var elapsedMs = (long)timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
            var code = hostStopping.IsCancellationRequested
                ? "host_stopped"
                : ex is OperationCanceledException
                    ? "generation_timeout"
                    : ex is BackingEngineException bee
                        ? bee.ErrorCode
                        : "provider_error";
            logger.LogWarning(ex, "Chat run {RunId} failed with {Code}", runId, code);
            try { await FinishAsync(runId, answer, hostStopping.IsCancellationRequested ? ChatRunStatus.Interrupted : ChatRunStatus.Failed, code, engineKind, usage, elapsedMs); }
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

    private async Task<BackingEngineToolResult> ExecuteToolCallWithGateAsync(
        Guid runId,
        Guid conversationId,
        Guid? activeWorktreeId,
        int sequenceNumber,
        BackingEngineToolCall call,
        IReadOnlyDictionary<string, McpToolDescriptor> toolByQualifiedName,
        IReadOnlySet<string> enabledServers,
        string permissionPolicy,
        string? workspaceRootPath,
        CancellationToken cancellationToken)
    {
        var (serverId, toolName) = ResolveServerAndTool(call.QualifiedName, toolByQualifiedName);
        var descriptor = toolByQualifiedName.GetValueOrDefault(call.QualifiedName);
        var securityTier = descriptor?.SecurityTier ?? ToolSecurityTiers.Tier3;
        var rawArgs = call.Arguments.ValueKind == JsonValueKind.Undefined ? "{}" : call.Arguments.GetRawText();
        var redactedArgs = exfiltrationGuard?.TruncateAndRedact(rawArgs, 8000) ?? rawArgs;

        var eventId = Guid.NewGuid();
        var createdUtc = timeProvider.GetUtcNow().UtcDateTime;
        var toolStartedAt = timeProvider.GetTimestamp();

        var decision = permissionGate!.Evaluate(
            permissionPolicy,
            enabledServers,
            serverId,
            toolName,
            securityTier,
            rawArgs);

        var initialStatus = decision.Outcome switch
        {
            GateOutcome.Allow => "Running",
            GateOutcome.RequireApproval => "PendingApproval",
            _ => "Denied",
        };
        var initialEventType = decision.Outcome == GateOutcome.RequireApproval ? "approval.requested" : "tool.requested";

        await using (var db = await factory.CreateDbContextAsync(cancellationToken))
        {
            db.ChatRunEvents.Add(new ChatRunEvent
            {
                Id = eventId,
                RunId = runId,
                SequenceNumber = sequenceNumber,
                EventType = initialEventType,
                ServerId = serverId,
                ToolName = toolName,
                SecurityTier = securityTier,
                Status = initialStatus,
                ArgumentsRedactedJson = redactedArgs,
                ResultRedactedJson = decision.Outcome == GateOutcome.Deny ? decision.Reason : null,
                ErrorCode = decision.ErrorCode,
                CreatedUtc = createdUtc,
                CompletedUtc = decision.Outcome == GateOutcome.Deny ? createdUtc : null,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        if (decision.Outcome == GateOutcome.Deny)
            return new BackingEngineToolResult(call.CallId, call.QualifiedName, decision.Reason ?? "Tool denied.", IsError: true);

        if (decision.Outcome == GateOutcome.RequireApproval)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingApprovals[eventId] = tcs;
            try
            {
                using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
                var approved = await tcs.Task;
                if (!approved)
                    return new BackingEngineToolResult(call.CallId, call.QualifiedName, "User denied this tool execution.", IsError: true);
            }
            finally
            {
                pendingApprovals.TryRemove(eventId, out _);
            }
        }

        ToolRollbackSnapshot? capturedSnapshot = null;
        if (string.Equals(securityTier, ToolSecurityTiers.Tier2, StringComparison.OrdinalIgnoreCase) && rollbackService is not null)
        {
            capturedSnapshot = await rollbackService.CapturePreStateAsync(
                runId,
                eventId,
                conversationId,
                activeWorktreeId,
                workspaceRootPath,
                toolName,
                rawArgs,
                cancellationToken);
        }

        McpToolCallResult result;
        try
        {
            result = await mcpRegistry!.InvokeToolAsync(
                new McpToolCallRequest(serverId, toolName, call.Arguments, workspaceRootPath),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (capturedSnapshot is not null && rollbackService is not null)
                await rollbackService.RollbackEventAsync(runId, eventId, CancellationToken.None);
            result = new McpToolCallResult(true, $"Tool execution threw an error and was rolled back: {ex.Message}", "tool_exception");
        }

        if (result.IsError && capturedSnapshot is not null && rollbackService is not null)
        {
            var (rolledBack, _, _) = await rollbackService.RollbackEventAsync(runId, eventId, CancellationToken.None);
            if (rolledBack)
                result = result with { Content = $"{result.Content}\n[Automatic rollback restored previous workspace state.]" };
        }

        var durationMs = (long)timeProvider.GetElapsedTime(toolStartedAt).TotalMilliseconds;
        var completedUtc = timeProvider.GetUtcNow().UtcDateTime;
        var redactedResult = exfiltrationGuard?.TruncateAndRedact(result.Content, 16000) ?? result.Content;

        await using (var db = await factory.CreateDbContextAsync(cancellationToken))
        {
            await db.ChatRunEvents
                .Where(x => x.Id == eventId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(x => x.EventType, result.IsError ? "tool.failed" : "tool.completed")
                    .SetProperty(x => x.Status, result.IsError ? "Failed" : "Completed")
                    .SetProperty(x => x.ResultRedactedJson, redactedResult)
                    .SetProperty(x => x.ErrorCode, result.ErrorCode)
                    .SetProperty(x => x.DurationMs, durationMs)
                    .SetProperty(x => x.CompletedUtc, completedUtc), cancellationToken);
        }

        return new BackingEngineToolResult(call.CallId, call.QualifiedName, redactedResult, result.IsError);
    }

    private static (string ServerId, string ToolName) ResolveServerAndTool(
        string qualifiedName,
        IReadOnlyDictionary<string, McpToolDescriptor> toolByQualifiedName)
    {
        if (toolByQualifiedName.TryGetValue(qualifiedName, out var descriptor))
            return (descriptor.ServerId, descriptor.Name);

        var sep = qualifiedName.IndexOf("__", StringComparison.Ordinal);
        return sep > 0
            ? (qualifiedName[..sep], qualifiedName[(sep + 2)..])
            : ("unknown", qualifiedName);
    }

    private static HashSet<string> ParseToolIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var items = JsonSerializer.Deserialize<string[]>(json);
            return items is null ? [] : new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return [];
        }
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

    private async Task FinishAsync(
        Guid id,
        string text,
        ChatRunStatus outcome,
        string? code,
        string engineKind,
        BackingEngineUsage? usage,
        long durationMs)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var db = await factory.CreateDbContextAsync(deadline.Token);
        await using var transaction = await db.Database.BeginTransactionAsync(deadline.Token);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var changed = await db.ChatRuns.Where(x => x.Id == id && (x.Status == ChatRunStatus.Running || x.Status == ChatRunStatus.Cancelling))
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.Status, x => x.Status == ChatRunStatus.Cancelling ? ChatRunStatus.Cancelled : outcome)
                .SetProperty(x => x.ErrorCode, x => x.Status == ChatRunStatus.Cancelling ? "cancelled" : code)
                .SetProperty(x => x.EngineKind, engineKind)
                .SetProperty(x => x.InputTokens, usage != null ? usage.InputTokens : null)
                .SetProperty(x => x.OutputTokens, usage != null ? usage.OutputTokens : null)
                .SetProperty(x => x.EstimatedCostUsd, usage != null ? usage.EstimatedCostUsd : null)
                .SetProperty(x => x.CostSource, usage != null ? usage.CostSource : null)
                .SetProperty(x => x.DurationMs, durationMs)
                .SetProperty(x => x.Sequence, x => x.Sequence + 1)
                .SetProperty(x => x.UpdatedUtc, now).SetProperty(x => x.FinishedUtc, now), deadline.Token);
        if (changed > 0)
            await db.ChatMessages.Where(x => x.RunId == id && x.Role == ChatMessageRole.Assistant)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Text, text.Length > 131072 ? text.Substring(0, 131072) : text), deadline.Token);
        await transaction.CommitAsync(deadline.Token);
    }
}
