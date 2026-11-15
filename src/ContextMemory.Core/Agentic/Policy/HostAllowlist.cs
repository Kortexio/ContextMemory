using System.Net;
using System.Net.Sockets;

namespace ContextMemory.Core.Agentic;

/// <summary>Shared host allowlist + basic SSRF checks for HTTP/browser/vision URL tools.</summary>
public static class HostAllowlist
{
    public static bool IsHostAllowed(IReadOnlyList<string> allowedHosts, string urlOrHost)
    {
        if (allowedHosts.Count == 0)
            return false;

        if (!TryGetHost(urlOrHost, out var host))
            return false;

        foreach (var allowed in allowedHosts)
        {
            if (string.IsNullOrWhiteSpace(allowed))
                continue;

            var needle = allowed.Trim();
            if (host.Equals(needle, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool TryValidatePublicHttpUrl(
        string url,
        IReadOnlyList<string> allowedHosts,
        out Uri uri,
        out string error)
    {
        uri = null!;
        error = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https"))
        {
            error = "Only absolute http/https URLs are allowed.";
            return false;
        }

        if (!IsHostAllowed(allowedHosts, parsed.Host))
        {
            error = $"Host '{parsed.Host}' is not in the allowlist.";
            return false;
        }

        if (IsBlockedHostOrIp(parsed.Host))
        {
            error = $"Host '{parsed.Host}' is blocked (loopback/private/link-local).";
            return false;
        }

        uri = parsed;
        return true;
    }

    public static bool IsBlockedHostOrIp(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return true;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IPAddress.TryParse(host, out var ip))
            return IsPrivateOrSpecial(ip);

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            foreach (var addr in addresses)
            {
                if (IsPrivateOrSpecial(addr))
                    return true;
            }
        }
        catch
        {
            // DNS failure is not treated as SSRF; the HTTP client will surface the error.
            return false;
        }

        return false;
    }

    public static bool IsPrivateOrSpecial(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
            // 169.254.0.0/16 link-local
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;
            // 127.0.0.0/8 already covered by IsLoopback; 0.0.0.0/8
            if (bytes[0] == 0)
                return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal)
                return true;
            if (ip.Equals(IPAddress.IPv6Loopback))
                return true;
        }

        return false;
    }

    private static bool TryGetHost(string urlOrHost, out string host)
    {
        host = string.Empty;
        if (string.IsNullOrWhiteSpace(urlOrHost))
            return false;

        if (Uri.TryCreate(urlOrHost, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            return !string.IsNullOrWhiteSpace(host);
        }

        host = urlOrHost.Trim();
        return host.Length > 0;
    }
}
