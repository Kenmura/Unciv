using Unciv.Logic;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Tile;

// core/src/com/unciv/models/ruleset/tile/TileImprovement.kt
// Not ported yet: getTurnsToBuild (Civilization/MapUnit, T046/T048), getDescription/getShortDecription and the Civilopedia members (Civilopedia text types, T034),
// getConstructorUnits/getCreatingUnits (Ruleset.units, T034).
public class TileImprovement : RulesetStatsObject
{
    public override string MakeLink() => $"Improvement/{Name}";

    public string? Replaces { get; set; }
    public List<string> TerrainsCanBeBuiltOn { get; set; } = [];
    public string? TechRequired { get; set; }
    public string? UniqueTo { get; set; }

    /// <summary>Kotlin <c>val</c> that json can still set.</summary>
    public char? ShortcutKey { get; set; }

    /// <summary>This is the base cost. A cost of 0 means created instead of buildable.</summary>
    public int TurnsToBuild { get; set; } = -1;

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Improvement;

    public IEnumerable<string> LegacyRequiredTechs() => TechRequired == null ? [] : [TechRequired];

    public bool IsGreatImprovement() => ((IHasUniques)this).HasUnique(UniqueType.GreatImprovement);

    public bool IsRoad() => Enum.GetValues<RoadStatus>().Any(it => it != RoadStatus.None && it.ToString() == Name);

    public bool IsAncientRuinsEquivalent(GameContext? state = null) =>
        ((IHasUniques)this).HasUnique(UniqueType.IsAncientRuinsEquivalent, state ?? GameContext.IgnoreConditionalsState);

    public bool IsBarbarianCampEquivalent(GameContext? state = null) =>
        Name == Constants.BarbarianEncampment // backward compatibility for mods
        || ((IHasUniques)this).HasUnique(UniqueType.IsBarbarianCampEquivalent, state ?? GameContext.IgnoreConditionalsState);

    public bool CanBeBuiltOn(string terrain) => TerrainsCanBeBuiltOn.Contains(terrain);

    public bool CanBeBuiltOn(Terrain terrain) => TerrainsCanBeBuiltOn.Any(it => terrain.MatchesFilter(it));

    /// <summary>
    /// Check: Is this improvement allowed on a given terrain feature?
    /// This not used for e.g. a lumbermill - it derives the right to be placed on forest from <see cref="TerrainsCanBeBuiltOn"/>.
    /// </summary>
    public bool IsAllowedOnFeature(Terrain terrain) => CanBeBuiltOn(terrain)
        || ((IHasUniques)this).GetMatchingUniques(UniqueType.NoFeatureRemovalNeeded).Any(it => terrain.MatchesFilter(it.Params[0]));

    /// <summary>Implements UniqueParameterType.ImprovementFilter</summary>
    public bool MatchesFilter(string filter, GameContext? tileState = null, bool multiFilter = true)
    {
        var self = (IHasUniques)this;
        if (multiFilter)
        {
            return MultiFilter.Filter(filter, it =>
                MatchesSingleFilter(it)
                || (tileState != null && self.HasTagUnique(it, tileState))
                || (tileState == null && self.HasTagUnique(it)));
        }
        return MatchesSingleFilter(filter)
            || (tileState != null && self.HasTagUnique(filter, tileState))
            || (tileState == null && self.HasTagUnique(filter));
    }

    private bool MatchesSingleFilter(string filter) => filter switch
    {
        "all" or "All" => true,
        "Improvement" => true, // For situations involving tileFilter
        Constants.AllRoad => IsRoad(),
        "Great Improvement" or "Great" => IsGreatImprovement(),
        _ => filter == Name || filter == Replaces, // 2 string equalities is better than hashmap lookup
    };

    public Counter<string> GetStockpiledResourceRequirements(GameContext gameContext)
    {
        var counter = new Counter<string>();
        ((IHasUniques)this).ForEachMatchingUnique(UniqueType.CostsResources, gameContext, unique =>
        {
            var amount = int.Parse(unique.Params[0], System.Globalization.CultureInfo.InvariantCulture);
            if (unique.IsModifiedByGameSpeed())
                amount = KotlinMath.FloatToInt(amount * gameContext.GameInfo!.Speed.Modifier);
            counter.Add(unique.Params[1], amount);
        });
        return counter;
    }
}
