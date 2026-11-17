namespace Guildhall.AgentHost.Api;

public sealed record ScheduledAutomationView(
    Guid Id,
    string Name,
    Guid AgentDefinitionId,
    string AgentName,
    Guid? ProjectId,
    Guid? ConversationId,
    string Prompt,
    string CronExpression,
    string TimezoneId,
    string MisfirePolicy,
    int MaxCatchUpRuns,
    int? MaxBudgetTokens,
    int MaxDurationSeconds,
    bool IsPaused,
    DateTime? LastRunUtc,
    DateTime? NextRunUtc,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    long Revision,
    string? LastRunStatus = null);

public sealed record CreateScheduledAutomationRequest(
    string Name,
    Guid AgentDefinitionId,
    string Prompt,
    string CronExpression,
    string? TimezoneId = null,
    string? MisfirePolicy = null,
    int? MaxCatchUpRuns = null,
    int? MaxBudgetTokens = null,
    int? MaxDurationSeconds = null,
    Guid? ProjectId = null);

public sealed record UpdateScheduledAutomationRequest(
    long ExpectedRevision,
    string? Name = null,
    Guid? AgentDefinitionId = null,
    string? Prompt = null,
    string? CronExpression = null,
    string? TimezoneId = null,
    string? MisfirePolicy = null,
    int? MaxCatchUpRuns = null,
    int? MaxBudgetTokens = null,
    int? MaxDurationSeconds = null,
    bool? IsPaused = null,
    Guid? ProjectId = null);

public sealed record DurableTaskRunView(
    Guid Id,
    Guid? ScheduledAutomationId,
    Guid AgentDefinitionId,
    Guid? ChatRunId,
    Guid? ConversationId,
    string TriggerKind,
    DateTime ScheduledForUtc,
    string Status,
    int AttemptCount,
    string? ErrorCode,
    string? SummaryText,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    int? InputTokens = null,
    int? OutputTokens = null,
    long? DurationMs = null);
