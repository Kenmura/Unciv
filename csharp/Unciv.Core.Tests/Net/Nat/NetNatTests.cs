using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unciv.Net.Nat;

namespace Unciv.Core.Tests.Net.Nat;

public sealed class NetNatTests
{
    private const string SampleUpnpDescription = """
        <?xml version="1.0"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <device>
            <deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>
            <serviceList>
              <service>
                <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
                <serviceId>urn:upnp-org:serviceId:WANIPConn1</serviceId>
                <controlURL>/ipc</controlURL>
                <eventSubURL>/ipc_event</eventSubURL>
                <SCPDURL>/ipc_scpd.xml</SCPDURL>
              </service>
            </serviceList>
          </device>
        </root>
        """;

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handler(request));
        }
    }

    [Fact]
    public void Upnp_ParseDeviceDescription_ResolvesRelativeAndAbsoluteControlUrls()
    {
        var baseUri = new Uri("http://192.168.1.1:1900/rootDesc.xml");

        var result = NatDiscovery.ParseUpnpDeviceDescription(SampleUpnpDescription, baseUri);
        Assert.NotNull(result);
        Assert.Equal("urn:schemas-upnp-org:service:WANIPConnection:1", result.Value.ServiceType);
        Assert.Equal(new Uri("http://192.168.1.1:1900/ipc"), result.Value.ControlUri);
    }

    [Fact]
    public void Upnp_ParseDeviceDescription_HandlesWanPppConnection()
    {
        var pppXml = """
            <root>
              <service>
                <serviceType>urn:schemas-upnp-org:service:WANPPPConnection:1</serviceType>
                <controlURL>http://10.0.0.1:5000/ppp_control</controlURL>
              </service>
            </root>
            """;

        var baseUri = new Uri("http://10.0.0.1:5000/desc.xml");
        var result = NatDiscovery.ParseUpnpDeviceDescription(pppXml, baseUri);
        Assert.NotNull(result);
        Assert.Equal("urn:schemas-upnp-org:service:WANPPPConnection:1", result.Value.ServiceType);
        Assert.Equal(new Uri("http://10.0.0.1:5000/ppp_control"), result.Value.ControlUri);
    }

    [Fact]
    public void Upnp_ParseDeviceDescription_ReturnsNullOnMissingService()
    {
        var invalidXml = "<root><service><serviceType>urn:schemas-upnp-org:service:Layer3Forwarding:1</serviceType><controlURL>/l3</controlURL></service></root>";
        var result = NatDiscovery.ParseUpnpDeviceDescription(invalidXml, new Uri("http://192.168.1.1/"));
        Assert.Null(result);
    }

    [Fact(Timeout = 5000)]
    public async Task UpnpNatDevice_GetExternalIpAsync_ParsesSoapResponse()
    {
        var handler = new MockHttpHandler
        {
            Handler = request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Contains("\"urn:schemas-upnp-org:service:WANIPConnection:1#GetExternalIPAddress\"", request.Headers.GetValues("SOAPACTION"));

                var soapResp = """
                    <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
                      <s:Body>
                        <u:GetExternalIPAddressResponse xmlns:u="urn:schemas-upnp-org:service:WANIPConnection:1">
                          <NewExternalIPAddress>203.0.113.42</NewExternalIPAddress>
                        </u:GetExternalIPAddressResponse>
                      </s:Body>
                    </s:Envelope>
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(soapResp, Encoding.UTF8, "text/xml")
                };
            }
        };

        using var httpClient = new HttpClient(handler);
        var device = new UpnpNatDevice(new Uri("http://192.168.1.1/ipc"), "urn:schemas-upnp-org:service:WANIPConnection:1", httpClient);

        var ip = await device.GetExternalIpAsync();
        Assert.Equal(IPAddress.Parse("203.0.113.42"), ip);
    }

    [Fact(Timeout = 5000)]
    public async Task UpnpNatDevice_AddAndDeletePortMapping_SendsCorrectSoapPayloads()
    {
        string? lastAction = null;
        string? lastBody = null;

        var handler = new MockHttpHandler
        {
            Handler = request =>
            {
                lastAction = request.Headers.GetValues("SOAPACTION").FirstOrDefault();
                lastBody = request.Content?.ReadAsStringAsync().Result;

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<response/>", Encoding.UTF8, "text/xml")
                };
            }
        };

        using var httpClient = new HttpClient(handler);
        var device = new UpnpNatDevice(new Uri("http://192.168.1.1/ipc"), "urn:schemas-upnp-org:service:WANIPConnection:1", httpClient);

        // 1. Add port mapping
        var addOk = await device.AddPortMappingAsync(
            internalPort: 47821,
            externalPort: 47821,
            localAddress: IPAddress.Parse("192.168.1.50"),
            protocol: "TCP",
            description: "Unciv-Test",
            leaseDurationSeconds: 3600);

        Assert.True(addOk);
        Assert.Equal("\"urn:schemas-upnp-org:service:WANIPConnection:1#AddPortMapping\"", lastAction);
        Assert.NotNull(lastBody);
        Assert.Contains("<NewExternalPort>47821</NewExternalPort>", lastBody);
        Assert.Contains("<NewProtocol>TCP</NewProtocol>", lastBody);
        Assert.Contains("<NewInternalClient>192.168.1.50</NewInternalClient>", lastBody);
        Assert.Contains("<NewPortMappingDescription>Unciv-Test</NewPortMappingDescription>", lastBody);

        // 2. Delete port mapping
        var delOk = await device.DeletePortMappingAsync(externalPort: 47821, protocol: "TCP");
        Assert.True(delOk);
        Assert.Equal("\"urn:schemas-upnp-org:service:WANIPConnection:1#DeletePortMapping\"", lastAction);
        Assert.NotNull(lastBody);
        Assert.Contains("<NewExternalPort>47821</NewExternalPort>", lastBody);
    }

    [Fact(Timeout = 5000)]
    public async Task NatPmpDevice_GetExternalIpAsync_QueriesGatewayOverUdp()
    {
        using var fakeGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = (fakeGateway.Client.LocalEndPoint as IPEndPoint)?.Port ?? 0;

        var device = new NatPmpDevice(IPAddress.Loopback, port, timeout: TimeSpan.FromSeconds(2));

        var serverTask = Task.Run(async () =>
        {
            var req = await fakeGateway.ReceiveAsync();
            // Verify version 0, opcode 0
            Assert.Equal(0, req.Buffer[0]);
            Assert.Equal(0, req.Buffer[1]);

            // Form 12-byte response: [version=0, opcode=128, result=0,0, epoch=1,2,3,4, ip=198,51,100,77]
            var resp = new byte[12];
            resp[0] = 0;
            resp[1] = 128; // 128 + 0
            BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(2, 2), 0); // Result = 0 (Success)
            BinaryPrimitives.WriteUInt32BigEndian(resp.AsSpan(4, 4), 1000); // Epoch
            resp[8] = 198;
            resp[9] = 51;
            resp[10] = 100;
            resp[11] = 77;

            await fakeGateway.SendAsync(resp, resp.Length, req.RemoteEndPoint);
        });

        var ip = await device.GetExternalIpAsync();
        await serverTask;

        Assert.Equal(IPAddress.Parse("198.51.100.77"), ip);
    }

    [Fact(Timeout = 5000)]
    public async Task NatPmpDevice_AddAndDeletePortMapping_InteractsWithGateway()
    {
        using var fakeGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = (fakeGateway.Client.LocalEndPoint as IPEndPoint)?.Port ?? 0;

        var device = new NatPmpDevice(IPAddress.Loopback, port, timeout: TimeSpan.FromSeconds(2));

        var serverTask = Task.Run(async () =>
        {
            // First request: Add mapping
            var req1 = await fakeGateway.ReceiveAsync();
            Assert.Equal(0, req1.Buffer[0]);
            Assert.Equal(2, req1.Buffer[1]); // Opcode 2 = TCP
            var internalPort = BinaryPrimitives.ReadUInt16BigEndian(req1.Buffer.AsSpan(4, 2));
            var externalPort = BinaryPrimitives.ReadUInt16BigEndian(req1.Buffer.AsSpan(6, 2));
            var lifetime = BinaryPrimitives.ReadUInt32BigEndian(req1.Buffer.AsSpan(8, 4));

            Assert.Equal(47821, internalPort);
            Assert.Equal(47821, externalPort);
            Assert.Equal(7200u, lifetime);

            // Respond with 16 bytes: success
            var resp1 = new byte[16];
            resp1[0] = 0;
            resp1[1] = 130; // 128 + 2
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(8, 2), internalPort);
            BinaryPrimitives.WriteUInt16BigEndian(resp1.AsSpan(10, 2), externalPort);
            BinaryPrimitives.WriteUInt32BigEndian(resp1.AsSpan(12, 4), lifetime);
            await fakeGateway.SendAsync(resp1, resp1.Length, req1.RemoteEndPoint);

            // Second request: Delete mapping (lifetime = 0)
            var req2 = await fakeGateway.ReceiveAsync();
            Assert.Equal(0, req2.Buffer[0]);
            Assert.Equal(2, req2.Buffer[1]);
            var delLifetime = BinaryPrimitives.ReadUInt32BigEndian(req2.Buffer.AsSpan(8, 4));
            Assert.Equal(0u, delLifetime);

            var resp2 = new byte[16];
            resp2[0] = 0;
            resp2[1] = 130;
            BinaryPrimitives.WriteUInt16BigEndian(resp2.AsSpan(2, 2), 0);
            await fakeGateway.SendAsync(resp2, resp2.Length, req2.RemoteEndPoint);
        });

        var addOk = await device.AddPortMappingAsync(
            internalPort: 47821,
            externalPort: 47821,
            localAddress: IPAddress.Loopback,
            protocol: "TCP",
            leaseDurationSeconds: 7200);

        Assert.True(addOk);

        var delOk = await device.DeletePortMappingAsync(externalPort: 47821, protocol: "TCP");
        Assert.True(delOk);

        await serverTask;
    }

    [Fact(Timeout = 5000)]
    public async Task NatPmpDevice_UnresponsiveGateway_ReturnsNullGracefully()
    {
        // Port 1 on loopback should not answer UDP
        var device = new NatPmpDevice(IPAddress.Loopback, port: 1, timeout: TimeSpan.FromMilliseconds(100));
        var ip = await device.GetExternalIpAsync();
        Assert.Null(ip);

        var mapOk = await device.AddPortMappingAsync(47821, 47821, IPAddress.Loopback);
        Assert.False(mapOk);
    }

    [Fact(Timeout = 5000)]
    public async Task PortMapper_Lifecycle_MapsOnStartAndUnmapsOnDispose()
    {
        var fakeDevice = new FakeNatDevice
        {
            ExternalIp = IPAddress.Parse("203.0.113.100")
        };

        var mapper = new PortMapper(
            fakeDevice,
            port: 47821,
            localAddress: IPAddress.Parse("192.168.1.10"),
            protocol: "TCP",
            description: "Unciv-PortMapper-Test");

        Assert.False(mapper.IsMapped);
        Assert.Null(mapper.ExternalIp);

        var started = await mapper.StartAsync();
        Assert.True(started);
        Assert.True(mapper.IsMapped);
        Assert.Equal(IPAddress.Parse("203.0.113.100"), mapper.ExternalIp);
        Assert.Equal(47821, fakeDevice.LastAddedExternalPort);

        // Dispose removes mapping
        await mapper.DisposeAsync();
        Assert.False(mapper.IsMapped);
        Assert.Equal(47821, fakeDevice.LastDeletedExternalPort);

        // Multiple disposes are idempotent
        await mapper.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task PortMapper_DeviceFailure_IsNonFatal()
    {
        var failingDevice = new FakeNatDevice
        {
            ShouldFail = true
        };

        var mapper = new PortMapper(failingDevice, port: 47821);
        var ok = await mapper.StartAsync();
        Assert.False(ok);
        Assert.False(mapper.IsMapped);

        // Dispose does not throw even if mapping was never established
        await mapper.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task NatDiscovery_DiscoverUpnpAsync_ReceivesSsdpAndParsesDevice()
    {
        using var clientUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var clientPort = (clientUdp.Client.LocalEndPoint as IPEndPoint)?.Port ?? 0;

        using var serverUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var serverPort = (serverUdp.Client.LocalEndPoint as IPEndPoint)?.Port ?? 0;

        var handler = new MockHttpHandler
        {
            Handler = request =>
            {
                if (request.RequestUri?.AbsolutePath == "/desc.xml")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(SampleUpnpDescription, Encoding.UTF8, "text/xml")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        using var httpClient = new HttpClient(handler);

        // Server sends SSDP response to client
        var ssdpResponse = $"HTTP/1.1 200 OK\r\n" +
                           $"LOCATION: http://127.0.0.1:{serverPort}/desc.xml\r\n" +
                           $"ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";
        var ssdpBytes = Encoding.ASCII.GetBytes(ssdpResponse);

        var serverTask = Task.Run(async () =>
        {
            // Give client time to start listening
            await Task.Delay(50);
            await serverUdp.SendAsync(ssdpBytes, ssdpBytes.Length, new IPEndPoint(IPAddress.Loopback, clientPort));
        });

        var device = await NatDiscovery.DiscoverUpnpAsync(
            TimeSpan.FromSeconds(2),
            httpClient: httpClient,
            udpClient: clientUdp);

        await serverTask;

        Assert.NotNull(device);
        Assert.Equal("urn:schemas-upnp-org:service:WANIPConnection:1", device.ServiceType);
        Assert.Equal(new Uri($"http://127.0.0.1:{serverPort}/ipc"), device.ControlUri);
    }

    [Fact(Timeout = 5000)]
    public async Task NatDiscovery_DiscoverNatPmpAsync_SucceedsWithResponsiveGateway()
    {
        using var fakeGateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = (fakeGateway.Client.LocalEndPoint as IPEndPoint)?.Port ?? 0;

        var serverTask = Task.Run(async () =>
        {
            var req = await fakeGateway.ReceiveAsync();
            var resp = new byte[12];
            resp[1] = 128;
            resp[8] = 203; resp[9] = 0; resp[10] = 113; resp[11] = 99;
            await fakeGateway.SendAsync(resp, resp.Length, req.RemoteEndPoint);
        });

        var device = await NatDiscovery.DiscoverNatPmpAsync(
            IPAddress.Loopback,
            TimeSpan.FromSeconds(2),
            port: port);

        await serverTask;

        Assert.NotNull(device);
        Assert.Equal(NatProtocol.NatPmp, device.Protocol);
        Assert.Equal(port, device.GatewayEndPoint.Port);
    }

    [Fact(Timeout = 5000)]
    public async Task NatDiscovery_DiscoverAsync_ReturnsDeviceOrTimesOutGracefully()
    {
        // When running on a network with a real UPnP/NAT-PMP gateway, it may discover it;
        // otherwise it times out gracefully and returns null. Neither case should throw an unhandled exception.
        var device = await NatDiscovery.DiscoverAsync(TimeSpan.FromMilliseconds(500));
        if (device != null)
        {
            Assert.True(device.Protocol is NatProtocol.Upnp or NatProtocol.NatPmp);
        }
    }

    [Fact]
    public void NatDiscovery_GetDefaultGateway_DoesNotThrow()
    {
        var gw = NatDiscovery.GetDefaultGateway();
        if (gw != null)
        {
            Assert.Equal(AddressFamily.InterNetwork, gw.AddressFamily);
        }
    }

    private sealed class FakeNatDevice : INatDevice
    {
        public NatProtocol Protocol => NatProtocol.Upnp;
        public IPAddress? ExternalIp { get; set; }
        public bool ShouldFail { get; set; }
        public int LastAddedExternalPort { get; private set; }
        public int LastDeletedExternalPort { get; private set; }

        public Task<IPAddress?> GetExternalIpAsync(CancellationToken cancellationToken = default)
        {
            if (ShouldFail) return Task.FromResult<IPAddress?>(null);
            return Task.FromResult(ExternalIp);
        }

        public Task<bool> AddPortMappingAsync(
            int internalPort,
            int externalPort,
            IPAddress localAddress,
            string protocol = "TCP",
            string description = "Unciv",
            int leaseDurationSeconds = 3600,
            CancellationToken cancellationToken = default)
        {
            if (ShouldFail) return Task.FromResult(false);
            LastAddedExternalPort = externalPort;
            return Task.FromResult(true);
        }

        public Task<bool> DeletePortMappingAsync(
            int externalPort,
            string protocol = "TCP",
            CancellationToken cancellationToken = default)
        {
            if (ShouldFail) return Task.FromResult(false);
            LastDeletedExternalPort = externalPort;
            return Task.FromResult(true);
        }
    }
}
