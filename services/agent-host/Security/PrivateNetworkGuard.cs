using System.Net;
using System.Net.Sockets;

namespace Guildhall.AgentHost.Security;

public sealed record EndpointValidationResult(bool IsAllowed, bool IsPrivateTunnel, string? Reason);

public sealed class PrivateNetworkGuard
{
    public EndpointValidationResult ValidateMcpEndpoint(Uri? uri, bool allowPrivateTunnelHttp)
    {
        if (uri is null || !uri.IsAbsoluteUri)
            return new EndpointValidationResult(false, false, "EndpointUrl must be a valid absolute URI.");

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return new EndpointValidationResult(true, IsPrivateTunnelHost(uri.Host), null);

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return new EndpointValidationResult(false, false, "EndpointUrl scheme must be https or http.");

        if (uri.IsLoopback)
            return new EndpointValidationResult(true, false, null);

        var isPrivateTunnel = IsPrivateTunnelHost(uri.Host);
        if (allowPrivateTunnelHttp && isPrivateTunnel)
            return new EndpointValidationResult(true, true, null);

        return new EndpointValidationResult(
            false,
            isPrivateTunnel,
            isPrivateTunnel
                ? "Private-tunnel HTTP requires AllowPrivateTunnelHttp to be explicitly enabled."
                : "Plain HTTP is only allowed for loopback or explicitly opted-in private tunnel (WireGuard/Tailscale) hosts.");
    }

    public static bool IsPrivateTunnelHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var trimmed = host.Trim().Trim('[', ']');
        if (trimmed.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IPAddress.TryParse(trimmed, out var ip))
            return false;

        return IsPrivateTunnelIp(ip);
    }

    public static bool IsPrivateTunnelIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return false;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            // 10.0.0.0/8 (RFC 1918 / WireGuard common)
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12 (RFC 1918)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16 (RFC 1918)
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 100.64.0.0/10 (CGNAT / Tailscale)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            // fc00::/7 (Unique Local Address, WireGuard/Tailscale IPv6)
            return (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }
}
