namespace Unciv.Logic.Map;

/// <summary>
/// Ported from <c>core/src/com/unciv/logic/map/NeighborDirection.kt</c>.
/// Note: Enum ordinals match declaration order (0..5); clock positions (2, 4, 6, 8, 10, 12) are accessed via <see cref="NeighborDirectionExtensions.GetClockPosition"/>.
/// </summary>
public enum NeighborDirection
{
    TopRight,
    BottomRight,
    Bottom,
    BottomLeft,
    TopLeft,
    Top,
}

public static class NeighborDirectionExtensions
{
    public static IReadOnlyDictionary<int, NeighborDirection> ByClockPosition { get; } =
        new Dictionary<int, NeighborDirection>
        {
            [2] = NeighborDirection.TopRight,
            [4] = NeighborDirection.BottomRight,
            [6] = NeighborDirection.Bottom,
            [8] = NeighborDirection.BottomLeft,
            [10] = NeighborDirection.TopLeft,
            [12] = NeighborDirection.Top,
        };

    public static int GetClockPosition(this NeighborDirection direction) => direction switch
    {
        NeighborDirection.TopRight => 2,
        NeighborDirection.BottomRight => 4,
        NeighborDirection.Bottom => 6,
        NeighborDirection.BottomLeft => 8,
        NeighborDirection.TopLeft => 10,
        NeighborDirection.Top => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };
}
