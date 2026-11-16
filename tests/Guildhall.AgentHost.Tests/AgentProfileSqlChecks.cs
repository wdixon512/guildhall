using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Guildhall.AgentHost.Tests;

internal static class AgentProfileSqlChecks
{
    public static async Task VerifyAsync(DbContextOptions<GuildhallDbContext> options)
    {
        await using var app = new ApiFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IDbContextFactory<GuildhallDbContext>>();
            services.AddSingleton<IDbContextFactory<GuildhallDbContext>>(new Factory(options));
        }));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:43120") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token);
        async Task<AgentProfileResponse> Create(string name, int portrait) {
            var response = await client.PostAsJsonAsync("/agents", new CreateAgentProfileRequest(name, "Steward", portrait, null, "#71465f", $"Rules for {name}", null));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AgentProfileResponse>())!;
        }
        var maren = await Create("Maren", 0);
        var scholar = await Create("Scholar", 1);
        var changedResponse = await client.PatchAsJsonAsync($"/agents/{maren.Id}", new UpdateAgentProfileRequest("Maren the Steward", "Steward", 2, null, "#536078", "Maren-specific rules", null, maren.Revision));
        changedResponse.EnsureSuccessStatusCode();
        var changed = (await changedResponse.Content.ReadFromJsonAsync<AgentProfileResponse>())!;
        var profiles = await client.GetFromJsonAsync<AgentProfileResponse[]>("/agents");
        Assert.NotNull(profiles);
        var untouched = Assert.Single(profiles, x => x.Id == scholar.Id);
        Assert.Equal("Scholar", untouched.Name);
        Assert.Equal(1, untouched.PortraitId);
        Assert.Equal("Rules for Scholar", untouched.Instructions);
        Assert.Equal("Maren the Steward", Assert.Single(profiles, x => x.Id == changed.Id).Name);

        var behavior = await client.GetFromJsonAsync<GuildhallBehaviorResponse>("/guildhall-behavior");
        Assert.NotNull(behavior);
        var editedBehavior = "Shared revised council behavior.";
        var updatedBehavior = await client.PutAsJsonAsync("/guildhall-behavior", new UpdateGuildhallBehaviorRequest(editedBehavior, behavior.Revision));
        updatedBehavior.EnsureSuccessStatusCode();
        behavior = await updatedBehavior.Content.ReadFromJsonAsync<GuildhallBehaviorResponse>();
        Assert.Equal(editedBehavior, behavior!.Instructions);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/guildhall-behavior", new UpdateGuildhallBehaviorRequest("stale", behavior.Revision - 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/guildhall-behavior", new UpdateGuildhallBehaviorRequest("  ", behavior.Revision))).StatusCode);

        async Task<ConversationResponse> NewSession(Guid agentId) {
            var response = await client.PostAsJsonAsync("/conversations", new CreateConversationRequest("profile-session", null, agentId));
            response.EnsureSuccessStatusCode();
            var summary = (await response.Content.ReadFromJsonAsync<ConversationSummary>())!;
            return (await client.GetFromJsonAsync<ConversationResponse>($"/conversations/{summary.Id}"))!;
        }
        var firstSession = await NewSession(changed.Id);
        var secondSession = await NewSession(changed.Id);
        var otherSession = await NewSession(scholar.Id);
        Assert.Equal(firstSession.Agent.Id, secondSession.Agent.Id);
        Assert.Equal(changed.Id, firstSession.Agent.Id);
        Assert.Equal(scholar.Id, otherSession.Agent.Id);
        Assert.Equal("Rules for Scholar", otherSession.Agent.Instructions);
        // Preserve real persisted history and provenance through archive and restore.
        var runId = Guid.NewGuid();
        var secondRunId = Guid.NewGuid();
        await using (var seed = new GuildhallDbContext(options)) {
            var now = DateTime.UtcNow;
            var run = new ChatRun { Id = runId, ConversationId = firstSession.Conversation.Id, TurnNumber = 1, ClientRequestId = "member-archive", RequestFingerprint = "fixture", Status = ChatRunStatus.Completed, StartedUtc = now, UpdatedUtc = now, FinishedUtc = now, ModelId = "fixture-model", InstructionsSnapshot = "original rules" };
            run.Messages.Add(new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.User, Text = "Keep this question" });
            run.Messages.Add(new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.Assistant, Text = "Keep this answer" });
            var secondRun = new ChatRun { Id = secondRunId, ConversationId = secondSession.Conversation.Id, TurnNumber = 1, ClientRequestId = "member-delete", RequestFingerprint = "fixture-delete", Status = ChatRunStatus.Completed, StartedUtc = now, UpdatedUtc = now, FinishedUtc = now, ModelId = "fixture-model", InstructionsSnapshot = "second session rules" };
            var uploaded = new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.User, Text = "Keep this attachment until deletion" };
            uploaded.Attachments.Add(new ChatAttachment { Id = Guid.NewGuid(), FileName = "fixture.txt", ContentType = "text/plain", Length = 4, Content = [1, 2, 3, 4] });
            secondRun.Messages.Add(uploaded);
            secondRun.Messages.Add(new ChatMessage { Id = Guid.NewGuid(), Role = ChatMessageRole.Assistant, Text = "Second answer" });
            seed.ChatRuns.AddRange(run, secondRun); await seed.SaveChangesAsync();
        }
        async Task<HttpResponseMessage> Lifecycle(bool archived, long revision) => await client.PatchAsJsonAsync($"/agents/{changed.Id}/lifecycle", new GuildMemberLifecycleRequest(archived, revision));
        foreach (var status in new[] { ChatRunStatus.Running, ChatRunStatus.Cancelling, ChatRunStatus.Queued }) {
            await using (var db = new GuildhallDbContext(options))
                await db.ChatRuns.Where(x => x.Id == runId).ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, status));
            var blocked = await Lifecycle(true, changed.Revision);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal("member_has_active_run", (await blocked.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
            var deleteBlocked = await client.DeleteAsync($"/agents/{changed.Id}?expectedRevision={changed.Revision}");
            Assert.Equal(HttpStatusCode.Conflict, deleteBlocked.StatusCode);
            Assert.Equal("member_has_active_run", (await deleteBlocked.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
            await using var verify = new GuildhallDbContext(options);
            Assert.False((await verify.AgentDefinitions.SingleAsync(x => x.Id == changed.Id)).IsArchived);
        }
        await using (var db = new GuildhallDbContext(options))
            await db.ChatRuns.Where(x => x.Id == runId).ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, ChatRunStatus.Completed));
        Assert.Equal(HttpStatusCode.Conflict, (await Lifecycle(true, changed.Revision - 1)).StatusCode);
        var archivedResponse = await Lifecycle(true, changed.Revision);
        archivedResponse.EnsureSuccessStatusCode();
        var archived = (await archivedResponse.Content.ReadFromJsonAsync<AgentProfileResponse>())!;
        Assert.True(archived.IsArchived);
        Assert.Equal(changed.Revision + 1, archived.Revision);
        Assert.Equal(changed with { IsArchived = true, Revision = archived.Revision }, archived);
        var repeated = await Lifecycle(true, archived.Revision);
        repeated.EnsureSuccessStatusCode();
        Assert.Equal(archived, await repeated.Content.ReadFromJsonAsync<AgentProfileResponse>());
        var rejected = await client.PostAsJsonAsync("/conversations", new CreateConversationRequest("must not create", null, changed.Id));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("member_archived", (await rejected.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
        var retained = (await client.GetFromJsonAsync<ConversationResponse>($"/conversations/{firstSession.Conversation.Id}"))!;
        Assert.True(retained.Agent.IsArchived);
        Assert.Equal(firstSession.Conversation, retained.Conversation with { LastResponseUtc = null });
        Assert.NotNull(retained.Conversation.LastResponseUtc);
        Assert.Equal(new[] { "Keep this question", "Keep this answer" }, retained.Messages.Select(x => x.Text));
        var listed = (await client.GetFromJsonAsync<ConversationSummary[]>("/conversations?limit=100"))!;
        Assert.Contains(listed, x => x.Id == firstSession.Conversation.Id);
        Assert.Contains(listed, x => x.Id == secondSession.Conversation.Id);
        await using (var db = new GuildhallDbContext(options)) {
            Assert.Equal("original rules", (await db.ChatRuns.SingleAsync(x => x.Id == runId)).InstructionsSnapshot);
            Assert.False((await db.AgentDefinitions.SingleAsync(x => x.Id == scholar.Id)).IsArchived);
            Assert.False(await db.Conversations.AnyAsync(x => x.Title == "must not create"));
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Lifecycle(false, changed.Revision)).StatusCode);
        var restoredResponse = await Lifecycle(false, archived.Revision);
        restoredResponse.EnsureSuccessStatusCode();
        var restored = (await restoredResponse.Content.ReadFromJsonAsync<AgentProfileResponse>())!;
        Assert.False(restored.IsArchived);
        Assert.Equal(changed with { Revision = archived.Revision + 1 }, restored);
        await NewSession(changed.Id);
        var staleDelete = await client.DeleteAsync($"/agents/{changed.Id}?expectedRevision={restored.Revision - 1}");
        Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);
        await using (var beforeDelete = new GuildhallDbContext(options))
        {
            Assert.True(await beforeDelete.AgentDefinitions.AnyAsync(x => x.Id == changed.Id));
            Assert.Equal(3, await beforeDelete.Conversations.CountAsync(x => x.AgentDefinitionId == changed.Id));
        }
        var deleteMember = await client.DeleteAsync($"/agents/{changed.Id}?expectedRevision={restored.Revision}");
        Assert.Equal(HttpStatusCode.NoContent, deleteMember.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/agents/{changed.Id}?expectedRevision={restored.Revision}")).StatusCode);
        await using (var afterDelete = new GuildhallDbContext(options))
        {
            Assert.False(await afterDelete.AgentDefinitions.AnyAsync(x => x.Id == changed.Id));
            Assert.False(await afterDelete.Conversations.AnyAsync(x => x.AgentDefinitionId == changed.Id));
            Assert.False(await afterDelete.ChatRuns.AnyAsync(x => x.Id == runId || x.Id == secondRunId));
            Assert.False(await afterDelete.ChatAttachments.AnyAsync(x => x.FileName == "fixture.txt"));
            Assert.True(await afterDelete.AgentDefinitions.AnyAsync(x => x.Id == scholar.Id));
            Assert.True(await afterDelete.Conversations.AnyAsync(x => x.Id == otherSession.Conversation.Id));
        }

    }

    public static async Task VerifyProtectedSeedDeleteAsync(DbContextOptions<GuildhallDbContext> options)
    {
        await using var app = new ApiFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IDbContextFactory<GuildhallDbContext>>();
            services.AddSingleton<IDbContextFactory<GuildhallDbContext>>(new Factory(options));
        }));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:43120") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token);
        var response = await client.DeleteAsync("/agents/7e3b38f2-2a51-4d15-93c2-0f8f7b8cf1c2?expectedRevision=1");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("member_protected", (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
    }

    private sealed class Factory(DbContextOptions<GuildhallDbContext> options) : IDbContextFactory<GuildhallDbContext>
    { public GuildhallDbContext CreateDbContext() => new(options); }
}
