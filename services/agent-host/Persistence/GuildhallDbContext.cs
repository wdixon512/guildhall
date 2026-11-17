using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Guildhall.AgentHost.Persistence;

public sealed class ApplicationMetadata
{
    public int Id { get; set; }
    public Guid InstallationId { get; set; }
}

public sealed class GuildhallBehaviorSettings
{
    public int Id { get; set; }
    public string Instructions { get; set; } = string.Empty;
    public string? WorkspacePolicyOverride { get; set; }
    public string? DefaultWorktreeRootPath { get; set; }
    public long Revision { get; set; }
}

public sealed class AgentDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "Local Assistant";
    public string Role { get; set; } = "Steward";
    public int PortraitId { get; set; }
    public string? CustomPortrait { get; set; }
    public string Accent { get; set; } = "#71465f";
    public bool IsArchived { get; set; }
    public string Instructions { get; set; } = string.Empty;
    public string DefaultProviderId { get; set; } = "ollama";
    public string? DefaultModelId { get; set; }
    public string? DefaultHarnessMode { get; set; }
    public string EnabledToolIdsJson { get; set; } = "[\"filesystem-mcp\",\"git-mcp\"]";
    public string PermissionPolicy { get; set; } = "ask_unsafe";
    public long Revision { get; set; }
}

