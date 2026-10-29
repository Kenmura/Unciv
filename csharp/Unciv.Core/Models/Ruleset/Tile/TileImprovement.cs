using Unciv.Logic;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.UI.Components.Extensions;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Tile;

// core/src/com/unciv/models/ruleset/tile/TileImprovement.kt
public class TileImprovement : RulesetStatsObject, ICivilopediaText
{
    public override string MakeLink() => $"Improvement/{Name}";

    public int GetTurnsToBuild(Unciv.Logic.Civilization.Civilization civInfo, MapUnit unit)
    {
        var state = new GameContext(civInfo, unit: unit);

        var buildSpeedUniques = unit.GetMatchingUniques(UniqueType.SpecificImprovementTime, state, checkCivInfoUniques: true)
            .Where(it => MatchesFilter(it.Params[1], state)).ToList();
        var buildSpeedIncreases = unit.GetMatchingUniques(UniqueType.ImprovementTimeIncrease, state, checkCivInfoUniques: true)
            .Where(it => MatchesFilter(it.Params[0], state));
        var increase = ((float)buildSpeedIncreases.Sum(it => double.Parse(it.Params[1], System.Globalization.CultureInfo.InvariantCulture))).ToPercent();
        var buildTime = increase == 0f ? 0f
            : civInfo.GameInfo.Speed.ImprovementBuildLengthModifier * TurnsToBuild / increase;

        var calculated = buildSpeedUniques.Aggregate(buildTime, (calculatedTurnsToBuild, unique) =>
            calculatedTurnsToBuild * unique.Params[0].ToPercent());
        // In some weird cases it was possible for something to take 0 turns, leading to it instead never finishing
        return Math.Max(KotlinMath.RoundToInt(calculated), 1);
    }

