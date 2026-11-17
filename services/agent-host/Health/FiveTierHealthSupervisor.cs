using Guildhall.AgentHost.Api;
using Guildhall.AgentHost.Mcp;
using Guildhall.AgentHost.Persistence;
using Guildhall.AgentHost.Providers.Ollama;
using Guildhall.AgentHost.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Guildhall.AgentHost.Health;

public sealed record HealthTierStatus(
    string TierId,
    string DisplayName,
    string Status,
    string Summary,
    int ConsecutiveFailures,
    DateTime? LastCheckedUtc,
    DateTime? NextRetryUtc,
    bool CanRecover);

public sealed record FiveTierHealthSnapshot(
    DateTime CheckedUtc,
    string OverallStatus,
    IReadOnlyList<HealthTierStatus> Tiers,
    VramResidencyResponse? Vram);

public sealed class FiveTierHealthSupervisor(
    IDbContextFactory<GuildhallDbContext> dbFactory,
    IDatabaseReadiness databaseReadiness,
    OllamaDiscovery ollamaDiscovery,
    McpRegistry mcpRegistry,
    McpHealthSupervisor mcpHealthSupervisor,
    DurableTaskSchedulerService schedulerService,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<FiveTierHealthSupervisor> logger) : BackgroundService
{
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] BackoffSteps =
    [
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60)
    ];

    private readonly DateTime _hostStartedUtc = timeProvider.GetUtcNow().UtcDateTime;
    private DateTime? _lastUiHeartbeatUtc;
    private VramResidencyResponse? _lastVram;
    private readonly object _gate = new();

    private HealthTierState _sqlState = new("sql-server", "SQL Server", true);
    private HealthTierState _ollamaState = new("ollama", "Ollama", true);
    private HealthTierState _mcpState = new("mcp-servers", "MCP Tool Servers", true);

    public void RecordUiHeartbeat()
    {
        lock (_gate)
        {
            _lastUiHeartbeatUtc = timeProvider.GetUtcNow().UtcDateTime;
        }
    }

    public FiveTierHealthSnapshot GetSnapshot()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime? uiHeartbeat;
        VramResidencyResponse? vram;
        HealthTierStatus sql;
        HealthTierStatus ollama;
        HealthTierStatus mcp;

        lock (_gate)
        {
            uiHeartbeat = _lastUiHeartbeatUtc;
            vram = _lastVram;
            sql = _sqlState.ToDto();
            ollama = _ollamaState.ToDto();
            mcp = _mcpState.ToDto();
        }

        var uiConnected = uiHeartbeat.HasValue && (now - uiHeartbeat.Value) <= TimeSpan.FromSeconds(90);
        var uiTier = new HealthTierStatus(
            "tauri-ui",
            "Desktop UI",
            uiConnected ? "Healthy" : "Idle",
            uiConnected
                ? "Desktop window connected."
                : uiHeartbeat.HasValue
                    ? "Desktop window closed or idle (background host active)."
                    : "No desktop window attached (background host mode).",
            0,
            uiHeartbeat,
            null,
            false);

        var schedulerTick = schedulerService.LastTickUtc;
        var hostTier = new HealthTierStatus(
            "agent-host",
            "Agent Host",
            "Healthy",
            schedulerTick.HasValue
                ? $"Running since {_hostStartedUtc:HH:mm} UTC · scheduler active."
                : $"Running since {_hostStartedUtc:HH:mm} UTC.",
            0,
            now,
            null,
            false);

        var tiers = new[] { uiTier, hostTier, sql, ollama, mcp };
        var overall = tiers.Any(t => t.Status == "Unavailable")
            ? "Degraded"
            : tiers.Any(t => t.Status == "Degraded")
                ? "Degraded"
                : "Healthy";

        return new FiveTierHealthSnapshot(now, overall, tiers, vram);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial probe shortly after startup
        await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProbeAllAsync(force: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "5-tier health monitor pass failed.");
            }

            try
            {
                await Task.Delay(MonitorInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public async Task ProbeAllAsync(bool force, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await ProbeSqlAsync(now, force, ct);
        await ProbeOllamaAsync(now, force, ct);
        await ProbeMcpServersAsync(now, force, ct);
    }

    public async Task<FiveTierHealthSnapshot> RecoverTierAsync(string tierId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        switch (tierId.ToLowerInvariant())
        {
            case "sql-server":
                await ProbeSqlAsync(now, force: true, ct);
                break;
            case "ollama":
                await ProbeOllamaAsync(now, force: true, ct);
                break;
            case "mcp-servers":
                await ProbeMcpServersAsync(now, force: true, ct);
                break;
        }
        return GetSnapshot();
    }

    private async Task ProbeSqlAsync(DateTime now, bool force, CancellationToken ct)
    {
        if (!force && IsInBackoff(_sqlState, now))
            return;

        try
        {
            var readiness = await databaseReadiness.CheckAsync(ct);
            lock (_gate)
            {
                if (readiness.Status == "ready")
                {
                    _sqlState.MarkHealthy(now, "GuildhallDev connected and schema ready.");
                }
                else
                {
                    _sqlState.MarkFailure(now, readiness.Reason ?? "database_unavailable", BackoffSteps, "Unavailable");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate)
            {
                _sqlState.MarkFailure(now, ex.Message, BackoffSteps, "Unavailable");
            }
        }
    }

    private async Task ProbeOllamaAsync(DateTime now, bool force, CancellationToken ct)
    {
        if (!force && IsInBackoff(_ollamaState, now))
            return;

        try
        {
            var discovery = await ollamaDiscovery.DiscoverAsync(ct);
            var useGpu = !string.Equals(configuration["GUILDHALL_USE_GPU"], "0", StringComparison.OrdinalIgnoreCase);
            var keepAlive = int.TryParse(configuration["GUILDHALL_KEEP_ALIVE_SECONDS"], out var s) ? s : 300;
            VramResidencyResponse? vram = null;
            if (discovery.Status is "available" or "no_models")
                vram = await ollamaDiscovery.GetRunningModelsAsync(useGpu, keepAlive, ct);

            lock (_gate)
            {
                _lastVram = vram;
                if (discovery.Status == "available")
                {
                    var loaded = vram?.Models.Count ?? 0;
                    _ollamaState.MarkHealthy(now, $"{discovery.Models.Count} model(s) available · {loaded} warm in VRAM.");
                }
                else if (discovery.Status == "no_models")
                {
                    _ollamaState.MarkDegraded(now, "Ollama running with no local models installed.");
                }
                else
                {
                    _ollamaState.MarkFailure(now, "Ollama unavailable or restarting.", BackoffSteps, "Unavailable");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate)
            {
                _ollamaState.MarkFailure(now, ex.Message, BackoffSteps, "Unavailable");
            }
        }
    }

    private async Task ProbeMcpServersAsync(DateTime now, bool force, CancellationToken ct)
    {
        if (!force && IsInBackoff(_mcpState, now))
            return;

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var enabledIds = await db.McpServerRegistrations.AsNoTracking()
                .Where(x => x.IsEnabled)
                .Select(x => x.Id)
                .ToListAsync(ct);

            var degradedCount = 0;
            foreach (var serverId in enabledIds)
            {
                var current = mcpHealthSupervisor.Get(serverId);
                if (force || current is null || current.Status != McpHealthStatuses.Connected)
                {
                    var probed = await mcpRegistry.ProbeServerAsync(serverId, ct);
                    if (probed is not null && probed.Status != McpHealthStatuses.Connected)
                        degradedCount++;
                }
            }

            lock (_gate)
            {
                if (enabledIds.Count == 0)
                {
                    _mcpState.MarkHealthy(now, "Built-in MCP providers ready (0 custom servers).");
                }
                else if (degradedCount == 0)
                {
                    _mcpState.MarkHealthy(now, $"{enabledIds.Count} custom MCP server(s) connected.");
                }
                else
                {
                    _mcpState.MarkDegraded(now, $"{degradedCount} of {enabledIds.Count} custom MCP server(s) degraded.");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate)
            {
                _mcpState.MarkFailure(now, ex.Message, BackoffSteps, "Degraded");
            }
        }
    }

    private bool IsInBackoff(HealthTierState state, DateTime now)
    {
        lock (_gate)
        {
            return state.NextRetryUtc.HasValue && now < state.NextRetryUtc.Value;
        }
    }

    private sealed class HealthTierState(string tierId, string displayName, bool canRecover)
    {
        public string Status { get; private set; } = "Healthy";
        public string Summary { get; private set; } = "Checking…";
        public int ConsecutiveFailures { get; private set; }
        public DateTime? LastCheckedUtc { get; private set; }
        public DateTime? NextRetryUtc { get; private set; }

        public void MarkHealthy(DateTime now, string summary)
        {
            Status = "Healthy";
            Summary = summary;
            ConsecutiveFailures = 0;
            LastCheckedUtc = now;
            NextRetryUtc = null;
        }

        public void MarkDegraded(DateTime now, string summary)
        {
            Status = "Degraded";
            Summary = summary;
            LastCheckedUtc = now;
            NextRetryUtc = null;
        }

        public void MarkFailure(DateTime now, string summary, TimeSpan[] backoffSteps, string failureStatus)
        {
            ConsecutiveFailures++;
            Status = failureStatus;
            Summary = summary;
            LastCheckedUtc = now;
            var idx = Math.Min(ConsecutiveFailures - 1, backoffSteps.Length - 1);
            NextRetryUtc = now.Add(backoffSteps[idx]);
        }

        public HealthTierStatus ToDto() =>
            new(tierId, displayName, Status, Summary, ConsecutiveFailures, LastCheckedUtc, NextRetryUtc, canRecover);
    }
}
