namespace Unciv.Assets;

/// <summary>
/// One vertex of the border strip. <see cref="X"/>, <see cref="Y"/>, <see cref="Z"/> is the hex corner on the territory's edge (the true side of the tile). <see cref="InX"/>/<see cref="InZ"/> and
/// <see cref="OutX"/>/<see cref="OutZ"/> are the unit ground normals, pointing into the owned tile, of the side that arrives at the corner and of the side that leaves it (equal at the end of an open chain):
/// the vertex shader projects them with the live camera and offsets the vertex by <see cref="Across"/> (0 on the tile's edge, 1 on the band's inner edge) times the band width along their miter.
/// <see cref="Alpha"/> is the opacity of the side (remembered tiles are dimmer) and <see cref="Fade"/> the extra opacity factor of the corner (0 at an open end, 1 elsewhere).
/// </summary>
public readonly record struct BorderVertex(float X, float Y, float Z, float InX, float InZ, float OutX, float OutZ, float Across, float Alpha, float Fade);

/// <summary>Triangles of every border strip of one owner: four vertices per side (two corners times the band's two edges), so a side's own opacity never blends into its neighbour's.</summary>
public sealed record BorderMeshData(IReadOnlyList<BorderVertex> Vertices, IReadOnlyList<int> Indices);

/// <summary>T377: the engine-independent half of the border strips (see <see cref="BorderVertex"/>): vertices and triangles from <see cref="BorderChains"/>. The Godot side only hands them to the GPU.</summary>
public static class BorderMesh
{
    /// <summary>
    /// Builds the strips. <paramref name="heightOf"/> gives the height of a side's centre line (the renderer passes the rim height of the tiles on both sides plus a lift, so two civs' bands of one side are level),
    /// <paramref name="opacityOf"/> its opacity. Closed chains wrap; an open chain fades out over its first and its last side.
    /// </summary>
    public static BorderMeshData Build(IReadOnlyList<BorderChain> chains, Func<BorderEdge, float> heightOf, Func<BorderEdge, float> opacityOf)
    {
        var vertices = new List<BorderVertex>();
        var indices = new List<int>();
        foreach (var chain in chains)
        {
            var count = chain.Sides.Count;
            var normals = new (float X, float Z)[count];
            for (var i = 0; i < count; i++) normals[i] = InwardNormal(chain.Points[i], chain.Points[(i + 1) % chain.Points.Count]);
            for (var i = 0; i < count; i++)
            {
                var next = (i + 1) % count;
                var side = chain.Sides[i];
                var height = heightOf(side);
                var alpha = opacityOf(side);
                var startIn = i > 0 || chain.Closed ? normals[(i + count - 1) % count] : normals[i];
                var endOut = i < count - 1 || chain.Closed ? normals[next] : normals[i];
                var startFade = !chain.Closed && i == 0 ? 0f : 1f;
                var endFade = !chain.Closed && i == count - 1 ? 0f : 1f;
                var from = chain.Points[i];
                var to = chain.Points[chain.Closed ? next : i + 1];
                var at = vertices.Count;
                foreach (var across in new[] { 0f, 1f })
                    vertices.Add(new BorderVertex(from.X, height, from.Z, startIn.X, startIn.Z, normals[i].X, normals[i].Z, across, alpha, startFade));
                foreach (var across in new[] { 0f, 1f })
                    vertices.Add(new BorderVertex(to.X, height, to.Z, normals[i].X, normals[i].Z, endOut.X, endOut.Z, across, alpha, endFade));
                indices.AddRange([at, at + 1, at + 3, at, at + 3, at + 2]);
            }
        }
        return new BorderMeshData(vertices, indices);
    }

    /// <summary>
    /// The unit normal, on the ground, of the side from <paramref name="from"/> to <paramref name="to"/> pointing into the owned tile: the owned tile lies on the left of the engine's direction of travel,
    /// and world z is minus the engine's y, so in world (x, z) the left of travel (a, b) is (b, -a).
    /// </summary>
    internal static (float X, float Z) InwardNormal(BorderPoint from, BorderPoint to)
    {
        var a = to.X - from.X;
        var b = to.Z - from.Z;
        var length = MathF.Sqrt(a * a + b * b);
        return length < 1e-9f ? (0f, 0f) : (b / length, -a / length);
    }
}