public sealed class Conversation
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "New conversation";
    public Guid AgentDefinitionId { get; set; }
    public string? SelectedProviderId { get; set; }
    public string? SelectedModelId { get; set; }
    public string? SelectedHarnessMode { get; set; }
    public string? WorkspaceRootPath { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? ActiveWorktreeId { get; set; }
    public string? PermissionPolicyOverride { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedUtc { get; set; }
    public long Revision { get; set; }
    public AgentDefinition AgentDefinition { get; set; } = null!;
    public List<ChatRun> Runs { get; set; } = [];
}

public enum ChatRunStatus { Running, Cancelling, Completed, Failed, Cancelled, Interrupted, Queued }

public sealed class ChatRun
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public int TurnNumber { get; set; }
    public string ClientRequestId { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public ChatRunStatus Status { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public long Sequence { get; set; }
    public string? ErrorCode { get; set; }
    public string InstructionsSnapshot { get; set; } = string.Empty;
    public string ProviderId { get; set; } = "ollama";
    public string ModelId { get; set; } = string.Empty;
    public string EngineKind { get; set; } = "local";
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public string? CostSource { get; set; }
    public long? DurationMs { get; set; }
    public string? SettingsSnapshotJson { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public List<ChatMessage> Messages { get; set; } = [];
    public List<ChatRunEvent> Events { get; set; } = [];
}

public enum ChatMessageRole { User, Assistant }

public sealed class ChatMessage
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public ChatMessageRole Role { get; set; }
    public string Text { get; set; } = string.Empty;
    public ChatRun Run { get; set; } = null!;
    public List<ChatAttachment> Attachments { get; set; } = [];
}

public sealed class ChatAttachment
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Length { get; set; }
    public byte[] Content { get; set; } = [];
    public ChatMessage Message { get; set; } = null!;
}

public enum MemoryNoteScope { AboutYou, Member, Project }

public sealed class MemoryNote
{
    public Guid Id { get; set; }
    public MemoryNoteScope Scope { get; set; }
    public Guid? AgentDefinitionId { get; set; }
    public Guid? ProjectId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Guid? SourceConversationId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public long Revision { get; set; }
    public AgentDefinition? AgentDefinition { get; set; }
    public RegisteredProject? Project { get; set; }
}

public sealed class ChatRunEvent
{
    private readonly ILazyLoader? _lazyLoader;
    private ChatRun? _run;

    public ChatRunEvent() { }
    private ChatRunEvent(ILazyLoader lazyLoader) => _lazyLoader = lazyLoader;

    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public int SequenceNumber { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string SecurityTier { get; set; } = "Tier1";
    public string Status { get; set; } = "Running";
    public string ArgumentsRedactedJson { get; set; } = "{}";
    public string? ResultRedactedJson { get; set; }
    public string? ErrorCode { get; set; }
    public long? DurationMs { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public ChatRun Run
    {
        get => _lazyLoader.Load(this, ref _run)!;
        set => _run = value;
    }
}

public sealed class McpServerRegistration
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Transport { get; set; } = "stdio";
    public string? Command { get; set; }
    public string ArgumentsJson { get; set; } = "[]";
    public string? EndpointUrl { get; set; }
    public bool AllowPrivateTunnelHttp { get; set; }
    public string SecurityTier { get; set; } = "Tier3";
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public long Revision { get; set; }
}

public sealed class RegisteredProject
{
    private readonly ILazyLoader? _lazyLoader;
    private List<ProjectWorktree>? _worktrees;

    public RegisteredProject() { }
    private RegisteredProject(ILazyLoader lazyLoader) => _lazyLoader = lazyLoader;

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public bool IsGitRepository { get; set; }
    public string? DefaultBranch { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime LastOpenedUtc { get; set; }
    public long Revision { get; set; }

    public List<ProjectWorktree> Worktrees
    {
        get => _lazyLoader.Load(this, ref _worktrees)!;
        set => _worktrees = value;
    }
}

public sealed class ProjectWorktree
{
    private readonly ILazyLoader? _lazyLoader;
    private RegisteredProject? _project;

    public ProjectWorktree() { }
    private ProjectWorktree(ILazyLoader lazyLoader) => _lazyLoader = lazyLoader;

    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ConversationId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string WorktreePath { get; set; } = string.Empty;
    public string BaseCommitSha { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public RegisteredProject Project
    {
        get => _lazyLoader.Load(this, ref _project)!;
        set => _project = value;
    }
}

public sealed class ToolRollbackSnapshot
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid RunEventId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? WorktreeId { get; set; }
    public string Strategy { get; set; } = "FileBackup";
    public string? TargetPath { get; set; }
    public string? PreStateRef { get; set; }
    public string? PostStateRef { get; set; }
    public string Status { get; set; } = "Available";
    public DateTime CreatedUtc { get; set; }
    public DateTime? RolledBackUtc { get; set; }
}

public sealed class SqlMcpProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string AuthMode { get; set; } = "integrated";
    public string AllowedSchemasJson { get; set; } = "[\"dbo\"]";
    public string AllowedObjectsJson { get; set; } = "[]";
    public int MaxRows { get; set; } = 100;
    public int CommandTimeoutSeconds { get; set; } = 10;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public long Revision { get; set; }
}

public sealed class ScheduledAutomation
{
    private readonly ILazyLoader? _lazyLoader;
    private ICollection<DurableTaskRun>? _runs;

    public ScheduledAutomation() { }
    private ScheduledAutomation(ILazyLoader lazyLoader) => _lazyLoader = lazyLoader;

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid AgentDefinitionId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? ConversationId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string CronExpression { get; set; } = "0 * * * *";
    public string TimezoneId { get; set; } = "UTC";
    public string MisfirePolicy { get; set; } = "RunOnce";
    public int MaxCatchUpRuns { get; set; } = 3;
    public int? MaxBudgetTokens { get; set; }
    public int MaxDurationSeconds { get; set; } = 300;
    public bool IsPaused { get; set; }
    public DateTime? LastEvaluatedUtc { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public DateTime? NextRunUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public long Revision { get; set; }

    public ICollection<DurableTaskRun> Runs
    {
        get => _lazyLoader.Load(this, ref _runs) ?? (_runs ??= []);
        set => _runs = value;
    }
}

public sealed class DurableTaskRun
{
    private readonly ILazyLoader? _lazyLoader;
    private ScheduledAutomation? _scheduledAutomation;

    public DurableTaskRun() { }
    private DurableTaskRun(ILazyLoader lazyLoader) => _lazyLoader = lazyLoader;

    public Guid Id { get; set; }
    public Guid? ScheduledAutomationId { get; set; }
    public Guid AgentDefinitionId { get; set; }
    public Guid? ChatRunId { get; set; }
    public string TriggerKind { get; set; } = "Scheduled";
    public DateTime ScheduledForUtc { get; set; }
    public string Status { get; set; } = "Queued";
    public int AttemptCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? SummaryText { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public ScheduledAutomation? ScheduledAutomation
    {
        get => _lazyLoader.Load(this, ref _scheduledAutomation);
        set => _scheduledAutomation = value;
    }
}

public sealed class GuildhallDbContext(DbContextOptions<GuildhallDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationMetadata> ApplicationMetadata => Set<ApplicationMetadata>();
    public DbSet<GuildhallBehaviorSettings> GuildhallBehaviorSettings => Set<GuildhallBehaviorSettings>();
    public DbSet<AgentDefinition> AgentDefinitions => Set<AgentDefinition>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatRun> ChatRuns => Set<ChatRun>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatAttachment> ChatAttachments => Set<ChatAttachment>();
    public DbSet<MemoryNote> MemoryNotes => Set<MemoryNote>();
    public DbSet<ChatRunEvent> ChatRunEvents => Set<ChatRunEvent>();
    public DbSet<McpServerRegistration> McpServerRegistrations => Set<McpServerRegistration>();
    public DbSet<RegisteredProject> RegisteredProjects => Set<RegisteredProject>();
    public DbSet<ProjectWorktree> ProjectWorktrees => Set<ProjectWorktree>();
    public DbSet<ToolRollbackSnapshot> ToolRollbackSnapshots => Set<ToolRollbackSnapshot>();
    public DbSet<SqlMcpProfile> SqlMcpProfiles => Set<SqlMcpProfile>();
    public DbSet<ScheduledAutomation> ScheduledAutomations => Set<ScheduledAutomation>();
    public DbSet<DurableTaskRun> DurableTaskRuns => Set<DurableTaskRun>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationMetadata>().ToTable("ApplicationMetadata");
        modelBuilder.Entity<ApplicationMetadata>().Property(x => x.Id).ValueGeneratedNever();
        modelBuilder.Entity<ApplicationMetadata>().Property(x => x.InstallationId).HasDefaultValueSql("NEWID()");
        modelBuilder.Entity<GuildhallBehaviorSettings>(e => {
            e.ToTable("GuildhallBehaviorSettings"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Instructions).HasMaxLength(4000).IsRequired(); e.Property(x => x.WorkspacePolicyOverride).HasMaxLength(40); e.Property(x => x.DefaultWorktreeRootPath).HasMaxLength(1024); e.Property(x => x.Revision).IsConcurrencyToken();
        });
        modelBuilder.Entity<AgentDefinition>(e => {
            e.ToTable("AgentDefinitions"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Role).HasMaxLength(100).IsRequired(); e.Property(x => x.PortraitId).IsRequired(); e.Property(x => x.CustomPortrait).HasColumnType("nvarchar(max)"); e.Property(x => x.Accent).HasMaxLength(7).IsRequired(); e.Property(x => x.IsArchived).HasDefaultValue(false);
            e.Property(x => x.Instructions).HasMaxLength(8192).IsRequired(); e.Property(x => x.DefaultProviderId).HasMaxLength(50).IsRequired().HasDefaultValue("ollama"); e.Property(x => x.DefaultModelId).HasMaxLength(200); e.Property(x => x.DefaultHarnessMode).HasMaxLength(30); e.Property(x => x.EnabledToolIdsJson).HasMaxLength(2000).IsRequired().HasDefaultValue("[\"filesystem-mcp\",\"git-mcp\"]"); e.Property(x => x.PermissionPolicy).HasMaxLength(40).IsRequired().HasDefaultValue("ask_unsafe"); e.Property(x => x.Revision).IsConcurrencyToken();
        });
        modelBuilder.Entity<Conversation>(e => {
            e.ToTable("Conversations"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(200).IsRequired(); e.Property(x => x.SelectedProviderId).HasMaxLength(50); e.Property(x => x.SelectedModelId).HasMaxLength(200); e.Property(x => x.SelectedHarnessMode).HasMaxLength(30); e.Property(x => x.WorkspaceRootPath).HasMaxLength(500); e.Property(x => x.PermissionPolicyOverride).HasMaxLength(40);
            e.HasIndex(x => new { x.UpdatedUtc, x.Id }); e.HasOne(x => x.AgentDefinition).WithMany().HasForeignKey(x => x.AgentDefinitionId).OnDelete(DeleteBehavior.Restrict); e.Property(x => x.Revision).IsConcurrencyToken();
        });
        modelBuilder.Entity<ChatRun>(e => {
            e.ToTable("ChatRuns"); e.HasKey(x => x.Id); e.Property(x => x.ClientRequestId).HasMaxLength(100).IsRequired(); e.Property(x => x.RequestFingerprint).HasMaxLength(128).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.ErrorCode).HasMaxLength(80); e.Property(x => x.ProviderId).HasMaxLength(50).IsRequired(); e.Property(x => x.ModelId).HasMaxLength(200).IsRequired(); e.Property(x => x.EngineKind).HasMaxLength(20).IsRequired().HasDefaultValue("local"); e.Property(x => x.EstimatedCostUsd).HasColumnType("decimal(10,6)"); e.Property(x => x.CostSource).HasMaxLength(40); e.Property(x => x.InstructionsSnapshot).HasMaxLength(8192).IsRequired(); e.Property(x => x.SettingsSnapshotJson).HasMaxLength(4000);
            e.HasIndex(x => new { x.ConversationId, x.TurnNumber }).IsUnique(); e.HasIndex(x => new { x.ConversationId, x.ClientRequestId }).IsUnique(); e.HasOne(x => x.Conversation).WithMany(x => x.Runs).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ChatMessage>(e => { e.ToTable("ChatMessages"); e.HasKey(x => x.Id); e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Text).HasMaxLength(131072).IsRequired(); e.HasIndex(x => new { x.RunId, x.Role }).IsUnique(); e.HasOne(x => x.Run).WithMany(x => x.Messages).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<ChatAttachment>(e => { e.ToTable("ChatAttachments"); e.HasKey(x => x.Id); e.Property(x => x.FileName).HasMaxLength(255).IsRequired(); e.Property(x => x.ContentType).HasMaxLength(160).IsRequired(); e.Property(x => x.Content).HasColumnType("varbinary(max)").IsRequired(); e.HasIndex(x => x.MessageId); e.HasOne(x => x.Message).WithMany(x => x.Attachments).HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<MemoryNote>(e => {
            e.ToTable("MemoryNotes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Scope).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            e.Property(x => x.IsEnabled).HasDefaultValue(true);
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasIndex(x => new { x.Scope, x.AgentDefinitionId, x.ProjectId, x.IsEnabled, x.UpdatedUtc });
            e.HasOne(x => x.AgentDefinition).WithMany().HasForeignKey(x => x.AgentDefinitionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ChatRunEvent>(e => {
            e.ToTable("ChatRunEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(40).IsRequired();
            e.Property(x => x.ServerId).HasMaxLength(80).IsRequired();
            e.Property(x => x.ToolName).HasMaxLength(120).IsRequired();
            e.Property(x => x.SecurityTier).HasMaxLength(20).IsRequired().HasDefaultValue("Tier1");
            e.Property(x => x.Status).HasMaxLength(30).IsRequired().HasDefaultValue("Running");
            e.Property(x => x.ArgumentsRedactedJson).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.ResultRedactedJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.ErrorCode).HasMaxLength(80);
            e.HasIndex(x => new { x.RunId, x.SequenceNumber }).IsUnique();
            e.HasOne(x => x.Run).WithMany(x => x.Events).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<McpServerRegistration>(e => {
            e.ToTable("McpServerRegistrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(80);
            e.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Transport).HasMaxLength(30).IsRequired();
            e.Property(x => x.Command).HasMaxLength(500);
            e.Property(x => x.ArgumentsJson).HasMaxLength(2000).IsRequired().HasDefaultValue("[]");
            e.Property(x => x.EndpointUrl).HasMaxLength(500);
            e.Property(x => x.AllowPrivateTunnelHttp).HasDefaultValue(false);
            e.Property(x => x.SecurityTier).HasMaxLength(20).IsRequired().HasDefaultValue("Tier3");
            e.Property(x => x.IsEnabled).HasDefaultValue(true);
            e.Property(x => x.Revision).IsConcurrencyToken();
        });
        modelBuilder.Entity<RegisteredProject>(e => {
            e.ToTable("RegisteredProjects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.RootPath).HasMaxLength(500).IsRequired();
            e.Property(x => x.DefaultBranch).HasMaxLength(120);
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasIndex(x => x.RootPath).IsUnique();
        });
        modelBuilder.Entity<ProjectWorktree>(e => {
            e.ToTable("ProjectWorktrees");
            e.HasKey(x => x.Id);
            e.Property(x => x.BranchName).HasMaxLength(160).IsRequired();
            e.Property(x => x.WorktreePath).HasMaxLength(500).IsRequired();
            e.Property(x => x.BaseCommitSha).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasMaxLength(30).IsRequired().HasDefaultValue("Active");
            e.HasIndex(x => new { x.ConversationId, x.Status });
            e.HasOne(x => x.Project).WithMany(x => x.Worktrees).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ToolRollbackSnapshot>(e => {
            e.ToTable("ToolRollbackSnapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Strategy).HasMaxLength(30).IsRequired();
            e.Property(x => x.TargetPath).HasMaxLength(500);
            e.Property(x => x.PreStateRef).HasColumnType("nvarchar(max)");
            e.Property(x => x.PostStateRef).HasColumnType("nvarchar(max)");
            e.Property(x => x.Status).HasMaxLength(30).IsRequired().HasDefaultValue("Available");
            e.HasIndex(x => new { x.RunId, x.RunEventId });
        });
        modelBuilder.Entity<SqlMcpProfile>(e => {
            e.ToTable("SqlMcpProfiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Server).HasMaxLength(200).IsRequired();
            e.Property(x => x.Database).HasMaxLength(128).IsRequired();
            e.Property(x => x.AuthMode).HasMaxLength(30).IsRequired().HasDefaultValue("integrated");
            e.Property(x => x.AllowedSchemasJson).HasMaxLength(2000).IsRequired().HasDefaultValue("[\"dbo\"]");
            e.Property(x => x.AllowedObjectsJson).HasMaxLength(4000).IsRequired().HasDefaultValue("[]");
            e.Property(x => x.MaxRows).HasDefaultValue(100);
            e.Property(x => x.CommandTimeoutSeconds).HasDefaultValue(10);
            e.Property(x => x.IsEnabled).HasDefaultValue(true);
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasIndex(x => x.Name).IsUnique();
        });
        modelBuilder.Entity<ScheduledAutomation>(e => {
            e.ToTable("ScheduledAutomations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Prompt).HasMaxLength(8000).IsRequired();
            e.Property(x => x.CronExpression).HasMaxLength(100).IsRequired();
            e.Property(x => x.TimezoneId).HasMaxLength(100).IsRequired().HasDefaultValue("UTC");
            e.Property(x => x.MisfirePolicy).HasMaxLength(30).IsRequired().HasDefaultValue("RunOnce");
            e.Property(x => x.MaxCatchUpRuns).HasDefaultValue(3);
            e.Property(x => x.MaxDurationSeconds).HasDefaultValue(300);
            e.Property(x => x.IsPaused).HasDefaultValue(false);
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasIndex(x => new { x.IsPaused, x.NextRunUtc });
            e.HasOne<AgentDefinition>().WithMany().HasForeignKey(x => x.AgentDefinitionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RegisteredProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<DurableTaskRun>(e => {
            e.ToTable("DurableTaskRuns");
            e.HasKey(x => x.Id);
            e.Property(x => x.TriggerKind).HasMaxLength(30).IsRequired().HasDefaultValue("Scheduled");
            e.Property(x => x.Status).HasMaxLength(30).IsRequired().HasDefaultValue("Queued");
            e.Property(x => x.ErrorCode).HasMaxLength(80);
            e.Property(x => x.SummaryText).HasMaxLength(4000);
            e.HasIndex(x => new { x.ScheduledAutomationId, x.CreatedUtc });
            e.HasIndex(x => new { x.Status, x.ScheduledForUtc });
            e.HasOne(x => x.ScheduledAutomation).WithMany(x => x.Runs).HasForeignKey(x => x.ScheduledAutomationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ChatRun>().WithMany().HasForeignKey(x => x.ChatRunId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
