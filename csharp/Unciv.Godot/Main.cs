using Godot;

namespace Unciv.GodotClient;

/// <summary>Empty root scene of the Godot client. Later client tickets add the UI beneath it.</summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        // Touches Unciv.Core so the project reference is exercised by the headless build.
        GD.Print($"Unciv Godot client started (default language {Constants.English})");
    }
}
