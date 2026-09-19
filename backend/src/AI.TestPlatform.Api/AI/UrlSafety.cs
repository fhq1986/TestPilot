using System.Net;
using System.Net.Sockets;

namespace AI.TestPlatform.Api.AI;

// SSRF 防护：import-swagger URL 抓取前校验 host，拒绝回环/内网/链路本地/保留地址段
public static class UrlSafety
{
    public static bool IsPrivateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return true;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        if (IPAddress.TryParse(host, out var literal))
            return IsPrivateAddress(literal);

        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(host);
        }
        catch
        {
            // 解析失败：不阻断，交由抓取环节自然报错
            return false;
        }
        return addresses.Any(IsPrivateAddress);
    }

    public static bool IsPrivateAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 10) return true;                                // 10.0.0.0/8
            if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return true; // 172.16.0.0/12
            if (bytes[0] == 192 && bytes[1] == 168) return true;            // 192.168.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254) return true;            // 169.254.0.0/16
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC; // fc00::/7
        }

        return false;
    }
}
