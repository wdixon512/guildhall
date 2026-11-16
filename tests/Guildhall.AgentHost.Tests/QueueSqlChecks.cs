using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Guildhall.AgentHost.Tests;

internal static class QueueSqlChecks
{
    public static async Task VerifyAsync(DbContextOptions<GuildhallDbContext> options, int keepAliveSeconds = 0)
    {
        var provider = new Provider(keepAliveSeconds);
        await using var app = new ApiFactory().WithWebHostBuilder(builder => builder.UseSetting("GUILDHALL_MODEL_KEEP_ALIVE_SECONDS", keepAliveSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureServices(services => {
            services.RemoveAll<IDbContextFactory<GuildhallDbContext>>();
            services.AddSingleton<IDbContextFactory<GuildhallDbContext>>(new Factory(options));
            services.RemoveAll<IHttpClientFactory>(); services.AddSingleton<IHttpClientFactory>(provider);
        }));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:43120") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token);
        var created = await client.PostAsJsonAsync("/conversations", new CreateConversationRequest("isolated-queue", "model-a"));
        created.EnsureSuccessStatusCode();
        var conversation = (await created.Content.ReadFromJsonAsync<ConversationSummary>())!;
        async Task<RunAcceptedResponse> Send(string text, string mode = "queue")
        {
            var current = (await client.GetFromJsonAsync<ConversationResponse>($"/conversations/{conversation.Id}"))!;
            var response = await client.PostAsJsonAsync($"/conversations/{conversation.Id}/messages", new CreateMessageRequest(Guid.NewGuid().ToString(), current.Conversation.Revision, text, mode));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<RunAcceptedResponse>())!;
        }
        var first = await Send("hold-first");
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await Send("queued-second");
        var current = (await client.GetFromJsonAsync<ConversationResponse>($"/conversations/{conversation.Id}"))!;
        var changed = await client.PatchAsJsonAsync($"/conversations/{conversation.Id}", new UpdateConversationRequest(null, "model-b", null, current.Conversation.Revision));
        changed.EnsureSuccessStatusCode();
        var third = await Send("queued-third");
        var removed = await Send("remove-me");
        (await client.PostAsync($"/runs/{removed.RunId}/cancel", null)).EnsureSuccessStatusCode();
        var urgent = await Send("urgent-follow-up", "send_now");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            await using var db = new GuildhallDbContext(options);
            var pending = await db.ChatRuns.AnyAsync(x => x.ConversationId == conversation.Id && (x.Status == ChatRunStatus.Running || x.Status == ChatRunStatus.Cancelling || x.Status == ChatRunStatus.Queued));
            if (!pending) break;
            Assert.True(DateTime.UtcNow < deadline, "Queue did not drain.");
            await Task.Delay(30);
        }
        var calls = provider.Calls.ToArray();
        Assert.Equal(new[] { "hold-first", "urgent-follow-up", "queued-second", "queued-third" }, calls.Select(x => x.Text));
        Assert.Equal(new[] { "model-a", "model-b", "model-a", "model-b" }, calls.Select(x => x.Model));
        Assert.Equal(1, provider.Peak);
        Assert.Contains("hold-first", calls[1].Context);
        Assert.DoesNotContain("queued-second", calls[1].Context);
        await using var verify = new GuildhallDbContext(options);
        var captured = await verify.ChatRuns.SingleAsync(x => x.Id == urgent.RunId);
        using (var snapshot = JsonDocument.Parse(captured.SettingsSnapshotJson!))
        {
            Assert.Equal(Chat.GuildhallBehavior.Version, snapshot.RootElement.GetProperty("guildhallBehaviorVersion").GetString());
            Assert.Equal(Chat.GuildhallBehavior.Instructions, snapshot.RootElement.GetProperty("guildhallBehavior").GetString());
        }
        Assert.Equal("A completed reply.", (await verify.ChatMessages.SingleAsync(x => x.RunId == urgent.RunId && x.Role == ChatMessageRole.Assistant)).Text);
        Assert.Equal(ChatRunStatus.Cancelled, (await verify.ChatRuns.SingleAsync(x => x.Id == first.RunId)).Status);
        Assert.Equal(ChatRunStatus.Cancelled, (await verify.ChatRuns.SingleAsync(x => x.Id == removed.RunId)).Status);
        Assert.Equal(ChatRunStatus.Completed, (await verify.ChatRuns.SingleAsync(x => x.Id == urgent.RunId)).Status);
        Assert.True((await verify.ChatRuns.SingleAsync(x => x.Id == urgent.RunId)).TurnNumber < (await verify.ChatRuns.SingleAsync(x => x.Id == second.RunId)).TurnNumber);
        var staleId = Guid.NewGuid();
        verify.ChatRuns.Add(new ChatRun { Id = staleId, ConversationId = conversation.Id, TurnNumber = 6, ClientRequestId = "stale-queued", RequestFingerprint = "stale", ModelId = "model-a", InstructionsSnapshot = "test", Status = ChatRunStatus.Queued, StartedUtc = DateTime.UtcNow.AddMinutes(-1), UpdatedUtc = DateTime.UtcNow.AddMinutes(-1) });
        await verify.SaveChangesAsync();
        await Chat.ChatRecovery.ReconcileAsync(new Factory(options), DateTime.UtcNow, default);
        await using var restarted = new GuildhallDbContext(options);
        Assert.Equal(ChatRunStatus.Interrupted, (await restarted.ChatRuns.SingleAsync(x => x.Id == staleId)).Status);
    }

    private sealed class Factory(DbContextOptions<GuildhallDbContext> options) : IDbContextFactory<GuildhallDbContext>
    { public GuildhallDbContext CreateDbContext() => new(options); }

    private sealed class Provider(int keepAliveSeconds) : HttpMessageHandler, IHttpClientFactory
    {
        public ConcurrentQueue<(string Text, string Model, string Context)> Calls { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int concurrent;
        public int Peak;
        public HttpClient CreateClient(string name) => new(this, false) { BaseAddress = new Uri("http://127.0.0.1:11434") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.False(json.RootElement.GetProperty("think").GetBoolean());
            Assert.Equal(Math.Clamp(keepAliveSeconds, 0, 900), json.RootElement.GetProperty("keep_alive").GetInt32());
            var messages = json.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            Assert.Equal("system", messages[0].GetProperty("role").GetString());
            Assert.StartsWith(Chat.GuildhallBehavior.Instructions, messages[0].GetProperty("content").GetString());
            var text = messages.Last().GetProperty("content").GetString()!;
            Calls.Enqueue((text, json.RootElement.GetProperty("model").GetString()!, string.Join("\n", messages.Select(x => x.GetProperty("content").GetString()))));
            var count = Interlocked.Increment(ref concurrent); Peak = Math.Max(Peak, count);
            try
            {
                if (text == "hold-first") { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"message\":{\"thinking\":\"Private fixture reasoning\"},\"done\":false}\n{\"message\":{\"content\":\"A completed reply.\"},\"done\":true}\n", Encoding.UTF8, "application/x-ndjson") };
            }
            finally { Interlocked.Decrement(ref concurrent); }
        }
    }
}
