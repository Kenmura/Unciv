namespace Unciv.Assets;

/// <summary>Plain triangle mesh: interleaved xyz positions and normals, uv pairs and a triangle index list (clockwise front faces, as Godot expects).</summary>
public sealed record MeshData(float[] Positions, float[] Normals, float[] Uvs, int[] Indices)
{
    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>Flat-topped hexagonal prism, the placeholder terrain tile. Corners sit at 0, 60, ... degrees in the XZ plane; the prism spans y in [0, height].</summary>
public static class HexGeometry
{
    private const float HalfSqrt3 = 0.8660254f;

    /// <summary>Unit-circle corner offsets (x, z) at 0, 60, 120, 180, 240, 300 degrees, written out so no trig library is involved.</summary>
    public static readonly (float X, float Z)[] Corners =
    [
        (1f, 0f), (0.5f, HalfSqrt3), (-0.5f, HalfSqrt3), (-1f, 0f), (-0.5f, -HalfSqrt3), (0.5f, -HalfSqrt3),
    ];

    /// <summary>Top face (a fan around the centre) plus six side walls; the underside is never seen and is omitted.</summary>
    public static MeshData Prism(float radius, float height)
    {
        if (!(radius > 0f) || !(height > 0f)) throw new ArgumentOutOfRangeException(nameof(radius), "radius and height must be positive");
        var pos = new List<float>();
        var nrm = new List<float>();
        var uv = new List<float>();
        var idx = new List<int>();

        void Vertex(float x, float y, float z, float nx, float ny, float nz, float u, float v)
        {
            pos.Add(x); pos.Add(y); pos.Add(z);
            nrm.Add(nx); nrm.Add(ny); nrm.Add(nz);
            uv.Add(u); uv.Add(v);
        }

        Vertex(0f, height, 0f, 0f, 1f, 0f, 0.5f, 0.5f);
        foreach (var (cx, cz) in Corners) Vertex(cx * radius, height, cz * radius, 0f, 1f, 0f, 0.5f + cx * 0.5f, 0.5f + cz * 0.5f);
        for (var i = 0; i < 6; i++)
        {
            idx.Add(0); idx.Add(1 + i); idx.Add(1 + (i + 1) % 6);
        }

        for (var i = 0; i < 6; i++)
        {
            var (ax, az) = Corners[i];
            var (bx, bz) = Corners[(i + 1) % 6];
            var nx = (ax + bx) / 2f;
            var nz = (az + bz) / 2f;
            var len = MathF.Sqrt(nx * nx + nz * nz);
            nx /= len; nz /= len;
            var first = pos.Count / 3;
            Vertex(ax * radius, height, az * radius, nx, 0f, nz, 0f, 0f);
            Vertex(bx * radius, height, bz * radius, nx, 0f, nz, 1f, 0f);
            Vertex(bx * radius, 0f, bz * radius, nx, 0f, nz, 1f, 1f);
            Vertex(ax * radius, 0f, az * radius, nx, 0f, nz, 0f, 1f);
            idx.Add(first); idx.Add(first + 2); idx.Add(first + 1);
            idx.Add(first); idx.Add(first + 3); idx.Add(first + 2);
        }
        return new MeshData([.. pos], [.. nrm], [.. uv], [.. idx]);
    }
}
