using Godot;
using Unciv.GodotClient.Assets;
using Unciv.Assets;

namespace Unciv.GodotClient.Map;

/// <summary>A translucent hex laid on the selected tile's top face.</summary>
public partial class TileHighlight : MeshInstance3D
{
    private const float Lift = 0.01f;
    private const float FaceFill = 0.96f;

    public void Setup(AssetRegistry registry)
    {
        Mesh = registry.Get<Mesh>(RulesetAssetKeys.HexMesh);
        MaterialOverride = MapMaterials.Highlight();
        CastShadow = ShadowCastingSetting.Off;
        Visible = false;
    }

    public void ShowOn(Vector3 tileTop)
    {
        // The prism is HexHeight tall with its base on y = 0: flatten it to a film resting on the face.
        Scale = new Vector3(FaceFill, 0.05f, FaceFill);
        Position = new Vector3(tileTop.X, tileTop.Y + Lift, tileTop.Z);
        Visible = true;
    }

    public void Clear() => Visible = false;
}
