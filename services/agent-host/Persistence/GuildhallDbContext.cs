using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Persistence;

public sealed class ApplicationMetadata
{
    public int Id { get; set; }
    public Guid InstallationId { get; set; }
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
    public string? DefaultModelId { get; set; }
    public long Revision { get; set; }
}

public sealed class Conversation
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "New conversation";
    public Guid AgentDefinitionId { get; set; }
    public string? SelectedModelId { get; set; }
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
    public string? SettingsSnapshotJson { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public List<ChatMessage> Messages { get; set; } = [];
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

public sealed class GuildhallDbContext(DbContextOptions<GuildhallDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationMetadata> ApplicationMetadata => Set<ApplicationMetadata>();
    public DbSet<AgentDefinition> AgentDefinitions => Set<AgentDefinition>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatRun> ChatRuns => Set<ChatRun>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatAttachment> ChatAttachments => Set<ChatAttachment>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationMetadata>().ToTable("ApplicationMetadata");
        modelBuilder.Entity<ApplicationMetadata>().Property(x => x.Id).ValueGeneratedNever();
        modelBuilder.Entity<ApplicationMetadata>().Property(x => x.InstallationId).HasDefaultValueSql("NEWID()");
        modelBuilder.Entity<AgentDefinition>(e => {
            e.ToTable("AgentDefinitions"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Role).HasMaxLength(100).IsRequired(); e.Property(x => x.PortraitId).IsRequired(); e.Property(x => x.CustomPortrait).HasColumnType("nvarchar(max)"); e.Property(x => x.Accent).HasMaxLength(7).IsRequired(); e.Property(x => x.IsArchived).HasDefaultValue(false);
            e.Property(x => x.Instructions).HasMaxLength(8192).IsRequired(); e.Property(x => x.DefaultModelId).HasMaxLength(200); e.Property(x => x.Revision).IsConcurrencyToken();
        });
        modelBuilder.Entity<Conversation>(e => {
            e.ToTable("Conversations"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(200).IsRequired(); e.Property(x => x.SelectedModelId).HasMaxLength(200);
            e.HasIndex(x => new { x.UpdatedUtc, x.Id }); e.HasOne(x => x.AgentDefinition).WithMany().HasForeignKey(x => x.AgentDefinitionId).OnDelete(DeleteBehavior.Restrict); e.Property(x => x.Revision).IsConcurrencyToken();
        });
        modelBuilder.Entity<ChatRun>(e => {
            e.ToTable("ChatRuns"); e.HasKey(x => x.Id); e.Property(x => x.ClientRequestId).HasMaxLength(100).IsRequired(); e.Property(x => x.RequestFingerprint).HasMaxLength(128).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.ErrorCode).HasMaxLength(80); e.Property(x => x.ProviderId).HasMaxLength(50).IsRequired(); e.Property(x => x.ModelId).HasMaxLength(200).IsRequired(); e.Property(x => x.InstructionsSnapshot).HasMaxLength(8192).IsRequired(); e.Property(x => x.SettingsSnapshotJson).HasMaxLength(4000);
            e.HasIndex(x => new { x.ConversationId, x.TurnNumber }).IsUnique(); e.HasIndex(x => new { x.ConversationId, x.ClientRequestId }).IsUnique(); e.HasOne(x => x.Conversation).WithMany(x => x.Runs).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ChatMessage>(e => { e.ToTable("ChatMessages"); e.HasKey(x => x.Id); e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Text).HasMaxLength(131072).IsRequired(); e.HasIndex(x => new { x.RunId, x.Role }).IsUnique(); e.HasOne(x => x.Run).WithMany(x => x.Messages).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<ChatAttachment>(e => { e.ToTable("ChatAttachments"); e.HasKey(x => x.Id); e.Property(x => x.FileName).HasMaxLength(255).IsRequired(); e.Property(x => x.ContentType).HasMaxLength(160).IsRequired(); e.Property(x => x.Content).HasColumnType("varbinary(max)").IsRequired(); e.HasIndex(x => x.MessageId); e.HasOne(x => x.Message).WithMany(x => x.Attachments).HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade); });
    }
}
