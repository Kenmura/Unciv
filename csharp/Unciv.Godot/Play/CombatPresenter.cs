using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.Logic.Map;

namespace Unciv.GodotClient.Play;

/// <summary>
/// C033: plays the engine's movement and combat events on the map. <see cref="Present"/> turns a batch of events into a script (<see cref="CombatPresentation.Plan"/>) and queues it;
/// <see cref="Step"/> advances the script by explicit time and poses the unit nodes, a projectile, a flash and floating damage numbers procedurally. A unit that dies is taken out of the
/// screen's unit table when its batch arrives (the screen rebuilds its nodes from the snapshot, in which the unit is already gone) and freed by this node when its death cue ends.
/// Off by default (nothing queues, so screens that do not animate behave as before); real-time (frame deltas) when <see cref="Enabled"/>; with <see cref="ManualStepping"/> only
/// <see cref="Step"/> moves time, which is what the golden harness and the tests use. Never changes the game.
/// </summary>
public partial class CombatPresenter : Node3D
{
    private const float ProjectileLift = 0.5f;
    private const float NumberLift = 1.5f;
    private const float FlashLift = 0.7f;
    private const float ProjectileRadius = 0.09f;
    private const float FlashRadius = 0.4f;

    private readonly PresentationTimeline timeline = new();
    private readonly Dictionary<Cue, Node3D> visuals = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, UnitNode> dying = [];
    private readonly List<(int Amount, HexCoord At)> numbers = [];
    private readonly Dictionary<int, float> facings = [];
    private Func<int, UnitNode?> unitOf = _ => null;
    private Func<HexCoord, Vector3> tileTop = _ => Vector3.Zero;

    /// <summary>Animate at all. Off, <see cref="Present"/> does nothing.</summary>
    public bool Enabled { get; set; }

    /// <summary>Only <see cref="Step"/> advances time, never the frame clock (golden harness, tests).</summary>
    public bool ManualStepping { get; set; }

    /// <summary>The script has been played to the end and nothing is left on the map but the units.</summary>
    public bool IsIdle => timeline.IsIdle;

    /// <summary>The damage numbers shown so far, in order, with the tile they floated over (what tests compare with the engine's result).</summary>
    public IReadOnlyList<(int Amount, HexCoord At)> NumbersShown => numbers;

    /// <summary>Units taken out of the screen's table that are still dying.</summary>
    public int DyingCount => dying.Count;

    /// <summary>Transient nodes alive right now (projectile, flash, damage numbers).</summary>
    public int EffectCount => visuals.Count;

    /// <summary>Wires the presenter to the screen: how to find the node of a unit by id, and where a tile's top is in the world.</summary>
    public void Bind(Func<int, UnitNode?> unitLookup, Func<HexCoord, Vector3> tileTopOf)
    {
        unitOf = unitLookup;
        tileTop = tileTopOf;
    }

    /// <summary>
    /// Queues the events of one command for playing. Call before the screen rebuilds its unit nodes: nodes of units this batch destroys are moved out of <paramref name="units"/>
    /// (and into this presenter) so the rebuild does not free them. Returns whether anything was queued.
    /// </summary>
    public bool Present(IReadOnlyList<GameEvent> events, IDictionary<int, UnitNode> units)
    {
        if (!Enabled) return false;
        var script = CombatPresentation.Plan(events);
        if (script.Count == 0) return false;
        foreach (var cue in script.SelectMany(it => it).Where(it => it.Kind == CueKind.Death && it.UnitId is { }))
        {
            var id = cue.UnitId!.Value;
            if (!units.TryGetValue(id, out var node) || dying.ContainsKey(id)) continue;
            units.Remove(id);
            node.GetParent()?.RemoveChild(node);
            AddChild(node);
            dying[id] = node;
        }
        timeline.Enqueue(script);
        return true;
    }

    /// <summary>Poses nodes for the current moment without moving time. Call after the screen rebuilt its nodes, so units that are about to walk start at the start of their walk.</summary>
    public void Refresh()
    {
        if (!Enabled) return;
        foreach (var (id, yaw) in facings) unitOf(id)?.Face(yaw); // the screen rebuilt its nodes: units keep the way they last turned
        foreach (var cue in timeline.Upcoming.Reverse()) if (cue.Kind == CueKind.Move) Pose(cue, 0f, preview: true);
        Apply(timeline.Advance(0f));
    }

    /// <summary>Drops the script and every node it made, for a screen that switches to another game (unit ids of the old game mean nothing in the new one).</summary>
    public void Reset()
    {
        timeline.Clear();
        foreach (var effect in visuals.Values) effect.QueueFree();
        visuals.Clear();
        foreach (var ghost in dying.Values) ghost.QueueFree();
        dying.Clear();
        numbers.Clear();
        facings.Clear();
    }

    public override void _Process(double delta)
    {
        if (Enabled && !ManualStepping) Step((float)delta);
    }

    /// <summary>Moves the script on by <paramref name="seconds"/> and poses everything that ran.</summary>
    public void Step(float seconds) => Apply(timeline.Advance(seconds));

    private void Apply(IReadOnlyList<CueUpdate> updates)
    {
        foreach (var update in updates)
        {
            Pose(update.Cue, update.Progress);
            if (update.Done) Finish(update.Cue);
        }
    }

