using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Tests;

public sealed class PrivateNetworkGuardTests
{
    private readonly PrivateNetworkGuard _guard = new();

    [Theory]
    [InlineData("https://mcp.example.com/rpc", false, true)]
    [InlineData("http://127.0.0.1:8080/mcp", false, true)]
    [InlineData("http://localhost:9000/mcp", false, true)]
    [InlineData("http://10.66.0.1:8080/mcp", true, true)]
    [InlineData("http://100.101.102.103:8080/mcp", true, true)]
    [InlineData("http://192.168.1.50:8080/mcp", true, true)]
    [InlineData("http://droplet.tail1234.ts.net:8080/mcp", true, true)]
    [InlineData("http://10.66.0.1:8080/mcp", false, false)]
    [InlineData("http://8.8.8.8:8080/mcp", true, false)]
    [InlineData("http://public.example.com/mcp", true, false)]
    [InlineData("ftp://10.66.0.1/mcp", true, false)]
    public void ValidatesStreamableHttpEndpoints(string url, bool allowPrivateTunnelHttp, bool expectedAllowed)
    {
        var uri = new Uri(url);
        var result = _guard.ValidateMcpEndpoint(uri, allowPrivateTunnelHttp);
        Assert.Equal(expectedAllowed, result.IsAllowed);
    }
}
