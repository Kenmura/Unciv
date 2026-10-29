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

    private readonly List<AnimationPlayer> players = [];
    private readonly List<UnitAnimator> animators = [];
    private readonly List<Node3D> figures = [];
    private readonly List<Vector3> formationSlots = [];

    /// <summary>C080: the animation player of each figure whose model has one (none for generated blocks), held in its idle pose until the presenter drives them.</summary>
    public IReadOnlyList<AnimationPlayer> Players => players;

    /// <summary>C081: the clip the first animated figure stands in (null for a unit without animated figures or one at rest before any cue).</summary>
    public string? CurrentClip => animators.Count == 0 ? null : animators[0].Clip;

    /// <summary>C081: seconds into <see cref="CurrentClip"/> the first animated figure stands.</summary>
    public float ClipPosition => animators.Count == 0 ? 0f : animators[0].Position;

    /// <summary>C081: the direction the figures face, as a yaw about Y (0 faces +Z, the glTF default).</summary>
    public float FacingYaw { get; private set; }

    /// <summary>C081: turns every figure to <paramref name="yaw"/>. Figures turn in place, so the formation keeps its layout and the health bar and marks stay put.</summary>
    public void Face(float yaw)
    {
        FacingYaw = yaw;
        if (Rigid != RigidKind.None) ShowRigid(RigidPose);
        else foreach (var figure in figures) figure.Rotation = new Vector3(0f, yaw, 0f);
    }

    /// <summary>C081: stands every figure that has <paramref name="clip"/> a fraction <paramref name="progress"/> through it; false when none has it (the presenter then poses procedurally).</summary>
    public bool ShowClip(string clip, float progress) => animators.Count(it => it.Show(clip, progress)) > 0;

    /// <summary>C081: like <see cref="ShowClip"/> for a looping clip, <paramref name="seconds"/> into it.</summary>
    public bool ShowClipLooped(string clip, float seconds) => animators.Count(it => it.ShowLooped(clip, seconds)) > 0;

    /// <summary>C081: back to the idle pose after a cue.</summary>
    public void RestClips()
    {
        foreach (var animator in animators) animator.Rest();
    }

    /// <summary>C083: how this unit moves when it has no clips (ships, siege, armour, aircraft); <see cref="RigidKind.None"/> for a unit whose figures animate with clips or that has no motion of its own.</summary>
    public RigidKind Rigid { get; private set; }

    /// <summary>C083: the rigid pose last applied (<see cref="RigidPose.Rest"/> at rest).</summary>
    public RigidPose RigidPose { get; private set; } = RigidPose.Rest;

    private readonly List<(Node3D Part, Vector3 Position)> turretParts = [];

    /// <summary>
    /// C083: poses every figure of a rigid unit: pushed back along its facing, lifted, pitched and rolled, with the turret parts (<c>Part1</c>, <c>Part2</c> of an armour model) turned about the vertical.
    /// Does nothing for a unit that is not rigid. Turning keeps the formation layout: the figure moves relative to its slot, never the slot.
    /// </summary>
    public void ShowRigid(RigidPose pose)
    {
        if (Rigid == RigidKind.None) return;
        RigidPose = pose;
        var forward = new Basis(Vector3.Up, FacingYaw) * Vector3.Back; // the figures face +Z at yaw 0
        for (var i = 0; i < figures.Count; i++)
        {
            figures[i].Position = formationSlots[i] - forward * pose.Recoil + new Vector3(0f, pose.Bob, 0f);
            figures[i].Basis = new Basis(Vector3.Up, FacingYaw) * new Basis(Vector3.Right, -pose.Pitch) * new Basis(Vector3.Back, pose.Roll) * Basis.FromScale(figures[i].Scale);
        }
        var turn = new Basis(Vector3.Up, pose.TurretYaw);
        foreach (var (part, rest) in turretParts)
        {
            part.Position = turn * rest;
            part.Rotation = new Vector3(0f, pose.TurretYaw, 0f);
        }
    }

    /// <summary>C083: back to the resting pose.</summary>
    public void RestRigid() => ShowRigid(RigidPose.Rest);

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
        Rigid = RigidMotion.KindOf(baseUnit?.UnitType);
        for (var i = 0; i < formation.Count; i++)
        {
            var model = assets.Get<Node3D>(key);
            if (civ is { } c) MapMaterials.ColourTeam(model, c.R, c.G, c.B);
            if (UnitModels.FindPlayer(model) is { } player)
            {
                var phase = UnitModels.IdlePhase(Unit.Id, i);
                UnitModels.HoldIdle(player, phase);
                players.Add(player);
                animators.Add(new UnitAnimator(player, phase));
            }
            if (i > 0) model.GetNodeOrNull("Glyphs")?.Free(); // one initials billboard for the whole group
            var figure = new Node3D { Name = "Figure" + i, Position = new Vector3(formation[i].X, 0f, formation[i].Z), Scale = Vector3.One * formation[i].Scale };
            figure.AddChild(model);
            AddChild(figure);
            figures.Add(figure);
            formationSlots.Add(figure.Position);
            if (Rigid == RigidKind.Armor && model.GetNodeOrNull<Node3D>("Part1") is { } turret && model.GetNodeOrNull<Node3D>("Part2") is { } barrel) turretParts.AddRange([(turret, turret.Position), (barrel, barrel.Position)]);
        }

        if (players.Count > 0) Rigid = RigidKind.None; // a model with clips animates itself
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