    /// <summary>Poses one cue. A <paramref name="preview"/> only puts a queued walker at the start of its path: it neither turns nor leaves its idle pose before its turn comes.</summary>
    private void Pose(Cue cue, float p, bool preview = false)
    {
        switch (cue.Kind)
        {
            case CueKind.Move when cue.UnitId is { } id && Find(id) is { } walker && cue.Path is { } path:
                {
                    var (leg, along) = CuePoses.PathLeg(path.Count, p);
                    walker.Position = tileTop(path[leg]).Lerp(tileTop(path[leg + 1]), along);
                    if (preview) break;
                    Turn(id, walker, tileTop(path[leg + 1]) - tileTop(path[leg]));
                    walker.ShowClipLooped(UnitModelContract.WalkClip, p * cue.Duration); // no walk clip: it just slides
                    break;
                }
            case CueKind.Strike when cue.UnitId is { } id && Find(id) is { } striker:
                Turn(id, striker, Toward(cue));
                striker.Position = striker.RestPosition + Toward(cue) * CuePoses.Lunge(p);
                striker.ShowClip(UnitModelContract.AttackClip, p); // no attack clip: the lunge alone
                break;
            case CueKind.Shot:
                {
                    // a unit's shot starts with its draw (attack_ranged); the projectile leaves at the release marker. A city's shot is the projectile alone.
                    var flight = cue.UnitId != null ? CuePoses.ShotFlight(p) : p;
                    if (cue.UnitId is { } archer && Find(archer) is { } drawing)
                    {
                        Turn(archer, drawing, Toward(cue));
                        drawing.ShowClip(UnitModelContract.RangedAttackClip, p);
                    }
                    if (flight <= 0f && !visuals.ContainsKey(cue)) break;
                    var ball = Effect(cue, () => Ball(ProjectileRadius, PlaceholderPalette.Projectile));
                    ball.Position = tileTop(cue.From).Lerp(tileTop(cue.To), flight) + new Vector3(0f, ProjectileLift + CuePoses.Arc(flight), 0f);
                    break;
                }
            case CueKind.Bombard:
                {
                    var flash = Effect(cue, () => Ball(FlashRadius, PlaceholderPalette.Flash));
                    flash.Position = tileTop(cue.From) + new Vector3(0f, FlashLift, 0f);
                    flash.Scale = Vector3.One * CuePoses.FlashScale(p);
                    if (flash is MeshInstance3D { MaterialOverride: StandardMaterial3D material }) material.AlbedoColor = material.AlbedoColor with { A = CuePoses.Opacity(p) };
                    break;
                }
            case CueKind.Hit:
                {
                    if (cue.UnitId is { } id && Find(id) is { } hurt && !hurt.ShowClip(UnitModelContract.HitClip, p)) hurt.Position = hurt.RestPosition + new Vector3(CuePoses.Shake(p), 0f, 0f); // no hit clip: shake
                    PoseNumber(cue, p);
                    break;
                }
            case CueKind.Death when cue.UnitId is { } id:
                {
                    if (Find(id) is not { } node) break;
                    if (node.ShowClip(UnitModelContract.DeathClip, p)) break; // a death clip does the falling; without one the unit shrinks and sinks
                    node.Scale = Vector3.One * CuePoses.DeathScale(p);
                    node.Position = node.RestPosition - new Vector3(0f, CuePoses.Sink(p), 0f);
                    break;
                }
        }
    }

    private void PoseNumber(Cue cue, float p)
    {
        if (cue.Amount <= 0) return;
        if (!visuals.TryGetValue(cue, out var label))
        {
            label = new Label3D
            {
                Name = "Damage_" + numbers.Count,
                Text = CombatPresentation.DamageText(cue.Amount),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FontSize = 96,
                PixelSize = 0.008f,
                OutlineSize = 16,
                Modulate = ProceduralAssets.ToColor(PlaceholderPalette.DamageNumber),
            };
            AddChild(label);
            visuals[cue] = label;
            numbers.Add((cue.Amount, cue.To));
        }
        label.Position = tileTop(cue.To) + new Vector3(0f, NumberLift + CuePoses.Rise(p), 0f);
        ((Label3D)label).Modulate = ((Label3D)label).Modulate with { A = CuePoses.Opacity(p) };
    }

    private void Finish(Cue cue)
    {
        switch (cue.Kind)
        {
            case CueKind.Move or CueKind.Strike or CueKind.Hit or CueKind.Shot when cue.UnitId is { } id && Find(id) is { } node:
                node.Position = node.RestPosition;
                node.RestClips();
                break;
        }
        if (cue.Kind == CueKind.Death && cue.UnitId is { } dead)
        {
            facings.Remove(dead);
            if (dying.Remove(dead, out var ghost)) ghost.QueueFree();
            else if (unitOf(dead) is { } live)
            {
                live.Scale = Vector3.One;
                live.RestClips();
            }
        }
        if (visuals.Remove(cue, out var effect)) effect.QueueFree();
    }

    /// <summary>Turns a unit toward a world-space direction and remembers it; no direction (a zero-length step) keeps the current way.</summary>
    private void Turn(int id, UnitNode node, Vector3 way)
    {
        if (CuePoses.FacingYaw(way.X, way.Z) is not { } yaw) return;
        facings[id] = yaw;
        node.Face(yaw);
    }

    private Vector3 Toward(Cue cue)
    {
        var way = tileTop(cue.To) - tileTop(cue.From);
        way.Y = 0f;
        return way;
    }

    private Node3D Effect(Cue cue, Func<Node3D> create)
    {
        if (visuals.TryGetValue(cue, out var existing)) return existing;
        var fresh = create();
        AddChild(fresh);
        visuals[cue] = fresh;
        return fresh;
    }

    private UnitNode? Find(int id) => dying.GetValueOrDefault(id) ?? unitOf(id);

    private static MeshInstance3D Ball(float radius, Rgb colour) => new()
    {
        Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = 12, Rings = 6 },
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = ProceduralAssets.ToColor(colour),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        },
    };
}
