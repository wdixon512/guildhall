using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Chat;

namespace Guildhall.AgentHost.Api;

public static class ChatEndpoints
{
    private static readonly Guid AssistantId = Guid.Parse("7e3b38f2-2a51-4d15-93c2-0f8f7b8cf1c2");
    private const int MaxTextBytes = 16 * 1024;
    private const int MaxAttachmentBytes = 20 * 1024 * 1024;
    private const long MaxMessageAttachmentsBytes = 50 * 1024 * 1024;
    private const int MaxAttachments = 5;

    public static void MapChat(this WebApplication app)
    {
        app.MapGet("/guildhall-behavior", async (IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var settings = await db.GuildhallBehaviorSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
            return settings is null ? Results.NotFound(new ErrorResponse("settings_missing", "Guildhall behavior settings have not been initialized by the database migration.")) : Results.Ok(new GuildhallBehaviorResponse(settings.Instructions, settings.Revision));
        });
        app.MapPut("/guildhall-behavior", async (UpdateGuildhallBehaviorRequest request, IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Instructions) || request.Instructions.Length > 4000)
                return Results.BadRequest(new ErrorResponse("invalid_behavior", "Guildhall behavior must contain 1–4000 characters."));
            await using var db = await factory.CreateDbContextAsync(ct);
            var settings = await db.GuildhallBehaviorSettings.SingleOrDefaultAsync(x => x.Id == 1, ct);
            if (settings is null) return Results.NotFound(new ErrorResponse("settings_missing", "Guildhall behavior settings have not been initialized by the database migration."));
            if (settings.Revision != request.ExpectedRevision) return Results.Conflict(new ErrorResponse("revision_conflict", "Guildhall behavior changed. Reload it before saving."));
            settings.Instructions = request.Instructions;
            settings.Revision++;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new ErrorResponse("revision_conflict", "Guildhall behavior changed. Reload it before saving.")); }
            return Results.Ok(new GuildhallBehaviorResponse(settings.Instructions, settings.Revision));
        });
        app.MapPost("/runs/{id:guid}/cancel", async (Guid id, ChatRunner runner, CancellationToken ct) =>
            await runner.CancelAsync(id, ct) ? Results.Ok(new { runId = id }) : Results.NotFound());
        app.MapPost("/runs/{id:guid}/promote", async (Guid id, PromoteRunRequest request, IDbContextFactory<GuildhallDbContext> factory, ChatRunner runner, CancellationToken ct) =>
        {
            await runner.MutationGate.WaitAsync(ct);
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
                var run = await db.ChatRuns.SingleOrDefaultAsync(x => x.Id == id, ct);
                if (run is null) return Results.NotFound();
                var conversation = await db.Conversations.SingleAsync(x => x.Id == run.ConversationId, ct);
                if (conversation.Revision != request.ExpectedConversationRevision)
                    return Results.Conflict(new ErrorResponse("revision_conflict", "Conversation changed; refresh and retry."));
                if (run.Status != ChatRunStatus.Queued)
                    return Results.Conflict(new ErrorResponse("run_not_queued", "Only a queued reply can be moved ahead."));

                var queued = await db.ChatRuns.AsNoTracking().Where(x => x.ConversationId == run.ConversationId && x.Status == ChatRunStatus.Queued)
                    .OrderBy(x => x.TurnNumber).Select(x => new { x.Id, x.TurnNumber }).ToListAsync(ct);
                var reservedTurns = queued.Select(x => x.TurnNumber).ToArray();
                var position = 0;
                await db.ChatRuns.Where(x => x.ConversationId == run.ConversationId && x.Status == ChatRunStatus.Queued)
                    .ExecuteUpdateAsync(set => set.SetProperty(x => x.TurnNumber, x => -x.TurnNumber), ct);
                foreach (var pending in queued.OrderBy(x => x.Id == id ? 0 : 1).ThenBy(x => x.TurnNumber))
                {
                    // Reuse only queued slots; terminal/removed turns retain their numbers.
                    var assignedTurn = reservedTurns[position++];
                    await db.ChatRuns.Where(x => x.Id == pending.Id)
                        .ExecuteUpdateAsync(set => set.SetProperty(x => x.TurnNumber, assignedTurn), ct);
                }

                conversation.Revision++;
                conversation.UpdatedUtc = DateTime.UtcNow;
                var activeRunId = await db.ChatRuns.Where(x => x.ConversationId == run.ConversationId &&
                        (x.Status == ChatRunStatus.Running || x.Status == ChatRunStatus.Cancelling))
                    .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                if (activeRunId.HasValue) await runner.CancelAsync(activeRunId.Value, ct);
                runner.Start(id);
                return Results.Ok(new { runId = id, conversationRevision = conversation.Revision });
            }
            finally { runner.MutationGate.Release(); }
        });
        app.MapGet("/assistant", async (IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var item = await db.AgentDefinitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == AssistantId, ct);
            return item is null ? Results.NotFound(new ErrorResponse("not_found", "Assistant is not initialized.")) : Results.Ok(ToResponse(item));
        });
        app.MapGet("/agents", async (IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var profiles = await db.AgentDefinitions.AsNoTracking().Where(x => x.Id != AssistantId || db.Conversations.Any(c => c.AgentDefinitionId == AssistantId)).OrderBy(x => x.IsArchived).ThenBy(x => x.Name).ThenBy(x => x.Id).Select(x => new AgentProfileResponse(x.Id, x.Name, x.Role, x.PortraitId, x.CustomPortrait, x.Accent, x.Instructions, x.DefaultModelId, x.Revision, x.IsArchived)).ToListAsync(ct);
            return Results.Ok(profiles);
        });
        app.MapPost("/agents", async (CreateAgentProfileRequest request, IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) =>
        {
            var invalid = ValidateProfile(request.Name, request.Role, request.PortraitId, request.CustomPortrait, request.Accent, request.Instructions, request.DefaultModelId);
            if (invalid is not null) return Results.BadRequest(new ErrorResponse("invalid_profile", invalid));
            await using var db = await factory.CreateDbContextAsync(ct);
            var profile = new AgentDefinition { Id = Guid.NewGuid(), Name = request.Name.Trim(), Role = request.Role.Trim(), PortraitId = request.PortraitId, CustomPortrait = request.CustomPortrait, Accent = request.Accent, Instructions = request.Instructions, DefaultModelId = string.IsNullOrWhiteSpace(request.DefaultModelId) ? null : request.DefaultModelId.Trim(), Revision = 1 };
            db.AgentDefinitions.Add(profile); await db.SaveChangesAsync(ct);
            return Results.Created($"/agents/{profile.Id}", ToProfile(profile));
        });
        app.MapPatch("/agents/{id:guid}", async (Guid id, UpdateAgentProfileRequest request, IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) =>
        {
            var invalid = ValidateProfile(request.Name, request.Role, request.PortraitId, request.CustomPortrait, request.Accent, request.Instructions, request.DefaultModelId);
            if (invalid is not null) return Results.BadRequest(new ErrorResponse("invalid_profile", invalid));
            await using var db = await factory.CreateDbContextAsync(ct);
            var profile = await db.AgentDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (profile is null) return Results.NotFound();
            if (profile.Revision != request.ExpectedRevision) return Results.Conflict(new ErrorResponse("revision_conflict", "Guild member changed; refresh and retry."));
            profile.Name = request.Name.Trim(); profile.Role = request.Role.Trim(); profile.PortraitId = request.PortraitId; profile.CustomPortrait = request.CustomPortrait; profile.Accent = request.Accent; profile.Instructions = request.Instructions; profile.DefaultModelId = string.IsNullOrWhiteSpace(request.DefaultModelId) ? null : request.DefaultModelId.Trim(); profile.Revision++;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToProfile(profile));
        });
        app.MapDelete("/agents/{id:guid}", async (Guid id, long expectedRevision, IDbContextFactory<GuildhallDbContext> factory, ChatRunner runner, CancellationToken ct) =>
        {
            await runner.MutationGate.WaitAsync(ct);
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
                var profile = await db.AgentDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct);
                if (profile is null) return Results.NotFound();
                if (profile.Revision != expectedRevision)
                    return Results.Conflict(new ErrorResponse("revision_conflict", "Guild member changed; refresh and retry."));

                var conversations = db.Conversations.Where(x => x.AgentDefinitionId == id);
                if (await conversations.AnyAsync(x => x.Runs.Any(r => r.Status == ChatRunStatus.Running || r.Status == ChatRunStatus.Cancelling || r.Status == ChatRunStatus.Queued), ct))
                    return Results.Conflict(new ErrorResponse("member_has_active_run", "Wait for this member's running or queued replies to finish before deleting the member and saved sessions."));
                if (id == AssistantId && !await conversations.AnyAsync(ct))
                    return Results.Conflict(new ErrorResponse("member_protected", "The built-in assistant cannot be deleted until it has a saved session."));

                await conversations.ExecuteDeleteAsync(ct);
                db.AgentDefinitions.Remove(profile);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Results.NoContent();
            }
            finally { runner.MutationGate.Release(); }
        });
        app.MapPatch("/agents/{id:guid}/lifecycle", async (Guid id, GuildMemberLifecycleRequest request, IDbContextFactory<GuildhallDbContext> factory, ChatRunner runner, CancellationToken ct) =>
        {
            await runner.MutationGate.WaitAsync(ct);
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
                var profile = await db.AgentDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct);
                if (profile is null) return Results.NotFound();
                if (profile.Revision != request.ExpectedRevision) return Results.Conflict(new ErrorResponse("revision_conflict", "Guild member changed; refresh and retry."));
                if (profile.IsArchived == request.IsArchived) { await transaction.CommitAsync(ct); return Results.Ok(ToProfile(profile)); }
                var activeRun = await db.Conversations.AnyAsync(c => c.AgentDefinitionId == id && c.Runs.Any(r => r.Status == ChatRunStatus.Running || r.Status == ChatRunStatus.Cancelling || r.Status == ChatRunStatus.Queued), ct);
                if (activeRun) return Results.Conflict(new ErrorResponse("member_has_active_run", "Wait for this member's running or queued replies to finish before archiving or restoring the member."));
                profile.IsArchived = request.IsArchived;
                profile.Revision++;
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Results.Ok(ToProfile(profile));
            }
            finally { runner.MutationGate.Release(); }
        });
        app.MapPatch("/assistant", async (UpdateAssistantRequest request, IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) =>
        {
            if (request.Instructions.Length > 8192) return Results.BadRequest(new ErrorResponse("input_limit", "Instructions are too long."));
            await using var db = await factory.CreateDbContextAsync(ct); var item = await db.AgentDefinitions.SingleOrDefaultAsync(x => x.Id == AssistantId, ct);
            if (item is null) return Results.NotFound(); if (item.Revision != request.ExpectedRevision) return Results.Conflict(new ErrorResponse("revision_conflict", "Assistant changed; refresh and retry."));
            item.Instructions = request.Instructions; item.DefaultModelId = string.IsNullOrWhiteSpace(request.DefaultModelId) ? null : request.DefaultModelId.Trim(); item.Revision++;
            await db.SaveChangesAsync(ct); return Results.Ok(ToResponse(item));
        });
        app.MapGet("/conversations", async (int? limit, bool? archived, IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 25, 1, 100); await using var db = await factory.CreateDbContextAsync(ct);
            var rows = await db.Conversations.AsNoTracking().Where(x => x.IsArchived == (archived ?? false)).OrderByDescending(x => x.UpdatedUtc).Take(take)
                .Select(x => new ConversationSummary(x.Id, x.Title, x.SelectedModelId, x.CreatedUtc, x.UpdatedUtc, x.IsArchived, x.ArchivedUtc,
                    x.Runs.Any(r => r.Status == ChatRunStatus.Running || r.Status == ChatRunStatus.Cancelling || r.Status == ChatRunStatus.Queued), x.Revision, x.AgentDefinitionId,
                    x.Runs.Where(r => r.FinishedUtc != null && r.Messages.Any(m => m.Role == ChatMessageRole.Assistant && (r.Status == ChatRunStatus.Completed || m.Text != string.Empty))).Max(r => (DateTime?)r.FinishedUtc))).ToListAsync(ct);
            var untitledIds = rows.Where(x => x.Title == "New conversation").Select(x => x.Id).ToList();
            var firstMessages = await db.ChatMessages.AsNoTracking().Where(m => untitledIds.Contains(m.Run.ConversationId) && m.Role == ChatMessageRole.User)
                .OrderBy(m => m.Run.TurnNumber).Select(m => new { ConversationId = m.Run.ConversationId, m.Text }).ToListAsync(ct);
            var firstTextByConversation = firstMessages.GroupBy(x => x.ConversationId).ToDictionary(x => x.Key, x => x.First().Text);
            return Results.Ok(rows.Select(x => x.Title == "New conversation" && firstTextByConversation.TryGetValue(x.Id, out var text) ? x with { Title = MakeTitle(text, []) } : x).ToList());
        });
        app.MapPost("/conversations", async (CreateConversationRequest request, IDbContextFactory<GuildhallDbContext> factory, ChatRunner runner, CancellationToken ct) =>
        {
            var title = string.IsNullOrWhiteSpace(request.Title) ? "New conversation" : request.Title.Trim(); if (title.Length > 200) return Results.BadRequest(new ErrorResponse("input_limit", "Title is too long."));
            await runner.MutationGate.WaitAsync(ct);
            try {
            await using var db = await factory.CreateDbContextAsync(ct); var agentId = request.AgentDefinitionId ?? AssistantId; var assistant = await db.AgentDefinitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == agentId, ct); if (assistant is null) return Results.BadRequest(new ErrorResponse("agent_not_found", "Choose an existing guild member.")); if (assistant.IsArchived) return Results.Conflict(new ErrorResponse("member_archived", "Restore this guild member before starting a new session."));
            var now = DateTime.UtcNow; var item = new Conversation { Id = Guid.NewGuid(), Title = title, AgentDefinitionId = assistant.Id, SelectedModelId = request.ModelId ?? assistant.DefaultModelId, CreatedUtc = now, UpdatedUtc = now, Revision = 1 }; db.Conversations.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/conversations/{item.Id}", ToSummary(item));
            } finally { runner.MutationGate.Release(); }
        });
        app.MapGet("/conversations/{id:guid}", async (Guid id, IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct); var item = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return Results.NotFound();
            var storedMessages = await db.ChatMessages.AsNoTracking().Where(m => m.Run.ConversationId == id).OrderBy(m => m.Run.TurnNumber).Select(m => new { m.Id, Role = m.Role, m.Text, m.RunId, Status = m.Run.Status, m.Run.ErrorCode, m.Run.ModelId, TurnNumber = m.Run.TurnNumber, m.Run.StartedUtc, m.Run.FinishedUtc }).ToListAsync(ct);
            var ids = storedMessages.Select(m => m.Id).ToList();
            var attachments = await db.ChatAttachments.AsNoTracking().Where(a => ids.Contains(a.MessageId)).Select(a => new { a.Id, a.MessageId, a.FileName, a.ContentType, a.Length }).ToListAsync(ct);
            var messages = storedMessages.OrderBy(m => m.TurnNumber).ThenBy(m => m.Role == ChatMessageRole.User ? 0 : 1).Select(m => new MessageView(m.Id, m.Role.ToString().ToLowerInvariant(), m.Text, m.RunId, m.Status.ToString(), m.TurnNumber, attachments.Where(a => a.MessageId == m.Id).Select(a => new AttachmentView(a.Id, a.FileName, a.ContentType, a.Length)).ToArray(), m.ErrorCode, m.ModelId,
                m.Role == ChatMessageRole.Assistant && m.FinishedUtc.HasValue ? Math.Max(0, (m.FinishedUtc.Value - m.StartedUtc).TotalSeconds) : null)).ToList();
            var title = item.Title;
            if (title == "New conversation")
            {
                var firstMessage = storedMessages.FirstOrDefault(m => m.Role == ChatMessageRole.User);
                if (firstMessage is not null) title = MakeTitle(firstMessage.Text, attachments.Where(a => a.MessageId == firstMessage.Id).Select(a => (a.FileName, a.ContentType, Array.Empty<byte>())).ToList());
            }
            var lastResponseUtc = storedMessages.Where(m => m.Role == ChatMessageRole.Assistant && m.FinishedUtc.HasValue && (m.Status == ChatRunStatus.Completed || !string.IsNullOrEmpty(m.Text))).Select(m => (DateTime?)m.FinishedUtc).Max();
            var summary = new ConversationSummary(item.Id, title, item.SelectedModelId, item.CreatedUtc, item.UpdatedUtc, item.IsArchived, item.ArchivedUtc,
                messages.Any(message => message.Status is "Running" or "Cancelling" or "Queued"), item.Revision, item.AgentDefinitionId, lastResponseUtc);
            var agent = await db.AgentDefinitions.AsNoTracking().SingleAsync(x => x.Id == item.AgentDefinitionId, ct);
            return Results.Ok(new ConversationResponse(summary, messages, ToProfile(agent)));
        });
        app.MapPatch("/conversations/{id:guid}", async (Guid id, UpdateConversationRequest request, IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct); var item = await db.Conversations.SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return Results.NotFound(); if (item.Revision != request.ExpectedRevision) return Results.Conflict(new ErrorResponse("revision_conflict", "Conversation changed; refresh and retry."));
            if (request.Title is not null) { if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200) return Results.BadRequest(new ErrorResponse("input_limit", "Chat names must be 1–200 characters.")); item.Title = request.Title.Trim(); } if (request.ModelId is not null) item.SelectedModelId = string.IsNullOrWhiteSpace(request.ModelId) ? null : request.ModelId.Trim(); if (request.IsArchived is not null) { var active = await db.ChatRuns.AnyAsync(x => x.ConversationId == id && (x.Status == ChatRunStatus.Running || x.Status == ChatRunStatus.Cancelling || x.Status == ChatRunStatus.Queued), ct); if (active) return Results.Conflict(new ErrorResponse("run_active", "Wait for the current response to finish before archiving or restoring this conversation.")); item.IsArchived = request.IsArchived.Value; item.ArchivedUtc = item.IsArchived ? DateTime.UtcNow : null; } item.Revision++; item.UpdatedUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); return Results.Ok(ToSummary(item));
        });
        app.MapDelete("/conversations/{id:guid}", async (Guid id, IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var item = await db.Conversations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var active = await db.ChatRuns.AnyAsync(x => x.ConversationId == id && (x.Status == ChatRunStatus.Running || x.Status == ChatRunStatus.Cancelling || x.Status == ChatRunStatus.Queued), ct);
            if (active) return Results.Conflict(new ErrorResponse("run_active", "Wait for the current response to finish before deleting this conversation."));
            db.Conversations.Remove(item);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        });
        app.MapGet("/runs/{id:guid}", async (Guid id, IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) => { await using var db = await factory.CreateDbContextAsync(ct); var run = await db.ChatRuns.AsNoTracking().Include(x => x.Messages).SingleOrDefaultAsync(x => x.Id == id, ct); return run is null ? Results.NotFound() : Results.Ok(new { runId = run.Id, status = run.Status.ToString(), sequence = run.Sequence, text = run.Messages.Single(x => x.Role == ChatMessageRole.Assistant).Text, errorCode = run.ErrorCode }); });
        app.MapGet("/attachments/{id:guid}", async (Guid id, IDbContextFactory<GuildhallDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var attachment = await db.ChatAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (attachment is null) return Results.NotFound();
            var contentType = Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(attachment.ContentType, out var parsed) ? parsed.ToString() : "application/octet-stream";
            return Results.File(attachment.Content, contentType, attachment.FileName, enableRangeProcessing: false);
        });
        app.MapPost("/conversations/{id:guid}/messages", async (Guid id, HttpRequest http, IDbContextFactory<GuildhallDbContext> factory, ChatRunner runner, CancellationToken ct) =>
        {
            string clientRequestId; long expectedRevision; string text; string deliveryMode = "queue"; var uploaded = new List<(string Name, string Type, byte[] Content)>();
            if (http.HasFormContentType)
            {
                if (http.ContentLength > MaxMessageAttachmentsBytes + 1024 * 1024) return Results.BadRequest(new ErrorResponse("attachment_limit", "The combined attachment limit is 50 MB."));
                var form = await http.ReadFormAsync(ct);
                clientRequestId = form["clientRequestId"].ToString(); text = form["text"].ToString(); deliveryMode = string.IsNullOrEmpty(form["deliveryMode"]) ? "queue" : form["deliveryMode"].ToString();
                if (!long.TryParse(form["expectedConversationRevision"], out expectedRevision)) return Results.BadRequest(new ErrorResponse("invalid_request", "Conversation revision is required."));
                if (form.Files.Count > MaxAttachments) return Results.BadRequest(new ErrorResponse("attachment_count", "Attach up to five files per message."));
                long total = 0;
                foreach (var file in form.Files)
                {
                    if (file.Length > MaxAttachmentBytes) return Results.BadRequest(new ErrorResponse("attachment_size", "Each attachment must be 20 MB or smaller."));
                    total += file.Length;
                    if (total > MaxMessageAttachmentsBytes) return Results.BadRequest(new ErrorResponse("attachment_limit", "The combined attachment limit is 50 MB."));
                    var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
                    if (string.IsNullOrWhiteSpace(name) || name.Length > 255) return Results.BadRequest(new ErrorResponse("attachment_name", "File names must be 1–255 characters."));
                    await using var buffer = new MemoryStream((int)file.Length);
                    await file.CopyToAsync(buffer, ct);
                    var content = buffer.ToArray();
                    uploaded.Add((name, DetectContentType(file.ContentType, content), content));
                }
            }
            else
            {
                var request = await http.ReadFromJsonAsync<CreateMessageRequest>(cancellationToken: ct);
                if (request is null) return Results.BadRequest(new ErrorResponse("invalid_request", "Message is required."));
                clientRequestId = request.ClientRequestId; expectedRevision = request.ExpectedConversationRevision; text = request.Text; deliveryMode = request.DeliveryMode;
            }
            if (string.IsNullOrWhiteSpace(clientRequestId) || clientRequestId.Length > 100 || Encoding.UTF8.GetByteCount(text ?? string.Empty) > MaxTextBytes || (string.IsNullOrWhiteSpace(text) && uploaded.Count == 0)) return Results.BadRequest(new ErrorResponse("input_limit", "Enter a message or attach a file; text is limited to 16 KB."));
            if (deliveryMode is not ("queue" or "send_now")) return Results.BadRequest(new ErrorResponse("invalid_delivery_mode", "Choose queue or send now."));
            using var fingerprintHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            fingerprintHash.AppendData(Encoding.UTF8.GetBytes(deliveryMode + "\0" + (text ?? string.Empty)));
            foreach (var item in uploaded) { fingerprintHash.AppendData(Encoding.UTF8.GetBytes($"\0{item.Name}\0{item.Type}\0")); fingerprintHash.AppendData(SHA256.HashData(item.Content)); }
            var fingerprint = Convert.ToHexString(fingerprintHash.GetHashAndReset()).ToLowerInvariant();
            await runner.MutationGate.WaitAsync(ct);
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
                var conversation = await db.Conversations.Include(x => x.AgentDefinition).SingleOrDefaultAsync(x => x.Id == id, ct);
                if (conversation is null) return Results.NotFound();
                var existing = await db.ChatRuns.Include(x => x.Messages).SingleOrDefaultAsync(x => x.ConversationId == id && x.ClientRequestId == clientRequestId, ct);
                if (existing is not null) return existing.RequestFingerprint == fingerprint
                    ? Results.Accepted($"/runs/{existing.Id}", new RunAcceptedResponse(existing.Id, existing.Messages.FirstOrDefault(x => x.Role == ChatMessageRole.Assistant)?.Id ?? Guid.Empty, conversation.Revision))
                    : Results.Conflict(new ErrorResponse("request_conflict", "Request ID was already used for another message."));
                if (conversation.Revision != expectedRevision) return Results.Conflict(new ErrorResponse("revision_conflict", "Conversation changed; refresh and retry."));
                if (string.IsNullOrWhiteSpace(conversation.SelectedModelId)) return Results.BadRequest(new ErrorResponse("model_missing", "Select a local model first."));
                if (await db.ChatRuns.CountAsync(x => x.ConversationId == id && x.Status == ChatRunStatus.Queued, ct) >= 8)
                    return Results.Conflict(new ErrorResponse("queue_full", "This session has eight queued messages. Remove a queued message or wait for a reply."));
                var interrupted = deliveryMode == "send_now" ? await db.ChatRuns.Where(x => x.ConversationId == id && (x.Status == ChatRunStatus.Running || x.Status == ChatRunStatus.Cancelling)).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct) : null;
                var behavior = await db.GuildhallBehaviorSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
                if (behavior is null) return Results.Conflict(new ErrorResponse("settings_missing", "Guildhall behavior settings require the approved database migration."));
                var now = DateTime.UtcNow;
                var turn = await db.ChatRuns.Where(x => x.ConversationId == id).Select(x => (int?)x.TurnNumber).MaxAsync(ct) ?? 0;
                var run = new ChatRun { Id = Guid.NewGuid(), ConversationId = id, TurnNumber = turn + 1,
                    ClientRequestId = clientRequestId, RequestFingerprint = fingerprint, Status = ChatRunStatus.Queued,
                    StartedUtc = now, UpdatedUtc = now, InstructionsSnapshot = conversation.AgentDefinition.Instructions,
                    ModelId = conversation.SelectedModelId, ProviderId = "ollama",
                    SettingsSnapshotJson = System.Text.Json.JsonSerializer.Serialize(new { deliveryMode, interruptedRunId = interrupted, guildhallBehaviorRevision = behavior.Revision, guildhallBehavior = behavior.Instructions }) };
                var userMessage = new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.User, Text = text ?? string.Empty };
                foreach (var item in uploaded) userMessage.Attachments.Add(new ChatAttachment { Id = Guid.NewGuid(), FileName = item.Name, ContentType = item.Type, Length = item.Content.LongLength, Content = item.Content });
                run.Messages.Add(userMessage);
                run.Messages.Add(new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.Assistant, Text = string.Empty });
                if (conversation.Title == "New conversation")
                {
                    var firstText = await db.ChatMessages.AsNoTracking().Where(m => m.Run.ConversationId == id && m.Role == ChatMessageRole.User).OrderBy(m => m.Run.TurnNumber).Select(m => m.Text).FirstOrDefaultAsync(ct);
                    conversation.Title = MakeTitle(firstText ?? text ?? string.Empty, firstText is null ? uploaded : []);
                }
                conversation.Revision++; conversation.UpdatedUtc = now; db.ChatRuns.Add(run);
                await db.SaveChangesAsync(ct);
                if (deliveryMode == "send_now")
                {
                    var nextTurn = await db.ChatRuns.Where(x => x.ConversationId == id && x.Status == ChatRunStatus.Queued).MinAsync(x => x.TurnNumber, ct);
                    // Removed queued entries also occupy a reserved turn number.
                    var pending = await db.ChatRuns.AsNoTracking().Where(x => x.ConversationId == id && x.TurnNumber >= nextTurn).OrderBy(x => x.TurnNumber).Select(x => new { x.Id, x.TurnNumber }).ToListAsync(ct);
                    // Vacate reserved numbers first to preserve the unique turn index.
                    await db.ChatRuns.Where(x => x.ConversationId == id && x.TurnNumber >= nextTurn).ExecuteUpdateAsync(set => set.SetProperty(x => x.TurnNumber, x => -x.TurnNumber), ct);
                    foreach (var queued in pending.OrderBy(x => x.Id == run.Id ? 0 : 1).ThenBy(x => x.TurnNumber))
                    {
                        var assigned = nextTurn++;
                        await db.ChatRuns.Where(x => x.Id == queued.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.TurnNumber, assigned), ct);
                    }
                }
                await transaction.CommitAsync(ct);
                if (interrupted.HasValue) await runner.CancelAsync(interrupted.Value, ct);
                runner.Start(run.Id);
                return Results.Accepted($"/runs/{run.Id}", new RunAcceptedResponse(run.Id, run.Messages.Single(x => x.Role == ChatMessageRole.Assistant).Id, conversation.Revision));
            }
            finally { runner.MutationGate.Release(); }
        });
    }
    private static AssistantResponse ToResponse(AgentDefinition x) => new(x.Id, x.Name, x.Instructions, x.DefaultModelId, x.Revision);
    private static AgentProfileResponse ToProfile(AgentDefinition x) => new(x.Id, x.Name, x.Role, x.PortraitId, x.CustomPortrait, x.Accent, x.Instructions, x.DefaultModelId, x.Revision, x.IsArchived);
    private static string? ValidateProfile(string name, string role, int portraitId, string? customPortrait, string accent, string instructions, string? defaultModelId)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40) return "Guild member names must be 1–40 characters.";
        if (string.IsNullOrWhiteSpace(role) || role.Trim().Length > 40) return "Guild member roles must be 1–40 characters.";
        if (portraitId is < 0 or > 3) return "Choose one of the available portraits.";
        if (customPortrait is { Length: > 350_000 } || (customPortrait is not null && !(customPortrait.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) || customPortrait.StartsWith("data:image/png;base64,", StringComparison.Ordinal) || customPortrait.StartsWith("data:image/webp;base64,", StringComparison.Ordinal)))) return "Custom portraits must be JPEG, PNG or WebP images no larger than 256 KiB after preparation.";
        try { if (customPortrait is not null) { var comma = customPortrait.IndexOf(','); var bytes = Convert.FromBase64String(customPortrait[(comma + 1)..]); if (!IsSupportedPortrait(customPortrait, bytes)) return "The custom portrait data is invalid."; } } catch (FormatException) { return "The custom portrait data is invalid."; }
        if (accent is not ("#71465f" or "#536078" or "#3d6654" or "#7f6042" or "#54717a")) return "Choose an available accent color.";
        if (instructions.Length > 8192) return "Instructions must be 8 KiB of text or less.";
        if (defaultModelId?.Trim().Length > 200) return "Model IDs must be 200 characters or less.";
        return null;
    }
    private static bool IsSupportedPortrait(string dataUri, byte[] bytes) =>
        dataUri.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) && bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[^2] == 0xFF && bytes[^1] == 0xD9 ||
        dataUri.StartsWith("data:image/png;base64,", StringComparison.Ordinal) && bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ||
        dataUri.StartsWith("data:image/webp;base64,", StringComparison.Ordinal) && bytes.Length >= 12 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP";
    private static ConversationSummary ToSummary(Conversation x) => new(x.Id, x.Title, x.SelectedModelId, x.CreatedUtc, x.UpdatedUtc, x.IsArchived, x.ArchivedUtc, false, x.Revision, x.AgentDefinitionId);
    private static string DetectContentType(string supplied, byte[] content)
    {
        if (content.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (content.Length >= 4 && content[0] == 0xFF && content[1] == 0xD8 && content[^2] == 0xFF && content[^1] == 0xD9) return "image/jpeg";
        if (content.Length >= 12 && System.Text.Encoding.ASCII.GetString(content, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(content, 8, 4) == "WEBP") return "image/webp";
        if (content.Length >= 6 && System.Text.Encoding.ASCII.GetString(content, 0, 3) == "GIF") return "image/gif";
        return Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(supplied, out var parsed) ? parsed.MediaType.ToString() : "application/octet-stream";
    }
    private static string MakeTitle(string text, IReadOnlyList<(string Name, string Type, byte[] Content)> uploaded)
    {
        var firstLine = text.TrimStart();
        if (string.IsNullOrWhiteSpace(firstLine) && uploaded.Count > 0) return uploaded[0].Name;
        if (string.IsNullOrWhiteSpace(firstLine)) return "New conversation";
        firstLine = firstLine.Split('\n', 2)[0].Trim();
        if (firstLine.Length <= 64) return firstLine;
        var shortTitle = firstLine[..64];
        var wordBoundary = shortTitle.LastIndexOf(' ');
        return (wordBoundary > 32 ? shortTitle[..wordBoundary] : shortTitle).TrimEnd() + "…";
    }
}

