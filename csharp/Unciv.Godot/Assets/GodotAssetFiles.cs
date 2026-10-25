using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Assets;

/// <summary>Reads the client's asset files from the Godot resource filesystem (the only place a <c>res://</c> path is formed).</summary>
public sealed class GodotAssetFiles : IAssetFiles
{
    private const string Root = "res://";

    public bool Exists(string relativePath) => ResourceLoader.Exists(Root + relativePath) || Godot.FileAccess.FileExists(Root + relativePath);

    public string? ReadText(string relativePath) =>
        Godot.FileAccess.FileExists(Root + relativePath) ? Godot.FileAccess.GetFileAsString(Root + relativePath) : null;

    public static Resource? Load(string relativePath) => ResourceLoader.Load(Root + relativePath);
}
