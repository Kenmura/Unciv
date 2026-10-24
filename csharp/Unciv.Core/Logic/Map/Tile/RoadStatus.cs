namespace Unciv.Logic.Map.Tiles;

/// <summary>
/// Ported from <c>core/src/com/unciv/logic/map/tile/RoadStatus.kt</c>.
/// Note: Order is important; <c>ordinal</c> (0, 1, 2) is compared as "roadLevel".
/// </summary>
// TODO(T034): Add Improvement(Ruleset ruleset) => ruleset.TileImprovements.GetValueOrDefault(status.ToString());
public enum RoadStatus
{
    None = 0,
    Road = 1,
    Railroad = 2,
}

public static class RoadStatusExtensions
{
    public static int GetUpkeep(this RoadStatus status) => status switch
    {
        RoadStatus.None => 0,
        RoadStatus.Road => 1,
        RoadStatus.Railroad => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static float GetMovement(this RoadStatus status) => status switch
    {
        RoadStatus.None => 1f,
        RoadStatus.Road => 0.5f,
        RoadStatus.Railroad => 0.1f,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static float GetMovementImproved(this RoadStatus status) => status switch
    {
        RoadStatus.None => 1f,
        RoadStatus.Road => 1f / 3f,
        RoadStatus.Railroad => 0.1f,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string? GetRemoveAction(this RoadStatus status) => status switch
    {
        RoadStatus.None => null,
        RoadStatus.Road => "Remove Road",
        RoadStatus.Railroad => "Remove Railroad",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
