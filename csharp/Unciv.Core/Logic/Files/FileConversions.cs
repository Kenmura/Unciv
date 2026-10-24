using System.IO.Compression;
using System.Text;
using Unciv.Json;

namespace Unciv.Logic.Files;

// core/src/com/unciv/logic/files/FileConversions.kt
//
// Deviations:
// - The libGDX Json is UncivJson; readJson/writeJson take the target type as a generic argument.
// - Java's GZIPInputStream reads the header in its constructor, so Kotlin's readJson falls back to plain text only when the header
//   (or the first base64 group) is bad. Here the whole file is decoded eagerly, so a corrupt later part of a zipped file also falls back
//   to plain text (which then fails to parse as JSON, so the observable result is still an exception).
// - Gzip bytes are not guaranteed identical to the JVM's (deflater implementations differ); decoding either is.

public static class FileConversions
{
    /// <summary>gzip-compresses <paramref name="data"/> (UTF-8) and base64-encodes the result.</summary>
    public static string Zip(string data)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(data));
        }
        return Convert.ToBase64String(buffer.ToArray());
    }

    /// <summary>Inverse of <see cref="Zip"/>. Throws when <paramref name="data"/> is not base64 (whitespace is rejected, like Java's basic decoder) or not gzip.</summary>
    public static string Unzip(string data) => Encoding.UTF8.GetString(Unzipped(Encoding.UTF8.GetBytes(data)));

    private static byte[] Unzipped(byte[] base64Bytes)
    {
        foreach (var b in base64Bytes)
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                throw new FormatException("Illegal base64 character");
        var compressed = Convert.FromBase64String(Encoding.ASCII.GetString(base64Bytes));
        using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var result = new MemoryStream();
        gzip.CopyTo(result);
        return result.ToArray();
    }

    /// <summary>Plain base64 encoding, no gzip - e.g. for encoding a checksum digest as text.</summary>
    public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes);

    /// <summary>Reads <paramref name="file"/> as JSON of <typeparamref name="T"/>, transparently gunzip+base64-decoding it if that's how it was saved -
    /// otherwise (or if that fails), falls back to reading it as plain JSON text.</summary>
    public static T? ReadJson<T>(IUncivFileHandle file)
    {
        byte[] raw;
        using (var stream = file.Read())
        using (var copy = new MemoryStream())
        {
            stream.CopyTo(copy);
            raw = copy.ToArray();
        }
        string text;
        try
        {
            text = Encoding.UTF8.GetString(Unzipped(raw));
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            text = Encoding.UTF8.GetString(raw);
        }
        return UncivJson.Deserialize<T>(text);
    }

    /// <summary>Writes <paramref name="obj"/> as JSON to <paramref name="file"/>, gzip-compressing and base64-encoding it first if <paramref name="zip"/> is true.</summary>
    public static void WriteJson<T>(IUncivFileHandle file, T obj, bool zip)
    {
        var json = UncivJson.Serialize(obj);
        using var stream = file.Write(false);
        stream.Write(Encoding.UTF8.GetBytes(zip ? Zip(json) : json));
    }
}
