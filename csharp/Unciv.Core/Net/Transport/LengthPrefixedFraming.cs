using System.Buffers.Binary;
using System.Text;

namespace Unciv.Net.Transport;

/// <summary>
/// Length-prefixed framing over stream (SERVER.md 6.3).
/// Big-endian 4-byte prefix followed by UTF-8 bytes. The prefix's top bit marks "more chunks follow": a message
/// up to <see cref="ProtocolVersion.MaxMessageBytes"/> is sent as chunks of at most <see cref="ProtocolVersion.MaxFrameBytes"/>
/// and reassembled by the reader, so a large-map snapshot never throws. A message that fits in one chunk has the same
/// wire format as before (top bit clear), so old and new peers interoperate for ordinary traffic.
/// </summary>
public static class LengthPrefixedFraming
{
    private const int ContinuationBit = unchecked((int)0x80000000);

    public static async ValueTask WriteFrameAsync(Stream stream, string frame, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(frame);
        if (bytes.Length > ProtocolVersion.MaxMessageBytes)
            throw new InvalidOperationException($"Encoded message length {bytes.Length} exceeds ProtocolVersion.MaxMessageBytes ({ProtocolVersion.MaxMessageBytes})");

        var offset = 0;
        do
        {
            var count = Math.Min(ProtocolVersion.MaxFrameBytes, bytes.Length - offset);
            var more = offset + count < bytes.Length;
            var buffer = new byte[4 + count]; // header and payload in one write: one TLS record, and a frame is never interleaved by two writers
            BinaryPrimitives.WriteInt32BigEndian(buffer, more ? count | ContinuationBit : count);
            bytes.AsSpan(offset, count).CopyTo(buffer.AsSpan(4));
            await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            offset += count;
        }
        while (offset < bytes.Length);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <param name="stream"></param>

    /// <param name="cancellationToken"></param>    /// <param name="maxMessageBytes">Cap on the reassembled message. Peers that only ever send small messages (the host reading from unauthenticated clients) pass <see cref="ProtocolVersion.MaxFrameBytes"/> so a stranger cannot make them buffer a large snapshot-sized message.</param>
    public static async ValueTask<string?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default, int maxMessageBytes = ProtocolVersion.MaxMessageBytes)
    {
        MemoryStream? assembled = null;
        var header = new byte[4];
        while (true)
        {
            var totalRead = 0;
            while (totalRead < 4)
            {
                var read = await stream.ReadAsync(header.AsMemory(totalRead, 4 - totalRead), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    if (totalRead == 0 && assembled == null) return null; // Clean EOF between messages
                    throw new EndOfStreamException("Unexpected end of stream while reading frame length prefix");
                }
                totalRead += read;
            }

            var prefix = BinaryPrimitives.ReadInt32BigEndian(header);
            var more = (prefix & ContinuationBit) != 0;
            var length = prefix & ~ContinuationBit;
            if (length > ProtocolVersion.MaxFrameBytes
                || (assembled?.Length ?? 0) + length > maxMessageBytes)
                throw new InvalidOperationException($"Frame length {length} exceeds allowed maximum of {ProtocolVersion.MaxFrameBytes} bytes");

            var payload = new byte[length];
            var payloadRead = 0;
            while (payloadRead < length)
            {
                var read = await stream.ReadAsync(payload.AsMemory(payloadRead, length - payloadRead), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException("Unexpected end of stream while reading frame payload");
                payloadRead += read;
            }

            if (!more && assembled == null) return Encoding.UTF8.GetString(payload);
            assembled ??= new MemoryStream();
            assembled.Write(payload);
            if (!more) return Encoding.UTF8.GetString(assembled.GetBuffer(), 0, (int)assembled.Length);
        }
    }
}
