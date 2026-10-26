using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.Models.Ruleset.Unit;
using Unciv.Net.Visibility;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.GodotClient.Play;

/// <summary>
/// One unit on the map (C004): a formation of figures (count from the unit's strength tier, see <see cref="UnitVisuals"/>), each the registry's model for the unit coloured with its civ, a health bar
/// when hurt, and status marks above. Remembers the snapshot row it was drawn from so tests and panels ask the view, not the engine.
/// </summary>
public partial class UnitNode : Node3D
{
    private const float BarHeight = 0.98f;
    private const float BarPixelSize = 0.011f;
    private const float IconHeight = 1.28f;
    private const float IconSpacing = 0.3f;
    private const float IconPixelSize = 0.0042f;

    public UnitDto Unit { get; set; } = null!; // JUSTIFY: C010 set by GameScreen right after construction

    /// <summary>C033: where the unit stands when no animation moves it; set by the screen when it places the node, and what the presenter returns the node to.</summary>
    public Vector3 RestPosition { get; private set; }

    /// <summary>Puts the node at its resting place (C033).</summary>
    public void Place(Vector3 position)
    {
        RestPosition = position;
        Position = position;
    }

    public bool IsOwn => Unit.Own != null;

    /// <summary>How many figures stand on the tile.</summary>
    public int FigureCount { get; private set; }

    /// <summary>The marks shown, in drawing order.</summary>
    public IReadOnlyList<UnitStatusIcon> StatusIcons { get; private set; } = [];

    /// <summary>The health bar as drawn, null while the unit is unhurt.</summary>
    public HealthBarSpec? HealthBar { get; private set; }

    /// <summary>Builds figures, health bar and status marks. <paramref name="baseUnit"/> is null for a unit the ruleset does not know (one figure); <paramref name="civ"/> is the owner's colour (0..1 channels) or null.</summary>
    public void Build(AssetRegistry assets, RulesetClass ruleset, BaseUnit? baseUnit, (float R, float G, float B)? civ)
    {
        FigureCount = baseUnit == null ? 1 : UnitVisuals.FigureCount(baseUnit, ruleset);
        var key = AssetKey.Of(RulesetAssetKeys.Unit, Unit.Name);
        var formation = UnitVisuals.Formation(FigureCount);
        for (var i = 0; i < formation.Count; i++)
        {
            var model = assets.Get<Node3D>(key);
            if (civ is { } c) MapMaterials.ColourBanner(model, c.R, c.G, c.B);
            if (i > 0) model.GetNodeOrNull("Glyphs")?.Free(); // one initials billboard for the whole group
            var figure = new Node3D { Name = "Figure" + i, Position = new Vector3(formation[i].X, 0f, formation[i].Z), Scale = Vector3.One * formation[i].Scale };
            figure.AddChild(model);
            AddChild(figure);
        }

        var bar = UnitVisuals.HealthBar(Unit);
        if (bar.Visible)
        {
            HealthBar = bar;
            AddChild(new Sprite3D
            {
                Name = "HealthBar",
                Texture = ProceduralAssets.ToTexture(ProceduralImages.HealthBar(bar.Fraction, bar.Fill)),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                PixelSize = BarPixelSize,
                NoDepthTest = true,
                Position = new Vector3(0f, BarHeight, 0f),
            });
        }

        StatusIcons = UnitVisuals.StatusIcons(Unit);
        for (var i = 0; i < StatusIcons.Count; i++)
        {
            AddChild(new Sprite3D
            {
                Name = "Status_" + StatusIcons[i],
                Texture = assets.Get<Texture2D>(UnitVisuals.IconKey(StatusIcons[i])),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                PixelSize = IconPixelSize,
                NoDepthTest = true,
                Position = new Vector3((i - (StatusIcons.Count - 1) / 2f) * IconSpacing, IconHeight, 0f),
            });
        }
    }
}
