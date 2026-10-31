namespace Unciv.Assets;

/// <summary>A corner of a hexagon on the ground plane: world x and world z (minus the engine's y), as <see cref="MapOverlays.EdgeEnds"/> answers them.</summary>
public readonly record struct BorderPoint(float X, float Z);

/// <summary>
/// One connected piece of a territory's outline: <see cref="Points"/> are the hex corners in order, <see cref="Sides"/> the border sides between them (side i runs from point i to point i + 1; for a
/// <see cref="Closed"/> chain the last side runs back to point 0 and there is one point per side, for an open chain there is one more point than sides). The owned tile lies on the left of the
/// direction of travel in the engine's coordinates (y up), so a loop around a territory runs counter-clockwise there. An open chain ends where the outline runs into a tile whose owner is unknown
/// (fogged and never seen) or into a pinch with nothing left to continue.
/// </summary>
public sealed record BorderChain(string Owner, IReadOnlyList<BorderPoint> Points, IReadOnlyList<BorderEdge> Sides, bool Closed);

/// <summary>
/// T377: turns the unordered border sides of <see cref="MapOverlays.BorderEdges"/> into chains of connected sides, so the renderer can join them at the hex corners (a miter join needs the two sides
/// that meet in a corner). Pure and deterministic: the result depends only on the order of the sides, never on hashing.
/// </summary>
public static class BorderChains
{
    private const float Resolution = 1024f;

    private sealed record Side(BorderEdge Edge, BorderPoint A, BorderPoint B, (long, long) From, (long, long) To);

    /// <summary>
    /// Every side appears in exactly one chain. Where two sides of one owner leave the same corner (two owned tiles touching in a single corner) the continuation turning furthest to the left is taken
    /// (a loop may then pass the corner twice). Chains are ordered open first, then closed, each in the order of their first side in <paramref name="edges"/>; owners in order of first appearance.
    /// </summary>
    public static IReadOnlyList<BorderChain> Build(IReadOnlyList<BorderEdge> edges)
    {
        var open = new List<BorderChain>();
        var closed = new List<BorderChain>();
        var known = new HashSet<(long, long)>();
        foreach (var group in edges.GroupBy(it => it.Owner, StringComparer.Ordinal))
        {
            var sides = group.Select(it => MakeSide(it, known)).ToList();
            var outgoing = new Dictionary<(long, long), List<int>>();
            var arrivals = new HashSet<(long, long)>();
            for (var i = 0; i < sides.Count; i++)
            {
                if (!outgoing.TryGetValue(sides[i].From, out var list)) outgoing[sides[i].From] = list = [];
                list.Add(i);
                arrivals.Add(sides[i].To);
            }
            var used = new bool[sides.Count];
            for (var first = 0; first < sides.Count; first++)
                if (!used[first] && !arrivals.Contains(sides[first].From)) open.Add(Follow(group.Key, first, sides, outgoing, used)); // nothing arrives at its start: an open chain begins here
            for (var first = 0; first < sides.Count; first++)
                if (!used[first]) closed.Add(Follow(group.Key, first, sides, outgoing, used));
        }
        return [.. open.Concat(closed)];
    }

    private static Side MakeSide(BorderEdge edge, HashSet<(long, long)> known)
    {
        var (a, b) = MapOverlays.EdgeEnds(edge.X, edge.Y, edge.Dx, edge.Dy);
        return new Side(edge, new BorderPoint(a.X, a.Z), new BorderPoint(b.X, b.Z), Key(a, known), Key(b, known));
    }

    private static BorderChain Follow(string owner, int first, List<Side> sides, Dictionary<(long, long), List<int>> outgoing, bool[] used)
    {
        var points = new List<BorderPoint> { sides[first].A };
        var chainSides = new List<BorderEdge>();
        var current = first;
        while (true)
        {
            used[current] = true;
            chainSides.Add(sides[current].Edge);
            var next = Continue(sides, current, outgoing, used, first);
            if (next == first) return new BorderChain(owner, points, chainSides, true);
            points.Add(sides[current].B);
            if (next < 0) return new BorderChain(owner, points, chainSides, false);
            current = next;
        }
    }

    /// <summary>The side to continue with after <paramref name="current"/>: leaves the corner it arrives at, is unused (or is the chain's own first side, which closes the loop), turns furthest left.</summary>
    private static int Continue(List<Side> sides, int current, Dictionary<(long, long), List<int>> outgoing, bool[] used, int first)
    {
        if (!outgoing.TryGetValue(sides[current].To, out var candidates)) return -1;
        var best = -1;
        var bestTurn = float.NegativeInfinity;
        var dx = sides[current].B.X - sides[current].A.X;
        var dz = sides[current].B.Z - sides[current].A.Z;
        foreach (var candidate in candidates)
        {
            if (used[candidate] && candidate != first) continue;
            var ex = sides[candidate].B.X - sides[candidate].A.X;
            var ez = sides[candidate].B.Z - sides[candidate].A.Z;
            var turn = MathF.Atan2(dx * ez - dz * ex, dx * ex + dz * ez);
            if (turn > bestTurn) { bestTurn = turn; best = candidate; }
        }
        return best;
    }

    /// <summary>
    /// The same corner reaches here from up to three tiles through different float arithmetic and can land on either side of a rounding boundary: a key that has a known neighbour cell is that corner.
    /// Distinct corners are at least half a world unit apart, far more than a cell.
    /// </summary>
    private static (long, long) Key((float X, float Z) point, HashSet<(long, long)> known)
    {
        var key = ((long)MathF.Round(point.X * Resolution, MidpointRounding.AwayFromZero), (long)MathF.Round(point.Z * Resolution, MidpointRounding.AwayFromZero));
        for (var dx = -1L; dx <= 1; dx++)
            for (var dz = -1L; dz <= 1; dz++)
                if (known.Contains((key.Item1 + dx, key.Item2 + dz))) return (key.Item1 + dx, key.Item2 + dz);
        known.Add(key);
        return key;
    }
}
