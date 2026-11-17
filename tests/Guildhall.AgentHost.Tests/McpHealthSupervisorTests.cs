using Guildhall.AgentHost.Mcp;

namespace Guildhall.AgentHost.Tests;

public sealed class McpHealthSupervisorTests
{
    private sealed class TestTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }

    [Fact]
    public void TransitionsThroughDegradedBackoffToFailedAndRecoversOnProbe()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var supervisor = new McpHealthSupervisor(clock);

        supervisor.GetOrCreate("custom-mcp", "Custom MCP", "stdio");
        Assert.True(supervisor.CanAttempt("custom-mcp"));

        // 1st failure -> Degraded, 2s backoff
        var s1 = supervisor.RecordFailure("custom-mcp", "Custom MCP", "stdio", "timeout");
        Assert.Equal(McpHealthStatuses.Degraded, s1.Status);
        Assert.Equal(1, s1.ConsecutiveFailures);
        Assert.False(supervisor.CanAttempt("custom-mcp"));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(supervisor.CanAttempt("custom-mcp"));

        // 2nd failure -> Degraded, 5s backoff
        var s2 = supervisor.RecordFailure("custom-mcp", "Custom MCP", "stdio", "timeout");
        Assert.Equal(McpHealthStatuses.Degraded, s2.Status);
        Assert.Equal(2, s2.ConsecutiveFailures);
        Assert.False(supervisor.CanAttempt("custom-mcp"));

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(supervisor.CanAttempt("custom-mcp"));

        // 3rd failure -> Failed (circuit open until manual probe)
        var s3 = supervisor.RecordFailure("custom-mcp", "Custom MCP", "stdio", "crashed");
        Assert.Equal(McpHealthStatuses.Failed, s3.Status);
        Assert.Equal(3, s3.ConsecutiveFailures);
        Assert.False(supervisor.CanAttempt("custom-mcp"));

        // Manual probe reset + success -> Connected
        supervisor.ResetForProbe("custom-mcp", "Custom MCP", "stdio");
        var recovered = supervisor.RecordSuccess("custom-mcp", "Custom MCP", "stdio");
        Assert.Equal(McpHealthStatuses.Connected, recovered.Status);
        Assert.Equal(0, recovered.ConsecutiveFailures);
        Assert.True(supervisor.CanAttempt("custom-mcp"));
    }
}
