using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Unciv.Net.Nat;

/// <summary>
/// NAT-PMP (RFC 6886) client for automatic port mapping (SERVER.md 6.5).
/// Communicates over UDP with the gateway router (default port 5351).
/// </summary>
public sealed class NatPmpDevice : INatDevice
{
    public const int DefaultPort = 5351;

    private readonly IPEndPoint gatewayEndPoint;
    private readonly TimeSpan requestTimeout;

    public NatPmpDevice(IPAddress gatewayAddress, int port = DefaultPort, TimeSpan? timeout = null)
    {
        gatewayEndPoint = new IPEndPoint(gatewayAddress, port);
        requestTimeout = timeout ?? TimeSpan.FromSeconds(3);
    }

    public NatProtocol Protocol => NatProtocol.NatPmp;

    public IPEndPoint GatewayEndPoint => gatewayEndPoint;

    public async Task<IPAddress?> GetExternalIpAsync(CancellationToken cancellationToken = default)
    {
        // 2-byte request: version = 0, opcode = 0
        var request = new byte[] { 0, 0 };

        var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (response == null || response.Length < 12) return null;

        // Verify version 0 and opcode 128
        if (response[0] != 0 || response[1] != 128) return null;

        var resultCode = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2, 2));
        if (resultCode != 0) return null; // Non-zero indicates failure

        var ipBytes = response.AsSpan(8, 4).ToArray();
        return new IPAddress(ipBytes);
    }

    public async Task<bool> AddPortMappingAsync(
        int internalPort,
        int externalPort,
        IPAddress localAddress,
        string protocol = "TCP",
        string description = "Unciv",
        int leaseDurationSeconds = 3600,
        CancellationToken cancellationToken = default)
    {
        return await SetPortMappingLifetimeAsync(
            internalPort,
            externalPort,
            (uint)leaseDurationSeconds,
            protocol,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeletePortMappingAsync(
        int externalPort,
        string protocol = "TCP",
        CancellationToken cancellationToken = default)
    {
        // NAT-PMP deletes mapping by specifying lifetime = 0
        return await SetPortMappingLifetimeAsync(
            internalPort: externalPort,
            externalPort: externalPort,
            lifetimeSeconds: 0,
            protocol: protocol,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SetPortMappingLifetimeAsync(
        int internalPort,
        int externalPort,
        uint lifetimeSeconds,
        string protocol,
        CancellationToken cancellationToken)
    {
        byte opcode = string.Equals(protocol, "UDP", StringComparison.OrdinalIgnoreCase) ? (byte)1 : (byte)2;

        var request = new byte[12];
        request[0] = 0; // Version
        request[1] = opcode; // 1 = UDP, 2 = TCP
        request[2] = 0; // Reserved
        request[3] = 0; // Reserved
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), (ushort)internalPort);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6, 2), (ushort)externalPort);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8, 4), lifetimeSeconds);

        var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (response == null || response.Length < 16) return false;

        if (response[0] != 0 || response[1] != (byte)(128 + opcode)) return false;

        var resultCode = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2, 2));
        return resultCode == 0;
    }

    private async Task<byte[]?> SendRequestAsync(byte[] request, CancellationToken cancellationToken)
    {
        using var client = new UdpClient();
        client.Client.SendTimeout = (int)requestTimeout.TotalMilliseconds;
        client.Client.ReceiveTimeout = (int)requestTimeout.TotalMilliseconds;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(requestTimeout);

        try
        {
            await client.SendAsync(request, request.Length, gatewayEndPoint).ConfigureAwait(false);
            var result = await client.ReceiveAsync(cts.Token).ConfigureAwait(false);
            return result.Buffer;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
}
