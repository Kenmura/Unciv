using System.Net;

namespace Unciv.Relay;

/// <summary>PROXY protocol v1 and v2 headers (what nginx <c>proxy_protocol on</c> sends first): only the client address is taken from it.</summary>
public static class ProxyProtocol
{
    private static readonly byte[] V2Signature = [0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A];

    /// <summary>Reads one header; the client address, or null for a header that carries none (v1 UNKNOWN, v2 LOCAL). Throws <see cref="InvalidDataException"/> for anything else.</summary>
    public static async Task<IPAddress?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var head = new byte[6];
        await ReadExactAsync(stream, head, cancellationToken).ConfigureAwait(false);
        if (head.AsSpan().SequenceEqual("PROXY "u8)) return await ReadV1Async(stream, cancellationToken).ConfigureAwait(false);
        if (!head.AsSpan().SequenceEqual(V2Signature.AsSpan(0, 6))) throw new InvalidDataException("not a PROXY header");
        var rest = new byte[10]; // signature tail (6), version/command, family, length (2)
        await ReadExactAsync(stream, rest, cancellationToken).ConfigureAwait(false);
        if (!rest.AsSpan(0, 6).SequenceEqual(V2Signature.AsSpan(6, 6))) throw new InvalidDataException("bad PROXY v2 signature");
        var versionCommand = rest[6];
        var family = rest[7];
        var length = (rest[8] << 8) | rest[9];
        if (versionCommand >> 4 != 2 || length > 536) throw new InvalidDataException("bad PROXY v2 header");
        var payload = new byte[length];
        await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        if ((versionCommand & 0x0F) == 0) return null; // LOCAL: a health check of the proxy itself
        if ((versionCommand & 0x0F) != 1) throw new InvalidDataException("bad PROXY v2 command");
        return (family >> 4) switch
        {
            1 when length >= 12 => new IPAddress(payload.AsSpan(0, 4)),
            2 when length >= 36 => new IPAddress(payload.AsSpan(0, 16)),
            _ => null,
        };
    }

    private static async Task<IPAddress?> ReadV1Async(Stream stream, CancellationToken cancellationToken)
    {
        var line = new List<byte>(100);
        var one = new byte[1];
        while (true)
        {
            await ReadExactAsync(stream, one, cancellationToken).ConfigureAwait(false);
            if (one[0] == (byte)'\n') break;
            if (line.Count >= 107) throw new InvalidDataException("PROXY v1 header too long");
            line.Add(one[0]);
        }
        var fields = System.Text.Encoding.ASCII.GetString([.. line]).TrimEnd('\r').Split(' ');
        if (fields[0] == "UNKNOWN") return null;
        if (fields.Length != 5 || fields[0] is not ("TCP4" or "TCP6") || !IPAddress.TryParse(fields[1], out var address)) throw new InvalidDataException("bad PROXY v1 header");
        return address;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var done = 0;
        while (done < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(done), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new InvalidDataException("connection closed inside a PROXY header");
            done += read;
        }
    }
}
