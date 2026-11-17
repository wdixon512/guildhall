using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Guildhall.AgentHost.Security;

namespace Guildhall.AgentHost.Mcp;

public sealed record TunnelInterfaceInfo(
    string Name,
    string Kind,
    string Status,
    IReadOnlyList<string> Addresses);

public sealed record PortExposureCheck(
    int Port,
    string ServiceName,
    string Posture,
    IReadOnlyList<string> ListeningAddresses,
    string Summary);

public sealed record PrivateNetworkStatusResponse(
    bool WireGuardDetected,
    bool TailscaleDetected,
    IReadOnlyList<TunnelInterfaceInfo> TunnelInterfaces,
    IReadOnlyList<PortExposureCheck> PortChecks,
    DateTimeOffset CheckedUtc);

public sealed class PrivateNetworkProbeService(TimeProvider timeProvider)
{
    public PrivateNetworkStatusResponse Inspect()
    {
        var tunnels = new List<TunnelInterfaceInfo>();
        var wireGuardDetected = false;
        var tailscaleDetected = false;

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                var nameAndDesc = $"{nic.Name} {nic.Description}";
                var isWireGuard = nameAndDesc.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
                                  nic.Name.StartsWith("wg", StringComparison.OrdinalIgnoreCase);
                var isTailscale = nameAndDesc.Contains("Tailscale", StringComparison.OrdinalIgnoreCase);

                var addrs = nic.GetIPProperties().UnicastAddresses
                    .Select(a => a.Address)
                    .Where(ip => ip.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    .ToList();

                var hasCgnat = addrs.Any(ip =>
                {
                    if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
                    var b = ip.GetAddressBytes();
                    return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
                });

                if (!isWireGuard && !isTailscale && !hasCgnat)
                    continue;

                var kind = isTailscale || (hasCgnat && !isWireGuard) ? "Tailscale" : "WireGuard";
                if (nic.OperationalStatus == OperationalStatus.Up)
                {
                    if (kind == "WireGuard") wireGuardDetected = true;
                    if (kind == "Tailscale") tailscaleDetected = true;
                }

                tunnels.Add(new TunnelInterfaceInfo(
                    nic.Name,
                    kind,
                    nic.OperationalStatus.ToString(),
                    addrs.Select(a => a.ToString()).ToArray()));
            }
        }
        catch
        {
            // Network interface enumeration is best-effort; never crash the host.
        }

        var portChecks = new[]
        {
            CheckTcpPort(1433, "SQL Server"),
            CheckTcpPort(11434, "Ollama"),
        };

        return new PrivateNetworkStatusResponse(
            wireGuardDetected,
            tailscaleDetected,
            tunnels,
            portChecks,
            timeProvider.GetUtcNow());
    }

    private static PortExposureCheck CheckTcpPort(int port, string serviceName)
    {
        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Where(ep => ep.Port == port)
                .Select(ep => ep.Address)
                .Distinct()
                .ToList();

            if (listeners.Count == 0)
            {
                return new PortExposureCheck(
                    port,
                    serviceName,
                    "not_listening",
                    [],
                    $"{serviceName} (port {port}) is not currently listening on TCP.");
            }

            var hasWildcard = listeners.Any(ip => ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any));
            var hasPublicIp = listeners.Any(ip => !IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any) && !PrivateNetworkGuard.IsPrivateTunnelIp(ip));

            if (hasWildcard || hasPublicIp)
            {
                return new PortExposureCheck(
                    port,
                    serviceName,
                    "wildcard_or_non_loopback",
                    listeners.Select(ip => ip.ToString()).ToArray(),
                    $"{serviceName} (port {port}) is bound to {string.Join(", ", listeners)}; ensure Windows Firewall blocks public inbound traffic.");
            }

            return new PortExposureCheck(
                port,
                serviceName,
                "private_only",
                listeners.Select(ip => ip.ToString()).ToArray(),
                $"{serviceName} (port {port}) is bound to loopback/private tunnel addresses only.");
        }
        catch
        {
            return new PortExposureCheck(port, serviceName, "unknown", [], $"Could not inspect TCP listener on port {port}.");
        }
    }
}
