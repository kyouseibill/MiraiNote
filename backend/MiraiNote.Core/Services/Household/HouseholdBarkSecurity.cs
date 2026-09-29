using System.Net;
using System.Net.Sockets;
using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// Bark 地址只接受 https，并且主机必须在白名单里。默认只有 api.day.app，配置里的主机是追加。
/// </summary>
public static class HouseholdBarkAddresses
{
    public const string DefaultHost = "api.day.app";
    public const string HttpsOnlyMessage = "Bark 地址只接受 https";
    public const string HostRejectedMessage = "Bark 地址不在允许的主机名单里";
    public const string UnusableMessage = "Bark 地址不可用";

    public static IReadOnlySet<string> AllowedHosts(HouseholdNotificationOptions options)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DefaultHost };
        if (options.BarkAllowedHosts == null)
            return hosts;

        foreach (var raw in options.BarkAllowedHosts)
        {
            var host = NormalizeConfiguredHost(raw);
            if (host != null)
                hosts.Add(host);
        }

        return hosts;
    }

    public static string Require(string? value, HouseholdNotificationOptions options, int maxLength)
    {
        if (value != null && HouseholdUrls.ContainsControlOrFormat(value))
            throw new BusinessException(HttpsOnlyMessage, 400);

        if (!HouseholdUrls.TryNormalize(value, out var absolute) || absolute.Length > maxLength)
            throw new BusinessException(HttpsOnlyMessage, 400);

        if (!Uri.TryCreate(absolute, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new BusinessException(HttpsOnlyMessage, 400);

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new BusinessException(UnusableMessage, 400);

        var host = uri.IdnHost.TrimEnd('.');
        if (host.Length == 0)
            throw new BusinessException(HttpsOnlyMessage, 400);

        if (IPAddress.TryParse(host, out var literal) && HouseholdBarkNetwork.IsBlocked(literal))
            throw new BusinessException(UnusableMessage, 400);

        if (!AllowedHosts(options).Contains(host))
            throw new BusinessException(HostRejectedMessage, 400);

        return absolute;
    }

    private static string? NormalizeConfiguredHost(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || HouseholdUrls.ContainsControlOrFormat(raw))
            return null;

        var trimmed = raw.Trim().TrimEnd('.');
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                return null;
            trimmed = uri.IdnHost.TrimEnd('.');
        }

        if (trimmed.Length == 0 || trimmed.Contains('/') || trimmed.Contains('@'))
            return null;
        return trimmed;
    }
}

/// <summary>
/// 连接前看 DNS 解析出来的实际地址。任一地址落在内网、环回、链路本地或组播，就整次拒绝，用来挡住 DNS rebinding。
/// </summary>
public static class HouseholdBarkNetwork
{
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return IsBlockedV4(address.GetAddressBytes());

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return IsBlockedV6(address.GetAddressBytes());

        return true;
    }

    public static void EnsurePublic(IReadOnlyList<IPAddress> addresses)
    {
        if (addresses.Count == 0 || addresses.Any(IsBlocked))
            throw new HouseholdNotificationDeliveryException("Bark 通知发送失败");
    }

    private static bool IsBlockedV4(byte[] bytes)
    {
        var first = bytes[0];
        var second = bytes[1];
        if (first == 0)
            return true;
        if (first == 10)
            return true;
        if (first == 100 && second is >= 64 and <= 127)
            return true;
        if (first == 127)
            return true;
        if (first == 169 && second == 254)
            return true;
        if (first == 172 && second is >= 16 and <= 31)
            return true;
        if (first == 192 && second == 168)
            return true;
        if (first >= 224)
            return true;
        return false;
    }

    private static bool IsBlockedV6(byte[] bytes)
    {
        if ((bytes[0] & 0xFE) == 0xFC)
            return true;
        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
            return true;
        if (bytes[0] == 0xFF)
            return true;
        return false;
    }
}

public static class HouseholdBarkConnector
{
    public static async ValueTask<Stream> ConnectCallback(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        return await ConnectAsync(
            endpoint.Host,
            endpoint.Port,
            static (host, token) => Dns.GetHostAddressesAsync(host, token),
            OpenSocketAsync,
            cancellationToken);
    }

    internal static async Task<Stream> ConnectAsync(
        string host,
        int port,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, int, CancellationToken, Task<Stream>> open,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
            addresses = [literal];
        else
            addresses = await resolve(host, cancellationToken);

        HouseholdBarkNetwork.EnsurePublic(addresses);
        return await open(addresses[0], port, cancellationToken);
    }

    private static async Task<Stream> OpenSocketAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
