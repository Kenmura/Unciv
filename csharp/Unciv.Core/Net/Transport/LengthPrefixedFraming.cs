using System.Buffers.Binary;
using System.Text;

namespace Unciv.Net.Transport;

/// <summary>
/// Length-prefixed framing over stream (SERVER.md 6.3).
/// Big-endian 4-byte prefix followed by UTF-8 bytes, bounded by <see cref="ProtocolVersion.MaxFrameBytes"/>.
/// </summary>
public static class LengthPrefixedFraming
{
    public static async ValueTask WriteFrameAsync(Stream stream, string frame, CancellationToken cancellationToken = default)
    {
        var byteCount = Encoding.UTF8.GetByteCount(frame);
        if (byteCount > ProtocolVersion.MaxFrameBytes)
            throw new InvalidOperationException($"Encoded frame length {byteCount} exceeds ProtocolVersion.MaxFrameBytes ({ProtocolVersion.MaxFrameBytes})");

        var buffer = new byte[4 + byteCount];
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(0, 4), byteCount);
        Encoding.UTF8.GetBytes(frame, 0, frame.Length, buffer, 4);

        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<string?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var lengthHeader = new byte[4];
        var totalRead = 0;
        while (totalRead < 4)
        {
            var read = await stream.ReadAsync(lengthHeader.AsMemory(totalRead, 4 - totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (totalRead == 0) return null; // Clean EOF
                throw new EndOfStreamException("Unexpected end of stream while reading frame length prefix");
            }
            totalRead += read;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthHeader);
        if (length is < 0 or > ProtocolVersion.MaxFrameBytes)
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

        return Encoding.UTF8.GetString(payload);
    }
}