    public string GetDescription(Ruleset ruleset) => ImprovementDescriptions.GetDescription(this, ruleset);
    public IEnumerable<FormattedLine> GetShortDescription(CivilopediaContext? context = null) =>
        ImprovementDescriptions.GetShortDescription(this, context);
    public IEnumerable<FormattedLine> GetShortDecription(CivilopediaContext? context = null) =>
        GetShortDescription(context);

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context) =>
        ImprovementDescriptions.GetCivilopediaTextLines(this, context);

    public int GetSortGroup(Ruleset ruleset) =>
        IsGreatImprovement() ? 1 :
        Name.StartsWith("Cancel ", StringComparison.Ordinal) || Name.StartsWith("Remove ", StringComparison.Ordinal) || Name == "Repair" ? 2 :
        0;

    public string? GetSubCategory(Ruleset ruleset) =>
        IsGreatImprovement() ? "Great Improvement" :
        Name.StartsWith("Cancel ", StringComparison.Ordinal) || Name.StartsWith("Remove ", StringComparison.Ordinal) || Name == "Repair" ? "Action" :
        "Tile Improvements";

    public List<BaseUnit> GetConstructorUnits(Ruleset ruleset)
    {
        if (((IHasUniques)this).HasUnique(UniqueType.Unbuildable)) return [];

        var canOnlyFilters = ((IHasUniques)this).GetMatchingUniques(UniqueType.CanOnlyBeBuiltOnTile)
            .Select(it => it.Params[0] == "Coastal" ? "Land" : it.Params[0]).ToHashSet();

        var cannotFilters = ((IHasUniques)this).GetMatchingUniques(UniqueType.CannotBuildOnTile)
            .Select(it => it.Params[0]).ToHashSet();

        var resourcesImprovedByThis = ruleset.TileResources.Values.Where(it => it.IsImprovedBy(Name)).ToList();

        var expandedTerrainsCanBeBuiltOn = new HashSet<string>();
        foreach (var t in TerrainsCanBeBuiltOn)
            expandedTerrainsCanBeBuiltOn.Add(t);
        foreach (var t in TerrainsCanBeBuiltOn)
        {
            if (ruleset.Terrains.TryGetValue(t, out var terrain))
                foreach (var occ in terrain.OccursOn)
                    expandedTerrainsCanBeBuiltOn.Add(occ);
        }
        if (((IHasUniques)this).HasUnique(UniqueType.CanOnlyImproveResource))
        {
            foreach (var res in resourcesImprovedByThis)
                foreach (var found in res.TerrainsCanBeFoundOn)
                    expandedTerrainsCanBeBuiltOn.Add(found);
        }
        if (Name.StartsWith(Constants.Remove, StringComparison.Ordinal))
        {
            var stripped = Name[Constants.Remove.Length..];
            expandedTerrainsCanBeBuiltOn.Add(stripped);
            if (ruleset.Terrains.TryGetValue(stripped, out var tr))
                foreach (var occ in tr.OccursOn)
                    expandedTerrainsCanBeBuiltOn.Add(occ);
            if (ruleset.TileImprovements.TryGetValue(stripped, out var ti))
                foreach (var tb in ti.TerrainsCanBeBuiltOn)
                    expandedTerrainsCanBeBuiltOn.Add(tb);
        }
        expandedTerrainsCanBeBuiltOn.ExceptWith(cannotFilters);

        var terrainsCanBeBuiltOnTypes = new HashSet<TerrainType>();
        foreach (var name in expandedTerrainsCanBeBuiltOn)
        {
            if (ruleset.Terrains.TryGetValue(name, out var t))
                terrainsCanBeBuiltOnTypes.Add(t.Type);
        }
        foreach (var entry in Enum.GetValues<TerrainType>())
        {
            if (expandedTerrainsCanBeBuiltOn.Contains(entry.ToString()))
                terrainsCanBeBuiltOnTypes.Add(entry);
        }
        terrainsCanBeBuiltOnTypes.RemoveWhere(it => cannotFilters.Contains(it.ToString()));

        if (canOnlyFilters.Count != 0 && !canOnlyFilters.Overlaps(expandedTerrainsCanBeBuiltOn))
        {
            expandedTerrainsCanBeBuiltOn.Clear();
            if (!terrainsCanBeBuiltOnTypes.Any(it => canOnlyFilters.Contains(it.ToString())))
                terrainsCanBeBuiltOnTypes.Clear();
        }

        bool MatchesBuildImprovementsFilter(string filter) =>
            MatchesFilter(filter) ||
            expandedTerrainsCanBeBuiltOn.Contains(filter) ||
            terrainsCanBeBuiltOnTypes.Any(it => it.ToString() == filter);

        return ruleset.Units.Values
            .Where(unit =>
                (TurnsToBuild != -1 && ((IHasUniques)unit).GetMatchingUniques(UniqueType.BuildImprovements, GameContext.IgnoreConditionalsState)
                    .Any(it => MatchesBuildImprovementsFilter(it.Params[0])))
                || (((IHasUniques)unit).HasUnique(UniqueType.CreateWaterImprovements)
                    && terrainsCanBeBuiltOnTypes.Contains(TerrainType.Water))
            ).ToList();
    }

    public List<BaseUnit> GetCreatingUnits(Ruleset ruleset) =>
        ruleset.Units.Values
            .Where(unit => ((IHasUniques)unit).GetMatchingUniques(UniqueType.ConstructImprovementInstantly, GameContext.IgnoreConditionalsState)
                .Any(it => it.Params[0] == Name))
            .ToList();

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
            return MultiFilter.Filter(filter, (improvement: this, tileState), static (it, s) =>
                s.improvement.MatchesSingleFilter(it)
                || (s.tileState != null && ((IHasUniques)s.improvement).HasTagUnique(it, s.tileState))
                || (s.tileState == null && ((IHasUniques)s.improvement).HasTagUnique(it)));
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
                amount = KotlinMath.FloatToInt(amount * gameContext.GameInfo.NotNull().Speed.Modifier);
            counter.Add(unique.Params[1], amount);
        });
        return counter;
    }
}
