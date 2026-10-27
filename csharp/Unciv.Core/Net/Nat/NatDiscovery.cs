using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Unciv.Net.Nat;

/// <summary>
/// Discovers UPnP IGD and NAT-PMP routers on the local network (SERVER.md 6.5).
/// BCL-only, zero external dependencies.
/// </summary>
public static class NatDiscovery
{
    private const string SsdpMulticastAddress = "239.255.255.250";
    private const int SsdpPort = 1900;

    private static readonly Regex LocationRegex = new(
        @"^LOCATION:\s*(?<url>https?://\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex ServiceBlockRegex = new(
        @"<service>(?<service>[\s\S]*?)</service>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ServiceTypeRegex = new(
        @"<serviceType>(?<type>urn:schemas-upnp-org:service:WAN(?:IP|PPP)Connection:[1-2])</serviceType>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ControlUrlRegex = new(
        @"<controlURL>(?<url>[^<]+)</controlURL>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Attempts to discover a compatible UPnP IGD or NAT-PMP device on the local network.
    /// Returns null if neither is available.
    /// </summary>
    public static async Task<INatDevice?> DiscoverAsync(
        TimeSpan? timeout = null,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        var discoveryTimeout = timeout ?? TimeSpan.FromSeconds(3);

        // 1. Try UPnP IGD
        var upnp = await DiscoverUpnpAsync(discoveryTimeout, httpClient, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (upnp != null) return upnp;

        // 2. Try NAT-PMP on default gateway
        var defaultGateway = GetDefaultGateway();
        if (defaultGateway != null)
        {
            var natPmp = await DiscoverNatPmpAsync(defaultGateway, discoveryTimeout, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (natPmp != null) return natPmp;
        }

        return null;
    }

    /// <summary>
    /// Discovers a UPnP IGD device by broadcasting SSDP M-SEARCH messages.
    /// </summary>
    public static async Task<UpnpNatDevice?> DiscoverUpnpAsync(
        TimeSpan timeout,
        HttpClient? httpClient = null,
        UdpClient? udpClient = null,
        CancellationToken cancellationToken = default)
    {
        var searchTargets = new[]
        {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANPPPConnection:1"
        };

        var client = udpClient ?? new UdpClient();
        var ownsClient = udpClient == null;
        if (ownsClient)
        {
            client.EnableBroadcast = true;
            client.Client.ReceiveTimeout = Math.Max(100, (int)timeout.TotalMilliseconds);
            client.Client.SendTimeout = Math.Max(100, (int)timeout.TotalMilliseconds);
        }

        var multicastEndpoint = new IPEndPoint(IPAddress.Parse(SsdpMulticastAddress), SsdpPort);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        foreach (var st in searchTargets)
        {
            var searchMsg = $"M-SEARCH * HTTP/1.1\r\n" +
                            $"HOST: {SsdpMulticastAddress}:{SsdpPort}\r\n" +
                            $"ST: {st}\r\n" +
                            $"MAN: \"ssdp:discover\"\r\n" +
                            $"MX: 2\r\n\r\n";

            var bytes = Encoding.ASCII.GetBytes(searchMsg);
            try
            {
                await client.SendAsync(bytes, bytes.Length, multicastEndpoint).ConfigureAwait(false);
            }
            catch { }
        }

        var clientToUse = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        try
        {
            while (!cts.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await client.ReceiveAsync(cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }

                var text = Encoding.UTF8.GetString(result.Buffer);
                var locationMatch = LocationRegex.Match(text);
                if (!locationMatch.Success) continue;

                if (!Uri.TryCreate(locationMatch.Groups["url"].Value.Trim(), UriKind.Absolute, out var descUri))
                    continue;

                try
                {
                    using var httpCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, httpCts.Token);
                    var descXml = await clientToUse.GetStringAsync(descUri, linked.Token).ConfigureAwait(false);
                    var serviceInfo = ParseUpnpDeviceDescription(descXml, descUri);
                    if (serviceInfo.HasValue)
                    {
                        return new UpnpNatDevice(serviceInfo.Value.ControlUri, serviceInfo.Value.ServiceType, clientToUse);
                    }
                }
                catch { }
            }
        }
        finally
        {
            if (ownsClient)
            {
                client.Dispose();
            }
            if (httpClient == null)
            {
                clientToUse.Dispose();
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a UPnP device description XML and extracts the control URL and service type for WANIP/WANPPPConnection.
    /// </summary>
    public static (Uri ControlUri, string ServiceType)? ParseUpnpDeviceDescription(string xml, Uri baseUri)
    {
        var matches = ServiceBlockRegex.Matches(xml);
        foreach (Match match in matches)
        {
            var block = match.Groups["service"].Value;
            var typeMatch = ServiceTypeRegex.Match(block);
            var urlMatch = ControlUrlRegex.Match(block);

            if (typeMatch.Success && urlMatch.Success)
            {
                var serviceType = typeMatch.Groups["type"].Value;
                var rawUrl = urlMatch.Groups["url"].Value.Trim();

                if (Uri.TryCreate(baseUri, rawUrl, out var controlUri))
                {
                    return (controlUri, serviceType);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Tests for a NAT-PMP service on the specified gateway address.
    /// </summary>
    public static async Task<NatPmpDevice?> DiscoverNatPmpAsync(
        IPAddress gatewayAddress,
        TimeSpan timeout,
        int port = NatPmpDevice.DefaultPort,
        CancellationToken cancellationToken = default)
    {
        var device = new NatPmpDevice(gatewayAddress, port: port, timeout: timeout);
        try
        {
            var ip = await device.GetExternalIpAsync(cancellationToken).ConfigureAwait(false);
            if (ip != null)
            {
                return device;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Inspects local network interfaces to detect the primary default gateway IPv4 address.
    /// </summary>
    public static IPAddress? GetDefaultGateway()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var ipProps = ni.GetIPProperties();
                foreach (var gateway in ipProps.GatewayAddresses)
                {
                    if (gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.Any.Equals(gateway.Address) &&
                        !IPAddress.None.Equals(gateway.Address))
                    {
                        return gateway.Address;
                    }
                }
            }
        }
        catch { }

        return null;
    }
}
