using Godot;
using Unciv.Net.Visibility;

namespace Unciv.GodotClient.Play;

/// <summary>One unit block on the map; remembers the snapshot row it was drawn from so tests and panels ask the view, not the engine.</summary>
public partial class UnitNode : Node3D
{
    public UnitDto Unit { get; set; } = null!; // JUSTIFY: C010 set by GameScreen right after construction

    public bool IsOwn => Unit.Own != null;
}
