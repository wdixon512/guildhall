namespace Guildhall.AgentHost.Api;

public sealed record AssistantResponse(Guid Id, string Name, string Instructions, string? DefaultModelId, long Revision);
public sealed record UpdateAssistantRequest(string Instructions, string? DefaultModelId, long ExpectedRevision);
public sealed record AgentProfileResponse(Guid Id, string Name, string Role, int PortraitId, string? CustomPortrait, string Accent, string Instructions, string? DefaultModelId, long Revision, bool IsArchived);
public sealed record CreateAgentProfileRequest(string Name, string Role, int PortraitId, string? CustomPortrait, string Accent, string Instructions, string? DefaultModelId);
public sealed record UpdateAgentProfileRequest(string Name, string Role, int PortraitId, string? CustomPortrait, string Accent, string Instructions, string? DefaultModelId, long ExpectedRevision);
public sealed record GuildMemberLifecycleRequest(bool IsArchived, long ExpectedRevision);
public sealed record ConversationSummary(Guid Id, string Title, string? SelectedModelId, DateTime CreatedUtc, DateTime UpdatedUtc, bool IsArchived, DateTime? ArchivedUtc, bool HasActiveRun, long Revision, Guid AgentDefinitionId);
public sealed record CreateConversationRequest(string? Title, string? ModelId, Guid? AgentDefinitionId = null);
public sealed record UpdateConversationRequest(string? Title, string? ModelId, bool? IsArchived, long ExpectedRevision);
public sealed record AttachmentView(Guid Id, string FileName, string ContentType, long Length);
public sealed record MessageView(Guid Id, string Role, string Text, Guid RunId, string Status, int TurnNumber, IReadOnlyList<AttachmentView> Attachments, string? ErrorCode = null, string? ModelId = null);
public sealed record ConversationResponse(ConversationSummary Conversation, IReadOnlyList<MessageView> Messages, AgentProfileResponse Agent);
public sealed record CreateMessageRequest(string ClientRequestId, long ExpectedConversationRevision, string Text, string DeliveryMode = "queue");
public sealed record RunAcceptedResponse(Guid RunId, Guid AssistantMessageId, long ConversationRevision);
public sealed record ErrorResponse(string Code, string Message);
