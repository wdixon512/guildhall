using Guildhall.AgentHost.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Chat;

public static class ChatRecovery
{
    public static async Task ReconcileAsync(IDbContextFactory<GuildhallDbContext> factory, DateTime hostStartedUtc, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        await db.ChatRuns.Where(x => x.StartedUtc < hostStartedUtc && (x.Status == ChatRunStatus.Running || x.Status == ChatRunStatus.Cancelling || x.Status == ChatRunStatus.Queued))
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, ChatRunStatus.Interrupted)
                .SetProperty(x => x.ErrorCode, "host_restarted").SetProperty(x => x.Sequence, x => x.Sequence + 1)
                .SetProperty(x => x.UpdatedUtc, now).SetProperty(x => x.FinishedUtc, now), ct);
    }
}
