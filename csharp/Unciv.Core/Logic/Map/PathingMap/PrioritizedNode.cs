namespace Unciv.Logic.Map.PathingMap;

// Ported from core/src/com/unciv/logic/map/pathingmap/PrioritizedNode.kt (T093). Stripped: the purity annotations.

/// <summary>This crams all the information we need about prioritizing a node into a single Long, avoiding allocations.</summary>
public readonly struct PrioritizedNode
{
    public long Bits { get; }

    public PrioritizedNode(long bits) => Bits = bits;

    public PrioritizedNode(RouteNode node, FixedPointMovement underestimatedTotal)
        : this((node.Bits & ~RouteNode.UnderestimatedTotalHiMask) | ToUnderestimatedTotalBits(underestimatedTotal))
    {
        if (underestimatedTotal <= 0) throw new ArgumentException($"underestimatedTotal {underestimatedTotal} must be positive");
        if (underestimatedTotal > RouteNode.MaxUnderestimatedTotal)
            throw new ArgumentException($"underestimatedTotal {underestimatedTotal} exceeds max {RouteNode.MaxUnderestimatedTotal}");
    }

    public int TileIdx
    {
        get
        {
            if (!Initialized) throw new ArgumentException("Failed requirement.");
            return (int)((Bits >> RouteNode.TileIdxOffset) & RouteNode.TileIdxLoMask);
        }
    }

    public FixedPointMovement UnderestimatedTotal =>
        FixedPointMovement.FpmFromFixedPointBits((int)((Bits >> RouteNode.UnderestimatedTotalOffset) & RouteNode.UnderestimatedTotalLoMask));

    public bool Initialized => Bits > 0;

    public override string ToString() => $"PrioritizedNode[underestimatedTotal={UnderestimatedTotal} {new RouteNode(Bits)}]";

    private static long ToUnderestimatedTotalBits(FixedPointMovement priority) => (long)priority.Bits << RouteNode.UnderestimatedTotalOffset;
}
