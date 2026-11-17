using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Guildhall.AgentHost.Chat;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Providers;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Scheduling;

public sealed class DurableTaskSchedulerService(
    IDbContextFactory<GuildhallDbContext> dbFactory,
    ChatRunner chatRunner,
    Lazy<IContextBuilder> contextBuilder,
    TimeProvider timeProvider,
    ILogger<DurableTaskSchedulerService> logger,
    ConnectionCatalogService? connectionCatalog = null) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private DateTime? _lastTickUtc;

    public DateTime? LastTickUtc => _lastTickUtc;

    public void Wake() => _wake.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Durable task scheduler recovery is waiting for the database.");
                await Task.Delay(TimeSpan.FromSeconds(5), timeProvider, stoppingToken);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Durable task scheduler tick failed; will retry.");
            }

            try
            {
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var delayTask = Task.Delay(PollInterval, timeProvider, delayCts.Token);
                var wakeTask = _wake.Reader.WaitToReadAsync(delayCts.Token).AsTask();
                await Task.WhenAny(delayTask, wakeTask);
                delayCts.Cancel();
                while (_wake.Reader.TryRead(out _)) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var inflight = await db.DurableTaskRuns
            .Where(x => x.Status == "Running" || x.Status == "NeedsApproval")
            .ToListAsync(ct);

        if (inflight.Count == 0)
            return;

        var chatRunIds = inflight.Where(x => x.ChatRunId.HasValue).Select(x => x.ChatRunId!.Value).Distinct().ToList();
        var chatRuns = await db.ChatRuns.AsNoTracking()
            .Where(x => chatRunIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Status, x.ErrorCode, x.FinishedUtc })
            .ToDictionaryAsync(x => x.Id, ct);

        foreach (var taskRun in inflight)
        {
            if (taskRun.ChatRunId is null || !chatRuns.TryGetValue(taskRun.ChatRunId.Value, out var chatRun))
            {
                taskRun.Status = "Interrupted";
                taskRun.ErrorCode = "host_restarted";
                taskRun.FinishedUtc = now;
                taskRun.UpdatedUtc = now;
                continue;
            }

            if (chatRun.Status is ChatRunStatus.Completed or ChatRunStatus.Failed or ChatRunStatus.Cancelled or ChatRunStatus.Interrupted)
            {
                taskRun.Status = chatRun.Status.ToString();
                taskRun.ErrorCode = chatRun.ErrorCode;
                taskRun.FinishedUtc = chatRun.FinishedUtc ?? now;
                taskRun.UpdatedUtc = now;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task TickAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        _lastTickUtc = now;

        await EvaluateDueSchedulesAsync(now, ct);
        await DispatchQueuedRunsAsync(now, ct);
        await SyncRunningTasksAsync(now, ct);
    }

    private async Task EvaluateDueSchedulesAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var due = await db.ScheduledAutomations
            .Where(x => !x.IsPaused && x.NextRunUtc != null && x.NextRunUtc <= now)
            .OrderBy(x => x.NextRunUtc)
            .ToListAsync(ct);

        if (due.Count == 0)
            return;

        foreach (var automation in due)
        {
            if (!CronScheduleParser.TryParse(automation.CronExpression, out var cron, out _) || cron is null)
            {
                automation.IsPaused = true;
                automation.UpdatedUtc = now;
                continue;
            }

            var tz = ResolveTimezone(automation.TimezoneId);
            var fromExclusive = automation.LastEvaluatedUtc ?? automation.NextRunUtc!.Value.AddMinutes(-1);
            var cap = string.Equals(automation.MisfirePolicy, "CatchUpBounded", StringComparison.OrdinalIgnoreCase)
                ? Math.Clamp(automation.MaxCatchUpRuns, 1, 10)
                : 10;

            var occurrences = cron.EnumerateOccurrencesUtc(fromExclusive, now, tz, cap);
            var nextFuture = cron.GetNextOccurrenceUtc(now, tz);

            if (occurrences.Count > 0)
            {
                var policy = automation.MisfirePolicy;
                var isMisfireGap = (now - occurrences[0]) > TimeSpan.FromMinutes(2);

                if (string.Equals(policy, "Skip", StringComparison.OrdinalIgnoreCase) && isMisfireGap)
                {
                    db.DurableTaskRuns.Add(new DurableTaskRun
                    {
                        Id = Guid.NewGuid(),
                        ScheduledAutomationId = automation.Id,
                        AgentDefinitionId = automation.AgentDefinitionId,
                        TriggerKind = "CatchUp",
                        ScheduledForUtc = occurrences[^1],
                        Status = "SkippedMisfire",
                        ErrorCode = "misfire_skipped",
                        SummaryText = $"Skipped {occurrences.Count} missed run(s) after sleep or downtime.",
                        CreatedUtc = now,
                        FinishedUtc = now,
                        UpdatedUtc = now,
                    });
                }
                else if (string.Equals(policy, "CatchUpBounded", StringComparison.OrdinalIgnoreCase))
                {
                    for (var i = 0; i < occurrences.Count; i++)
                    {
                        var slot = occurrences[i];
                        db.DurableTaskRuns.Add(new DurableTaskRun
                        {
                            Id = Guid.NewGuid(),
                            ScheduledAutomationId = automation.Id,
                            AgentDefinitionId = automation.AgentDefinitionId,
                            TriggerKind = i < occurrences.Count - 1 || isMisfireGap ? "CatchUp" : "Scheduled",
                            ScheduledForUtc = slot,
                            Status = "Queued",
                            CreatedUtc = now,
                            UpdatedUtc = now,
                        });
                    }
                }
                else
                {
                    // Default: RunOnce
                    db.DurableTaskRuns.Add(new DurableTaskRun
                    {
                        Id = Guid.NewGuid(),
                        ScheduledAutomationId = automation.Id,
                        AgentDefinitionId = automation.AgentDefinitionId,
                        TriggerKind = isMisfireGap ? "CatchUp" : "Scheduled",
                        ScheduledForUtc = occurrences[^1],
                        Status = "Queued",
                        CreatedUtc = now,
                        UpdatedUtc = now,
                    });
                }
            }

            automation.LastEvaluatedUtc = now;
            automation.NextRunUtc = nextFuture;
            automation.UpdatedUtc = now;
            automation.Revision++;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task DispatchQueuedRunsAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var queuedRuns = await db.DurableTaskRuns
            .Where(x => x.Status == "Queued")
            .OrderBy(x => x.ScheduledForUtc)
            .ThenBy(x => x.CreatedUtc)
            .Take(5)
            .ToListAsync(ct);

        if (queuedRuns.Count == 0)
            return;

        foreach (var taskRun in queuedRuns)
        {
            var automation = taskRun.ScheduledAutomationId.HasValue
                ? await db.ScheduledAutomations.FirstOrDefaultAsync(x => x.Id == taskRun.ScheduledAutomationId.Value, ct)
                : null;

            if (automation is null)
            {
                taskRun.Status = "Failed";
                taskRun.ErrorCode = "schedule_not_found";
                taskRun.FinishedUtc = now;
                taskRun.UpdatedUtc = now;
                continue;
            }

            var agent = await db.AgentDefinitions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == automation.AgentDefinitionId, ct);

            if (agent is null || agent.IsArchived)
            {
                taskRun.Status = "Failed";
                taskRun.ErrorCode = agent is null ? "agent_not_found" : "member_archived";
                taskRun.FinishedUtc = now;
                taskRun.UpdatedUtc = now;
                continue;
            }

            await chatRunner.MutationGate.WaitAsync(ct);
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

                var providerId = string.IsNullOrWhiteSpace(agent.DefaultProviderId)
                    ? "ollama"
                    : agent.DefaultProviderId;
                var modelId = !string.IsNullOrWhiteSpace(agent.DefaultModelId)
                    ? agent.DefaultModelId
                    : (connectionCatalog is not null
                        ? await connectionCatalog.ResolveDefaultModelIdAsync(providerId, ct)
                        : Guildhall.AgentHost.Providers.Cli.CliHarnessDescriptors.Find(providerId)?.DefaultModelId);

                if (string.IsNullOrWhiteSpace(modelId))
                {
                    taskRun.Status = "Failed";
                    taskRun.ErrorCode = "model_missing";
                    taskRun.SummaryText = "No model is configured for this council member. Open the member profile or start a session to choose a default model.";
                    taskRun.FinishedUtc = now;
                    taskRun.UpdatedUtc = now;
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    continue;
                }

                var conversation = new Conversation
                {
                    Id = Guid.NewGuid(),
                    Title = $"Scheduled: {automation.Name}".Length > 200
                        ? $"Scheduled: {automation.Name}"[..200]
                        : $"Scheduled: {automation.Name}",
                    AgentDefinitionId = agent.Id,
                    SelectedProviderId = providerId,
                    SelectedModelId = modelId,
                    SelectedHarnessMode = agent.DefaultHarnessMode,
                    ProjectId = automation.ProjectId,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                    Revision = 1,
                };
                db.Conversations.Add(conversation);
                automation.ConversationId = conversation.Id;

                var assembly = await contextBuilder.Value.InspectAsync(db, conversation, automation.Prompt, null, ct);
                if (assembly.ErrorCode is not null)
                {
                    taskRun.Status = "Failed";
                    taskRun.ErrorCode = assembly.ErrorCode;
                    taskRun.SummaryText = assembly.ErrorMessage;
                    taskRun.FinishedUtc = now;
                    taskRun.UpdatedUtc = now;
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    continue;
                }

                if (automation.MaxBudgetTokens.HasValue &&
                    assembly.Budget.EstimatedNextTurnTokens > automation.MaxBudgetTokens.Value)
                {
                    taskRun.Status = "Failed";
                    taskRun.ErrorCode = "schedule_budget_exceeded";
                    taskRun.SummaryText = $"Estimated {assembly.Budget.EstimatedNextTurnTokens} tokens exceeds schedule cap of {automation.MaxBudgetTokens.Value}.";
                    taskRun.FinishedUtc = now;
                    taskRun.UpdatedUtc = now;
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    continue;
                }

                var turn = await db.ChatRuns.Where(x => x.ConversationId == conversation.Id)
                    .Select(x => (int?)x.TurnNumber).MaxAsync(ct) ?? 0;

                var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"sched:{taskRun.Id}:{automation.Prompt}")));
                var settingsJson = JsonSerializer.Serialize(new
                {
                    scheduledAutomationId = automation.Id,
                    durableTaskRunId = taskRun.Id,
                    permissionPolicy = conversation.PermissionPolicyOverride ?? agent.PermissionPolicy,
                    maxDurationSeconds = automation.MaxDurationSeconds,
                    budget = assembly.Budget,
                });

                var chatRun = new ChatRun
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversation.Id,
                    TurnNumber = turn + 1,
                    ClientRequestId = $"sched-{taskRun.Id:N}",
                    RequestFingerprint = fingerprint,
                    Status = ChatRunStatus.Queued,
                    StartedUtc = now,
                    UpdatedUtc = now,
                    InstructionsSnapshot = assembly.InstructionsSnapshot,
                    ModelId = modelId,
                    ProviderId = providerId,
                    SettingsSnapshotJson = settingsJson,
                };
                chatRun.Messages.Add(new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.User, Text = automation.Prompt });
                chatRun.Messages.Add(new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.Assistant, Text = string.Empty });
                db.ChatRuns.Add(chatRun);

                conversation.Revision++;
                conversation.UpdatedUtc = now;

                taskRun.ChatRunId = chatRun.Id;
                taskRun.Status = "Running";
                taskRun.AttemptCount++;
                taskRun.StartedUtc = now;
                taskRun.UpdatedUtc = now;

                automation.LastRunUtc = now;
                automation.UpdatedUtc = now;

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                chatRunner.Start(chatRun.Id);
            }
            finally
            {
                chatRunner.MutationGate.Release();
            }
        }
    }

    private async Task SyncRunningTasksAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var active = await db.DurableTaskRuns
            .Where(x => (x.Status == "Running" || x.Status == "NeedsApproval") && x.ChatRunId != null)
            .ToListAsync(ct);

        if (active.Count == 0)
            return;

        var chatRunIds = active.Select(x => x.ChatRunId!.Value).Distinct().ToList();
        var runs = await db.ChatRuns.AsNoTracking()
            .Where(x => chatRunIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.Status,
                x.ErrorCode,
                x.FinishedUtc,
                HasPendingApproval = x.Events.Any(e => e.Status == "PendingApproval"),
                AssistantText = x.Messages
                    .Where(m => m.Role == ChatMessageRole.Assistant)
                    .Select(m => m.Text)
                    .FirstOrDefault(),
            })
            .ToDictionaryAsync(x => x.Id, ct);

        var changed = false;
        foreach (var taskRun in active)
        {
            if (!runs.TryGetValue(taskRun.ChatRunId!.Value, out var run))
                continue;

            if (run.Status is ChatRunStatus.Completed or ChatRunStatus.Failed or ChatRunStatus.Cancelled or ChatRunStatus.Interrupted)
            {
                var reportedError = run.Status == ChatRunStatus.Completed && ContainsReportedError(run.AssistantText);
                taskRun.Status = reportedError ? "Failed" : run.Status.ToString();
                taskRun.ErrorCode = reportedError ? "task_reported_error" : run.ErrorCode;
                taskRun.FinishedUtc = run.FinishedUtc ?? now;
                taskRun.UpdatedUtc = now;
                if (!string.IsNullOrWhiteSpace(run.AssistantText))
                {
                    taskRun.SummaryText = StripThinkingAndFormatSummary(run.AssistantText);
                }
                else if (taskRun.Status == "Failed")
                {
                    taskRun.SummaryText = DescribeRunError(taskRun.ErrorCode);
                }
                changed = true;
            }
            else if (run.HasPendingApproval && taskRun.Status != "NeedsApproval")
            {
                taskRun.Status = "NeedsApproval";
                taskRun.UpdatedUtc = now;
                changed = true;
            }
            else if (!run.HasPendingApproval && taskRun.Status == "NeedsApproval")
            {
                taskRun.Status = "Running";
                taskRun.UpdatedUtc = now;
                changed = true;
            }
        }

        if (changed)
            await db.SaveChangesAsync(ct);
    }

    public static bool ContainsReportedError(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        Regex.IsMatch(text, @"<error>[\s\S]*?</error>", RegexOptions.IgnoreCase);

    public static string StripThinkingAndFormatSummary(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var withoutThinking = Regex.Replace(text, @"<think>[\s\S]*?(?:</think>|$)", string.Empty, RegexOptions.IgnoreCase);
        var formattedErrors = Regex.Replace(withoutThinking, @"<error>([\s\S]*?)</error>", "Error: $1", RegexOptions.IgnoreCase);
        var collapsed = Regex.Replace(formattedErrors, @"\s+", " ").Trim();
        return collapsed.Length > 400 ? collapsed[..400] + "…" : collapsed;
    }

    public static string DescribeRunError(string? errorCode) => errorCode switch
    {
        "task_reported_error" => "The council member reported that the task could not be completed with the available tools.",
        "model_missing" => "No model is configured for this council member. Select a default model or backing engine first.",
        "provider_unavailable" => "The configured backing engine is unavailable. Check Connections and try again.",
        "provider_auth_failed" => "Provider authentication failed. Check the configured API key or CLI sign-in in Connections.",
        "cli_not_installed" => "The configured CLI harness was not found on PATH. Check Connections to install it.",
        "generation_timeout" => "The run timed out before finishing.",
        "context_exceeded" => "The prompt and context exceeded the model's context window.",
        "schedule_budget_exceeded" => "Estimated tokens exceeded this schedule's token budget.",
        "provider_error" => "The backing model provider returned an error before producing output. Check that the model is installed and running.",
        null or "" => "The run failed before producing output.",
        _ => $"The run failed ({errorCode}).",
    };

    public static TimeZoneInfo ResolveTimezone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId) || string.Equals(timezoneId, "UTC", StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Utc;
        if (string.Equals(timezoneId, "Local", StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(timezoneId); }
        catch { return TimeZoneInfo.Utc; }
    }
}
