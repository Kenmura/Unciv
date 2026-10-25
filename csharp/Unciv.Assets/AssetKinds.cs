namespace Unciv.Assets;

/// <summary>What kind of object a key's category yields from the procedural generator (and what a file override for it must be).</summary>
public enum AssetKind
{
    TerrainMaterial,
    Mesh,
    UnitBlock,
    Prop,
    Icon,
    Theme,
    StyleBox,
    Font,
}

public static class AssetKinds
{
    public static AssetKind Of(AssetKey key) => key.Category switch
    {
        RulesetAssetKeys.Terrain => AssetKind.TerrainMaterial,
        "mesh" => AssetKind.Mesh,
        RulesetAssetKeys.Unit or RulesetAssetKeys.UnitType => AssetKind.UnitBlock,
        RulesetAssetKeys.Feature or RulesetAssetKeys.Resource or RulesetAssetKeys.ResourceType or RulesetAssetKeys.Improvement or RulesetAssetKeys.Building => AssetKind.Prop,
        "font" => AssetKind.Font,
        RulesetAssetKeys.Ui when key.Name == "theme" => AssetKind.Theme,
        RulesetAssetKeys.Ui => AssetKind.StyleBox,
        _ => AssetKind.Icon,
    };
}
