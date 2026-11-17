using System.Text.Json;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Api;

public static class ScheduleEndpoints
{
    private static readonly string[] ValidMisfirePolicies = ["Skip", "RunOnce", "CatchUpBounded"];

    public static IEndpointRouteBuilder MapScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/schedules", async (IDbContextFactory<GuildhallDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var rows = await db.ScheduledAutomations.AsNoTracking()
                .OrderBy(x => x.IsPaused)
                .ThenBy(x => x.Name)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.AgentDefinitionId,
                    AgentName = db.AgentDefinitions.Where(a => a.Id == x.AgentDefinitionId).Select(a => a.Name).FirstOrDefault() ?? "Member",
                    x.ProjectId,
                    x.ConversationId,
                    x.Prompt,
                    x.CronExpression,
                    x.TimezoneId,
                    x.MisfirePolicy,
                    x.MaxCatchUpRuns,
                    x.MaxBudgetTokens,
                    x.MaxDurationSeconds,
                    x.IsPaused,
                    x.LastRunUtc,
                    x.NextRunUtc,
                    x.CreatedUtc,
                    x.UpdatedUtc,
                    x.Revision,
                    LastRunStatus = x.Runs.OrderByDescending(r => r.CreatedUtc).Select(r => r.Status).FirstOrDefault()
                })
                .ToListAsync(ct);

            return Results.Ok(rows.Select(x => new ScheduledAutomationView(
                x.Id, x.Name, x.AgentDefinitionId, x.AgentName, x.ProjectId, x.ConversationId,
                x.Prompt, x.CronExpression, x.TimezoneId, x.MisfirePolicy, x.MaxCatchUpRuns,
                x.MaxBudgetTokens, x.MaxDurationSeconds, x.IsPaused, AsUtc(x.LastRunUtc), AsUtc(x.NextRunUtc),
                AsUtc(x.CreatedUtc), AsUtc(x.UpdatedUtc), x.Revision, x.LastRunStatus)).ToList());
        });

        app.MapPost("/schedules", async (
            CreateScheduledAutomationRequest request,
            IDbContextFactory<GuildhallDbContext> dbFactory,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var name = request.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
                return Results.BadRequest(new ErrorResponse("invalid_schedule", "Schedule name must be 1–120 characters."));

            var prompt = request.Prompt?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 8000)
                return Results.BadRequest(new ErrorResponse("invalid_schedule", "Prompt must be 1–8000 characters."));

            if (!CronScheduleParser.TryParse(request.CronExpression, out var cron, out var cronError) || cron is null)
                return Results.BadRequest(new ErrorResponse("invalid_cron", cronError ?? "Invalid cron expression."));

            var misfire = NormalizeMisfire(request.MisfirePolicy);
            if (misfire is null)
                return Results.BadRequest(new ErrorResponse("invalid_schedule", "MisfirePolicy must be Skip, RunOnce, or CatchUpBounded."));

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var agent = await db.AgentDefinitions.AsNoTracking()
                .Where(x => x.Id == request.AgentDefinitionId)
                .Select(x => new { x.Id, x.Name, x.IsArchived })
                .SingleOrDefaultAsync(ct);

            if (agent is null)
                return Results.BadRequest(new ErrorResponse("agent_not_found", "Selected council member was not found."));
            if (agent.IsArchived)
                return Results.Conflict(new ErrorResponse("member_archived", "Restore this council member before scheduling tasks."));

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var tzId = string.IsNullOrWhiteSpace(request.TimezoneId) ? "UTC" : request.TimezoneId.Trim();
            var tz = DurableTaskSchedulerService.ResolveTimezone(tzId);
            var nextRun = cron.GetNextOccurrenceUtc(now, tz);

            var entity = new ScheduledAutomation
            {
                Id = Guid.NewGuid(),
                Name = name,
                AgentDefinitionId = agent.Id,
                ProjectId = request.ProjectId == Guid.Empty ? null : request.ProjectId,
                Prompt = prompt,
                CronExpression = request.CronExpression.Trim(),
                TimezoneId = tzId,
                MisfirePolicy = misfire,
                MaxCatchUpRuns = Math.Clamp(request.MaxCatchUpRuns ?? 3, 1, 10),
                MaxBudgetTokens = request.MaxBudgetTokens is > 0 ? request.MaxBudgetTokens : null,
                MaxDurationSeconds = Math.Clamp(request.MaxDurationSeconds ?? 300, 30, 3600),
                IsPaused = false,
                LastEvaluatedUtc = now,
                NextRunUtc = nextRun,
                CreatedUtc = now,
                UpdatedUtc = now,
                Revision = 1,
            };

            db.ScheduledAutomations.Add(entity);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/schedules/{entity.Id}", new ScheduledAutomationView(
                entity.Id, entity.Name, entity.AgentDefinitionId, agent.Name, entity.ProjectId, entity.ConversationId,
                entity.Prompt, entity.CronExpression, entity.TimezoneId, entity.MisfirePolicy, entity.MaxCatchUpRuns,
                entity.MaxBudgetTokens, entity.MaxDurationSeconds, entity.IsPaused, AsUtc(entity.LastRunUtc), AsUtc(entity.NextRunUtc),
                AsUtc(entity.CreatedUtc), AsUtc(entity.UpdatedUtc), entity.Revision));
        });

        app.MapPatch("/schedules/{id:guid}", async (
            Guid id,
            UpdateScheduledAutomationRequest request,
            IDbContextFactory<GuildhallDbContext> dbFactory,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var entity = await db.ScheduledAutomations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null) return Results.NotFound();
            if (entity.Revision != request.ExpectedRevision)
                return Results.Conflict(new ErrorResponse("revision_conflict", "Schedule changed; refresh and retry."));

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var recomputeNext = false;

            if (request.Name is not null)
            {
                var trimmed = request.Name.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 120)
                    return Results.BadRequest(new ErrorResponse("invalid_schedule", "Schedule name must be 1–120 characters."));
                entity.Name = trimmed;
            }
            if (request.AgentDefinitionId.HasValue && request.AgentDefinitionId.Value != entity.AgentDefinitionId)
            {
                var nextAgent = await db.AgentDefinitions.AsNoTracking()
                    .Where(x => x.Id == request.AgentDefinitionId.Value)
                    .Select(x => new { x.Id, x.IsArchived })
                    .SingleOrDefaultAsync(ct);
                if (nextAgent is null)
                    return Results.BadRequest(new ErrorResponse("agent_not_found", "Selected council member was not found."));
                if (nextAgent.IsArchived)
                    return Results.Conflict(new ErrorResponse("member_archived", "Restore this council member before scheduling tasks."));
                entity.AgentDefinitionId = nextAgent.Id;
            }
            if (request.Prompt is not null)
            {
                var trimmed = request.Prompt.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 8000)
                    return Results.BadRequest(new ErrorResponse("invalid_schedule", "Prompt must be 1–8000 characters."));
                entity.Prompt = trimmed;
            }
            if (request.CronExpression is not null)
            {
                if (!CronScheduleParser.TryParse(request.CronExpression, out _, out var err))
                    return Results.BadRequest(new ErrorResponse("invalid_cron", err ?? "Invalid cron expression."));
                entity.CronExpression = request.CronExpression.Trim();
                recomputeNext = true;
            }
            if (request.TimezoneId is not null)
            {
                entity.TimezoneId = string.IsNullOrWhiteSpace(request.TimezoneId) ? "UTC" : request.TimezoneId.Trim();
                recomputeNext = true;
            }
            if (request.MisfirePolicy is not null)
            {
                var misfire = NormalizeMisfire(request.MisfirePolicy);
                if (misfire is null)
                    return Results.BadRequest(new ErrorResponse("invalid_schedule", "Invalid MisfirePolicy."));
                entity.MisfirePolicy = misfire;
            }
            if (request.MaxCatchUpRuns.HasValue)
                entity.MaxCatchUpRuns = Math.Clamp(request.MaxCatchUpRuns.Value, 1, 10);
            if (request.MaxBudgetTokens.HasValue)
                entity.MaxBudgetTokens = request.MaxBudgetTokens.Value > 0 ? request.MaxBudgetTokens.Value : null;
            if (request.MaxDurationSeconds.HasValue)
                entity.MaxDurationSeconds = Math.Clamp(request.MaxDurationSeconds.Value, 30, 3600);
            if (request.ProjectId.HasValue)
                entity.ProjectId = request.ProjectId.Value == Guid.Empty ? null : request.ProjectId.Value;

            if (request.IsPaused.HasValue)
            {
                if (entity.IsPaused && !request.IsPaused.Value)
                {
                    entity.LastEvaluatedUtc = now;
                    recomputeNext = true;
                }
                entity.IsPaused = request.IsPaused.Value;
            }

            if (recomputeNext && !entity.IsPaused && CronScheduleParser.TryParse(entity.CronExpression, out var parsed, out _) && parsed is not null)
            {
                var tz = DurableTaskSchedulerService.ResolveTimezone(entity.TimezoneId);
                entity.NextRunUtc = parsed.GetNextOccurrenceUtc(now, tz);
            }

            entity.UpdatedUtc = now;
            entity.Revision++;
            await db.SaveChangesAsync(ct);

            var agentName = await db.AgentDefinitions.AsNoTracking()
                .Where(a => a.Id == entity.AgentDefinitionId).Select(a => a.Name).FirstOrDefaultAsync(ct) ?? "Member";

            return Results.Ok(new ScheduledAutomationView(
                entity.Id, entity.Name, entity.AgentDefinitionId, agentName, entity.ProjectId, entity.ConversationId,
                entity.Prompt, entity.CronExpression, entity.TimezoneId, entity.MisfirePolicy, entity.MaxCatchUpRuns,
                entity.MaxBudgetTokens, entity.MaxDurationSeconds, entity.IsPaused, AsUtc(entity.LastRunUtc), AsUtc(entity.NextRunUtc),
                AsUtc(entity.CreatedUtc), AsUtc(entity.UpdatedUtc), entity.Revision));
        });

        app.MapDelete("/schedules/{id:guid}", async (Guid id, IDbContextFactory<GuildhallDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var deleted = await db.ScheduledAutomations.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        });

        app.MapPost("/schedules/{id:guid}/run-now", async (
            Guid id,
            IDbContextFactory<GuildhallDbContext> dbFactory,
            DurableTaskSchedulerService scheduler,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var automation = await db.ScheduledAutomations.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { x.Id, x.AgentDefinitionId, x.ConversationId })
                .SingleOrDefaultAsync(ct);

            if (automation is null)
                return Results.NotFound();

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var taskRun = new DurableTaskRun
            {
                Id = Guid.NewGuid(),
                ScheduledAutomationId = automation.Id,
                AgentDefinitionId = automation.AgentDefinitionId,
                TriggerKind = "Manual",
                ScheduledForUtc = now,
                Status = "Queued",
                AttemptCount = 0,
                CreatedUtc = now,
                UpdatedUtc = now,
            };

            db.DurableTaskRuns.Add(taskRun);
            await db.SaveChangesAsync(ct);
            scheduler.Wake();

            return Results.Accepted($"/schedules/runs/{taskRun.Id}/stream", new DurableTaskRunView(
                taskRun.Id, taskRun.ScheduledAutomationId, taskRun.AgentDefinitionId, taskRun.ChatRunId,
                automation.ConversationId, taskRun.TriggerKind, AsUtc(taskRun.ScheduledForUtc), taskRun.Status,
                taskRun.AttemptCount, taskRun.ErrorCode, taskRun.SummaryText, AsUtc(taskRun.CreatedUtc),
                AsUtc(taskRun.StartedUtc), AsUtc(taskRun.FinishedUtc)));
        });

        app.MapGet("/schedules/{id:guid}/runs", async (Guid id, IDbContextFactory<GuildhallDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var rows = await db.DurableTaskRuns.AsNoTracking()
                .Where(x => x.ScheduledAutomationId == id)
                .OrderByDescending(x => x.CreatedUtc)
                .Take(50)
                .Select(x => new
                {
                    x.Id,
                    x.ScheduledAutomationId,
                    x.AgentDefinitionId,
                    x.ChatRunId,
                    ConversationId = x.ChatRunId != null
                        ? db.ChatRuns.Where(r => r.Id == x.ChatRunId).Select(r => (Guid?)r.ConversationId).FirstOrDefault()
                        : null,
                    x.TriggerKind,
                    x.ScheduledForUtc,
                    x.Status,
                    x.AttemptCount,
                    x.ErrorCode,
                    x.SummaryText,
                    x.CreatedUtc,
                    x.StartedUtc,
                    x.FinishedUtc,
                    InputTokens = x.ChatRunId != null
                        ? db.ChatRuns.Where(r => r.Id == x.ChatRunId).Select(r => r.InputTokens).FirstOrDefault()
                        : null,
                    OutputTokens = x.ChatRunId != null
                        ? db.ChatRuns.Where(r => r.Id == x.ChatRunId).Select(r => r.OutputTokens).FirstOrDefault()
                        : null,
                    DurationMs = x.ChatRunId != null
                        ? db.ChatRuns.Where(r => r.Id == x.ChatRunId).Select(r => r.DurationMs).FirstOrDefault()
                        : null,
                })
                .ToListAsync(ct);

            return Results.Ok(rows.Select(x =>
            {
                var reportedError = x.Status == "Completed" && DurableTaskSchedulerService.ContainsReportedError(x.SummaryText);
                var status = reportedError ? "Failed" : x.Status;
                var errorCode = reportedError ? "task_reported_error" : x.ErrorCode;
                var summary = string.IsNullOrWhiteSpace(x.SummaryText)
                    ? x.SummaryText
                    : DurableTaskSchedulerService.StripThinkingAndFormatSummary(x.SummaryText);
                return new DurableTaskRunView(
                    x.Id, x.ScheduledAutomationId, x.AgentDefinitionId, x.ChatRunId, x.ConversationId,
                    x.TriggerKind, AsUtc(x.ScheduledForUtc), status, x.AttemptCount, errorCode, summary,
                    AsUtc(x.CreatedUtc), AsUtc(x.StartedUtc), AsUtc(x.FinishedUtc), x.InputTokens, x.OutputTokens, x.DurationMs);
            }).ToList());
        });

        app.MapGet("/schedules/runs/{taskRunId:guid}/stream", async (
            Guid taskRunId,
            HttpContext http,
            IDbContextFactory<GuildhallDbContext> dbFactory,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            http.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";

            string? lastSignature = null;
            while (!ct.IsCancellationRequested)
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var taskRun = await db.DurableTaskRuns.AsNoTracking()
                    .Where(x => x.Id == taskRunId)
                    .Select(x => new
                    {
                        x.Id,
                        x.Status,
                        x.ChatRunId,
                        x.ErrorCode,
                        x.SummaryText,
                        x.StartedUtc,
                        x.FinishedUtc,
                        x.UpdatedUtc,
                    })
                    .SingleOrDefaultAsync(ct);

                if (taskRun is null)
                {
                    await http.Response.WriteAsync("event: task.error\ndata: {\"code\":\"not_found\"}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                    return;
                }

                string? assistantText = null;
                Guid? conversationId = null;
                long sequence = 0;
                if (taskRun.ChatRunId.HasValue)
                {
                    var chat = await db.ChatRuns.AsNoTracking()
                        .Where(r => r.Id == taskRun.ChatRunId.Value)
                        .Select(r => new
                        {
                            r.ConversationId,
                            r.Sequence,
                            AssistantText = r.Messages.Where(m => m.Role == ChatMessageRole.Assistant).Select(m => m.Text).FirstOrDefault(),
                        })
                        .SingleOrDefaultAsync(ct);

                    if (chat is not null)
                    {
                        conversationId = chat.ConversationId;
                        sequence = chat.Sequence;
                        assistantText = chat.AssistantText;
                    }
                }

                var reportedError = taskRun.Status == "Completed" &&
                    DurableTaskSchedulerService.ContainsReportedError(assistantText ?? taskRun.SummaryText);
                var effectiveStatus = reportedError ? "Failed" : taskRun.Status;
                var effectiveErrorCode = reportedError ? "task_reported_error" : taskRun.ErrorCode;

                var sig = $"{effectiveStatus}:{sequence}:{taskRun.UpdatedUtc.Ticks}";
                if (sig != lastSignature)
                {
                    lastSignature = sig;
                    var payload = JsonSerializer.Serialize(new
                    {
                        taskRunId = taskRun.Id,
                        status = effectiveStatus,
                        chatRunId = taskRun.ChatRunId,
                        conversationId,
                        text = assistantText ?? taskRun.SummaryText ?? string.Empty,
                        errorCode = effectiveErrorCode,
                        startedUtc = taskRun.StartedUtc,
                        finishedUtc = taskRun.FinishedUtc,
                    });
                    await http.Response.WriteAsync($"event: task.progress\ndata: {payload}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                }

                if (effectiveStatus is "Completed" or "Failed" or "Cancelled" or "Interrupted" or "SkippedMisfire")
                {
                    var donePayload = JsonSerializer.Serialize(new
                    {
                        status = effectiveStatus,
                        errorCode = effectiveErrorCode,
                        summaryText = taskRun.SummaryText,
                    });
                    await http.Response.WriteAsync($"event: task.done\ndata: {donePayload}\n\n", ct);
                    await http.Response.Body.FlushAsync(ct);
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(350), timeProvider, ct);
            }
        });

        return app;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) =>
        value.HasValue ? AsUtc(value.Value) : null;

    private static string? NormalizeMisfire(string? policy)
    {
        if (string.IsNullOrWhiteSpace(policy)) return "RunOnce";
        return ValidMisfirePolicies.FirstOrDefault(p => string.Equals(p, policy.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
