using Unciv.Models.StatTypes;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Assets;

/// <summary>Category names and the key derivation from ruleset objects: the one place that decides what a ruleset name is called as an asset.</summary>
public static class RulesetAssetKeys
{
    public const string Terrain = "terrain";
    public const string Unit = "unit";
    public const string UnitType = "unitType";
    public const string Feature = "feature";
    public const string Resource = "resource";
    public const string ResourceType = "resourceType";
    public const string Improvement = "improvement";
    public const string Building = "building";
    public const string StatIcon = "icon/stat";
    public const string TechIcon = "icon/tech";
    public const string BuildingIcon = "icon/building";
    public const string UnitIcon = "icon/unit";
    public const string ResourceIcon = "icon/resource";
    public const string ImprovementIcon = "icon/improvement";
    public const string PolicyIcon = "icon/policy";
    public const string PromotionIcon = "icon/promotion";
    public const string StatusIcon = "icon/status";
    public const string Ui = "ui";

    public static readonly AssetKey HexMesh = new("mesh", "hex");
    public static readonly AssetKey UiTheme = new(Ui, "theme");
    public static readonly AssetKey UiPanel = new(Ui, "panel");
    public static readonly AssetKey UiButton = new(Ui, "button");
    public static readonly AssetKey DefaultFont = new("font", "default");

    /// <summary>Every category has a <c>_default</c> key so fallback chains always terminate in something that exists.</summary>
    public static readonly string[] Categories =
    [
        Terrain, Feature, Unit, UnitType, Resource, ResourceType, Improvement, Building,
        StatIcon, TechIcon, BuildingIcon, UnitIcon, ResourceIcon, ImprovementIcon, PolicyIcon, PromotionIcon, StatusIcon, Ui, "mesh", "font",
    ];

    /// <summary>Fallback table for a ruleset: units to their unit type, resources to their resource type.</summary>
    public static AssetFallbacks FallbacksFor(RulesetClass ruleset)
    {
        var fallbacks = new AssetFallbacks();
        foreach (var unit in ruleset.Units.Values)
        {
            if (unit.UnitType.Length > 0) fallbacks.Add(AssetKey.Of(Unit, unit.Name), AssetKey.Of(UnitType, unit.UnitType));
        }
        foreach (var resource in ruleset.TileResources.Values)
        {
            fallbacks.Add(AssetKey.Of(Resource, resource.Name), AssetKey.Of(ResourceType, resource.ResourceType.ToString()));
        }
        return fallbacks;
    }

    /// <summary>All keys the client may ask for given <paramref name="ruleset"/>, in ruleset order, defaults and fixed UI keys included.</summary>
    public static IReadOnlyList<AssetKey> KeysFor(RulesetClass ruleset)
    {
        var keys = new List<AssetKey> { HexMesh, VegetationGeometry.TuftMeshKey, UiTheme, UiPanel, UiButton, DefaultFont };
        foreach (var category in Categories) keys.Add(new AssetKey(category, AssetKey.DefaultName));
        keys.AddRange(ruleset.Terrains.Keys.Select(n => AssetKey.Of(Terrain, n)));
        keys.AddRange(ruleset.Terrains.Keys.Select(TerrainRelief.MeshKey));
        keys.AddRange(ruleset.Terrains.Values.Where(t => t.Type == Unciv.Models.Ruleset.Tile.TerrainType.TerrainFeature).Select(t => AssetKey.Of(Feature, t.Name)));
        keys.AddRange(ruleset.Units.Keys.Select(n => AssetKey.Of(Unit, n)));
        keys.AddRange(ruleset.UnitTypes.Keys.Select(n => AssetKey.Of(UnitType, n)));
        keys.AddRange(ruleset.TileResources.Keys.Select(n => AssetKey.Of(Resource, n)));
        keys.AddRange(Enum.GetNames<Unciv.Models.Ruleset.Tile.ResourceType>().Select(n => AssetKey.Of(ResourceType, n)));
        keys.AddRange(ruleset.TileImprovements.Keys.Select(n => AssetKey.Of(Improvement, n)));
        keys.AddRange(Enumerable.Range(2, CitySpecs.TierCount - 1).Select(CitySpecs.KeyForTier)); // C032: the larger city models
        keys.AddRange(ruleset.Buildings.Keys.Select(n => AssetKey.Of(Building, n)));
        keys.AddRange(Enum.GetNames<Stat>().Select(n => AssetKey.Of(StatIcon, n)));
        keys.AddRange(ruleset.Technologies.Keys.Select(n => AssetKey.Of(TechIcon, n)));
        keys.AddRange(ruleset.Buildings.Keys.Select(n => AssetKey.Of(BuildingIcon, n)));
        keys.AddRange(ruleset.Units.Keys.Select(n => AssetKey.Of(UnitIcon, n)));
        keys.AddRange(ruleset.TileResources.Keys.Select(n => AssetKey.Of(ResourceIcon, n)));
        keys.AddRange(ruleset.TileImprovements.Keys.Select(n => AssetKey.Of(ImprovementIcon, n)));
        keys.AddRange(ruleset.Policies.Keys.Select(n => AssetKey.Of(PolicyIcon, n)));
        keys.AddRange(ruleset.UnitPromotions.Keys.Select(n => AssetKey.Of(PromotionIcon, n)));
        keys.AddRange(Enum.GetValues<UnitStatusIcon>().Select(UnitVisuals.IconKey));
        return keys;
    }
}
