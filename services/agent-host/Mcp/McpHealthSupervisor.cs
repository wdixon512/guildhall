using System.Collections.Concurrent;

namespace Guildhall.AgentHost.Mcp;

public sealed class McpHealthSupervisor(TimeProvider timeProvider)
{
    private static readonly TimeSpan[] BackoffSchedule =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
    ];

    private readonly ConcurrentDictionary<string, McpServerHealthState> _states = new(StringComparer.OrdinalIgnoreCase);

    public McpServerHealthState GetOrCreate(string serverId, string displayName, string transport, string initialStatus = McpHealthStatuses.Connected) =>
        _states.GetOrAdd(serverId, _ => new McpServerHealthState(
            serverId,
            displayName,
            transport,
            initialStatus,
            0,
            null,
            null,
            null));

    public McpServerHealthState? Get(string serverId) =>
        _states.TryGetValue(serverId, out var state) ? state : null;

    public IReadOnlyList<McpServerHealthState> GetAll() =>
        _states.Values.OrderBy(x => x.ServerId, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool CanAttempt(string serverId)
    {
        if (!_states.TryGetValue(serverId, out var state)) return true;
        if (state.Status == McpHealthStatuses.Disconnected) return false;
        if (state.Status == McpHealthStatuses.Failed) return false;
        if (state.Status == McpHealthStatuses.Degraded && state.NextRetryUtc is { } retryAt)
            return timeProvider.GetUtcNow() >= retryAt;
        return true;
    }

    public McpServerHealthState RecordSuccess(string serverId, string displayName, string transport)
    {
        var now = timeProvider.GetUtcNow();
        var next = new McpServerHealthState(
            serverId,
            displayName,
            transport,
            McpHealthStatuses.Connected,
            0,
            now,
            null,
            null);
        _states[serverId] = next;
        return next;
    }

    public McpServerHealthState RecordFailure(string serverId, string displayName, string transport, string errorMessage)
    {
        var now = timeProvider.GetUtcNow();
        return _states.AddOrUpdate(
            serverId,
            _ =>
            {
                var delay = BackoffSchedule[0];
                return new McpServerHealthState(
                    serverId,
                    displayName,
                    transport,
                    McpHealthStatuses.Degraded,
                    1,
                    now,
                    now + delay,
                    errorMessage);
            },
            (_, current) =>
            {
                var failures = current.ConsecutiveFailures + 1;
                if (failures >= 3)
                {
                    return new McpServerHealthState(
                        serverId,
                        displayName,
                        transport,
                        McpHealthStatuses.Failed,
                        failures,
                        now,
                        null,
                        errorMessage);
                }

                var delay = BackoffSchedule[Math.Min(failures - 1, BackoffSchedule.Length - 1)];
                return new McpServerHealthState(
                    serverId,
                    displayName,
                    transport,
                    McpHealthStatuses.Degraded,
                    failures,
                    now,
                    now + delay,
                    errorMessage);
            });
    }

    public McpServerHealthState ResetForProbe(string serverId, string displayName, string transport)
    {
        var next = new McpServerHealthState(
            serverId,
            displayName,
            transport,
            McpHealthStatuses.Degraded,
            0,
            null,
            null,
            null);
        _states[serverId] = next;
        return next;
    }

    public McpServerHealthState MarkDisconnected(string serverId, string displayName, string transport, string? reason = null)
    {
        var now = timeProvider.GetUtcNow();
        var next = new McpServerHealthState(
            serverId,
            displayName,
            transport,
            McpHealthStatuses.Disconnected,
            0,
            now,
            null,
            reason);
        _states[serverId] = next;
        return next;
    }
}
