using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Tile;

// core/src/com/unciv/models/ruleset/tile/TileResource.kt
// Not ported yet: getImprovingImprovement (Civilization/Tile, T046/T047).
public class TileResource : RulesetStatsObject, IGameResource, IRulesetResourceParameterView, ICivilopediaText
{
    public override string MakeLink() => $"Resource/{Name}";

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context) =>
        ResourceDescriptions.GetCivilopediaTextLines(this, context);

    public int GetSortGroup(Ruleset ruleset) => (int)ResourceType;

    public string? GetSubCategory(Ruleset ruleset) => ResourceType switch
    {
        ResourceType.Luxury => "Luxury resource",
        ResourceType.Strategic => "Strategic resource",
        ResourceType.Bonus => "Bonus resource",
        _ => null,
    };

    private readonly KotlinLazy<bool> isCityWide;
    private readonly KotlinLazy<bool> isStockpiled;

    public TileResource()
    {
        var self = (IHasUniques)this;
        isCityWide = new KotlinLazy<bool>(() => self.HasUnique(UniqueType.CityResource, GameContext.IgnoreConditionalsState));
        isStockpiled = new KotlinLazy<bool>(() => self.HasUnique(UniqueType.Stockpiled, GameContext.IgnoreConditionalsState));
    }

    public ResourceType ResourceType { get; set; } = ResourceType.Bonus;
    public List<string> TerrainsCanBeFoundOn { get; set; } = [];

    /// <summary>
    /// Stats that this resource adds to a tile when a correct improvement has been constructed.
    /// The Stats component of this resource on the other hand is always in effect.
    /// </summary>
    public Stats? ImprovementStats { get; set; }

    /// <summary>Name of the Technology needed to see and use this resource</summary>
    public string? RevealedBy { get; set; }

    /// <summary>Legacy "which improvement will unlock this resource"</summary>
    public string? Improvement { get; set; }

    /// <summary>Defines which improvement will "unlock" this resource</summary>
    public List<string> ImprovedBy { get; set; } = [];

    public DepositAmount MajorDepositAmount { get; set; } = new();
    public DepositAmount MinorDepositAmount { get; set; } = new();

    [JsonIgnore]
    public bool IsCityWide => isCityWide.Value;

    [JsonIgnore]
    public bool IsStockpiled => isStockpiled.Value;

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Resource;

    private LinkedHashSet<string>? allImprovements;
    private Ruleset? ruleset;

    /// <summary>
    /// Collects which improvements "unlock" this resource, caches and returns the Set.
    /// The cache is cleared after ruleset load and combine, via <see cref="SetTransients"/>.
    /// </summary>
    public IReadOnlySet<string> GetImprovements()
    {
        if (allImprovements != null) return allImprovements;

        var rulesetForCache = ruleset
            ?? throw new InvalidOperationException("No ruleset on TileResource when initializing improvements");

        var allImprovementsLocal = new LinkedHashSet<string>();

        if (Improvement != null) allImprovementsLocal.Add(Improvement);
        allImprovementsLocal.AddAll(ImprovedBy);
        foreach (var improvement in rulesetForCache.TileImprovements.Values)
        {
            // Explicitly stated by the improvement, or this is a replacement improvement
            if (((IHasUniques)improvement).GetMatchingUniques(UniqueType.ImprovesResources).Any(it => MatchesFilter(it.Params[0]))
                || (improvement.Replaces != null && allImprovementsLocal.Contains(improvement.Replaces)))
            {
                allImprovementsLocal.Add(improvement.Name);
            }
        }

        allImprovements = allImprovementsLocal;
        return allImprovementsLocal;
    }

    /// <summary>
    /// Clears the cache for <see cref="GetImprovements"/> and saves the Ruleset the cache update will need.
    /// Doesn't evaluate the cache immediately, that would trip the uniqueObjects lazy before tests (and mods loading) are done manipulating uniques.
    /// </summary>
    public void SetTransients(Ruleset ruleset)
    {
        allImprovements = null;
        this.ruleset = ruleset;
    }

    public bool IsImprovedBy(string improvementName) => GetImprovements().Contains(improvementName);

    public bool IsImprovedBy(TileImprovement improvement) => GetImprovements().Contains(improvement.Name);

    /// <returns>Of all the potential improvements in <see cref="GetImprovements"/>, the first this civ can actually build, if any.</returns>
    public TileImprovement? GetImprovingImprovement(Unciv.Logic.Map.Tiles.Tile tile, GameContext gameContext)
    {
        if (gameContext.CivInfo == null) return null;
        var civ = (Unciv.Logic.Civilization.Civilization)gameContext.CivInfo;
        return GetImprovements()
            .Select(it => civ.GameInfo.Ruleset.TileImprovements[it])
            .FirstOrDefault(it => tile.ImprovementFunctions.CanBuildImprovement(it, gameContext));
    }

    public bool MatchesFilter(string filter, GameContext? state = null)
    {
        return MultiFilter.Filter(filter, (resource: this, state), static (it, s) =>
            s.resource.MatchesSingleFilter(it)
            || (s.state != null && ((IHasUniques)s.resource).HasTagUnique(it, s.state))
            || (s.state == null && ((IHasUniques)s.resource).HasTagUnique(it)));
    }

    public bool MatchesSingleFilter(string filter)
    {
        if (filter == "any") return true;
        if (Constants.All.Contains(filter)) return true;
        if (filter == Name) return true;
        if (filter == ResourceType.ToString()) return true;
        return ImprovementStats?.Any(it => filter == it.Key.ToString()) == true;
    }

    public bool GeneratesNaturallyOn(Unciv.Logic.Map.Tiles.Tile tile)
    {
        if (!TerrainsCanBeFoundOn.Contains(tile.LastTerrain.Name)) return false;
        var gameContext = new GameContext(tile: tile);
        var self = (IHasUniques)this;
        if (self.HasUnique(UniqueType.NoNaturalGeneration, gameContext)) return false;
        if (tile.AllTerrains.Any(it => ((IHasUniques)it).HasUnique(UniqueType.BlocksResources, gameContext))) return false;

        var smallerLandmassUniques = self.GetMatchingUniques(UniqueType.NaturalWonderSmallerLandmass, gameContext).ToList();
        var largerLandmassUniques = self.GetMatchingUniques(UniqueType.NaturalWonderLargerLandmass, gameContext).ToList();

        if (smallerLandmassUniques.Count > 0 || largerLandmassUniques.Count > 0)
        {
            var sortedContinents = tile.TileMap.ContinentsSortedBySize;

            foreach (var unique in smallerLandmassUniques)
            {
                if (sortedContinents.Take(ParseInt(unique.Params[0])).Contains(tile.GetContinent())) return false;
            }
            foreach (var unique in largerLandmassUniques)
            {
                if (!sortedContinents.Take(ParseInt(unique.Params[0])).Contains(tile.GetContinent())) return false;
            }
        }
        if (tile.Temperature != null && tile.Humidity != null) // Only works when in map generation
        {
            foreach (var unique in self.GetMatchingUniques(UniqueType.TileGenerationConditions, gameContext))
            {
                var temperature = tile.Temperature.Value;
                var humidity = tile.Humidity.Value;
                if (!(temperature >= ParseDouble(unique.Params[0]) && temperature <= ParseDouble(unique.Params[1]))) return false;
                if (!(humidity >= ParseDouble(unique.Params[2]) && humidity <= ParseDouble(unique.Params[3]))) return false;
            }
        }
        return true;
    }

    private static int ParseInt(string text) => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    private static double ParseDouble(string text) => double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    public class DepositAmount
    {
        public int Sparse { get; set; } = 1;
        public int Default { get; set; } = 2;
        public int Abundant { get; set; } = 3;
    }
}
