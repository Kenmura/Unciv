using System.IO.Compression;

namespace Unciv.Logic.Map;

/// <summary>
/// Minimal PNG codec standing in for libGDX <c>Pixmap</c>/<c>PixmapIO.PNG</c> (the engine has no image library).
/// Encodes RGBA8888; decodes non-interlaced PNG of any colour type and bit depth to 0xRRGGBB values (what
/// <c>(getPixel ushr 8) and 0xFFFFFF</c> yields). Adam7-interlaced files are rejected.
/// </summary>
internal static class OdMapPng
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // rgba: one packed 0xRRGGBBAA value per pixel, row-major.
    public static byte[] Encode(int width, int height, int[] rgba)
    {
        var raw = new byte[height * (1 + width * 4)];
        var o = 0;
        for (var y = 0; y < height; y++)
        {
            raw[o++] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                var p = rgba[y * width + x];
                raw[o++] = (byte)(p >>> 24);
                raw[o++] = (byte)(p >>> 16);
                raw[o++] = (byte)(p >>> 8);
                raw[o++] = (byte)p;
            }
        }

        var header = new byte[13];
        WriteInt(header, 0, width);
        WriteInt(header, 4, height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            z.Write(raw, 0, raw.Length);

        using var output = new MemoryStream();
        output.Write(Signature, 0, Signature.Length);
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <exception cref="InvalidDataException">for anything that is not a PNG this decoder understands</exception>
    public static (int Width, int Height, int[] Rgb) Decode(byte[] png)
    {
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature)) throw new InvalidDataException("Not a PNG");
        int width = 0, height = 0, depth = 0, colorType = -1, interlace = 0;
        byte[] palette = [];
        using var idat = new MemoryStream();
        var pos = 8;
        while (pos + 8 <= png.Length)
        {
            var length = ReadInt(png, pos);
            var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = pos + 8;
            if (length < 0 || data + length > png.Length) throw new InvalidDataException("Truncated PNG");
            switch (type)
            {
                case "IHDR":
                    if (length < 13) throw new InvalidDataException("Bad IHDR");
                    width = ReadInt(png, data);
                    height = ReadInt(png, data + 4);
                    depth = png[data + 8];
                    colorType = png[data + 9];
                    interlace = png[data + 12];
                    break;
                case "PLTE":
                    palette = png.AsSpan(data, length).ToArray();
                    break;
                case "IDAT":
                    idat.Write(png, data, length);
                    break;
            }
            if (type == "IEND") break;
            pos = data + length + 4;
        }

        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException("Bad PNG colour type") };
        if (width <= 0 || height <= 0) throw new InvalidDataException("Empty PNG");
        if (interlace != 0) throw new InvalidDataException("Interlaced PNG is not supported");
        if (depth is not (1 or 2 or 4 or 8 or 16)) throw new InvalidDataException("Bad PNG bit depth");

        var bitsPerPixel = channels * depth;
        var bpp = Math.Max(1, bitsPerPixel / 8);
        var rowBytes = (int)(((long)width * bitsPerPixel + 7) / 8);
        var raw = new byte[(long)height * (rowBytes + 1) > int.MaxValue ? throw new InvalidDataException("PNG too large") : height * (rowBytes + 1)];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            z.ReadExactly(raw);

        var rgb = new int[width * height];
        var prev = new byte[rowBytes];
        var cur = new byte[rowBytes];
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * (rowBytes + 1);
            var filter = raw[rowStart];
            Array.Copy(raw, rowStart + 1, cur, 0, rowBytes);
            Unfilter(filter, cur, prev, bpp);
            for (var x = 0; x < width; x++)
                rgb[y * width + x] = PixelRgb(cur, x, colorType, depth, palette);
            (prev, cur) = (cur, prev);
        }
        return (width, height, rgb);
    }

    private static void Unfilter(int filter, byte[] cur, byte[] prev, int bpp)
    {
        for (var i = 0; i < cur.Length; i++)
        {
            var a = i >= bpp ? cur[i - bpp] : 0;
            var b = prev[i];
            var c = i >= bpp ? prev[i - bpp] : 0;
            var add = filter switch
            {
                0 => 0,
                1 => a,
                2 => b,
                3 => (a + b) / 2,
                4 => Paeth(a, b, c),
                _ => throw new InvalidDataException("Bad PNG filter"),
            };
            cur[i] = (byte)(cur[i] + add);
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static int PixelRgb(byte[] row, int x, int colorType, int depth, byte[] palette)
    {
        switch (colorType)
        {
            case 2: // RGB
                {
                    var step = depth / 8;
                    var o = x * 3 * step;
                    return row[o] << 16 | row[o + step] << 8 | row[o + 2 * step];
                }
            case 6: // RGBA
                {
                    var step = depth / 8;
                    var o = x * 4 * step;
                    return row[o] << 16 | row[o + step] << 8 | row[o + 2 * step];
                }
            case 4: // gray + alpha
                {
                    var v = row[x * 2 * (depth / 8)];
                    return v << 16 | v << 8 | v;
                }
            default: // 0 gray, 3 palette: one sample of 1..16 bits
                {
                    int sample;
                    if (depth == 16) sample = row[x * 2];
                    else if (depth == 8) sample = row[x];
                    else
                    {
                        var bit = x * depth;
                        sample = (row[bit / 8] >> (8 - depth - bit % 8)) & ((1 << depth) - 1);
                    }
                    if (colorType == 3)
                    {
                        var o = sample * 3;
                        return o + 2 < palette.Length ? palette[o] << 16 | palette[o + 1] << 8 | palette[o + 2] : 0;
                    }
                    var v = depth >= 8 ? sample : sample * 255 / ((1 << depth) - 1);
                    return v << 16 | v << 8 | v;
                }
        }
    }

    private static int ReadInt(byte[] b, int o) => b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3];

    private static void WriteInt(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >>> 24);
        b[o + 1] = (byte)(v >>> 16);
        b[o + 2] = (byte)(v >>> 8);
        b[o + 3] = (byte)v;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        var lengthBytes = new byte[4];
        WriteInt(lengthBytes, 0, data.Length);
        output.Write(lengthBytes, 0, 4);
        output.Write(typeBytes, 0, 4);
        output.Write(data, 0, data.Length);
        var crc = Crc32(Crc32(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        var crcBytes = new byte[4];
        WriteInt(crcBytes, 0, unchecked((int)crc));
        output.Write(crcBytes, 0, 4);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(uint crc, byte[] data)
    {
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
