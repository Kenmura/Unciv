using Unciv.Models.Metadata;

namespace Unciv.UI.Screens.VictoryScreen;

// core/src/com/unciv/ui/screens/victoryscreen/RankingType.kt
// Stripped: getImage (libGDX Image). The icon names it picked are listed in IconName for a future client.
public enum RankingType
{
    Score,
    Population,
    Growth,
    Production,
    Gold,
    Territory,
    Force,
    Happiness,
    Technologies,
    Culture,
    TilesExplored,
}

public static class RankingTypeExtensions
{
    public static string Label(this RankingType type) => type switch
    {
        RankingType.Growth => "Growth",
        RankingType.TilesExplored => "Tiles Explored",
        _ => type.ToString(),
    };

    public static char IdForSerialization(this RankingType type) => type switch
    {
        RankingType.Score => 'S',
        RankingType.Population => 'N',
        RankingType.Growth => 'C',
        RankingType.Production => 'P',
        RankingType.Gold => 'G',
        RankingType.Territory => 'T',
        RankingType.Force => 'F',
        RankingType.Happiness => 'H',
        RankingType.Technologies => 'W',
        RankingType.Culture => 'A',
        RankingType.TilesExplored => 'E',
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static bool IsVanilla(this RankingType type) => type != RankingType.TilesExplored;

    /// <summary>Icon image path, or null where Kotlin used <c>{ null }</c> (the icon comes from the translated label).</summary>
    public static string? IconName(this RankingType type) => type switch
    {
        RankingType.Score => "OtherIcons/Score",
        RankingType.Population => "StatIcons/Population",
        RankingType.Growth => "StatIcons/Food",
        RankingType.Territory => "OtherIcons/Hexagon",
        RankingType.Force => "OtherIcons/Shield",
        RankingType.Technologies => "StatIcons/Science",
        RankingType.TilesExplored => "UnitPromotionIcons/Scouting",
        _ => null,
    };
}

public static class RankingTypes
{
    public static RankingType? FromIdForSerialization(char c) =>
        Enum.GetValues<RankingType>().Cast<RankingType?>().FirstOrDefault(it => it!.Value.IdForSerialization() == c);

    public static IEnumerable<RankingType> FilteredEntries(GameParameters gameParameters) =>
        Enum.GetValues<RankingType>().Where(it => it.IsVanilla() || gameParameters.ShowAdditionalRankingTypes);
}
