using Godot;
using Unciv.Assets;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// C007 verification scene (scenario <c>asset-sheet</c>). Loads the Vanilla ruleset, asks the registry for every key the client can derive from it,
/// then lays out a labelled swatch sheet (every land/water terrain tile, one block per unit type, the stat and a few other icons, the theme's button and panel),
/// captures the viewport to <c>--out=DIR/asset-sheet.png</c> and exits 0 only when every key resolved, every procedural source was used (no stray override files),
/// and the frame is not blank. Arguments after <c>--</c>: <c>--out=DIR</c>, <c>--jsons=DIR</c> (default: <c>android/assets/jsons</c> found above the project).
/// </summary>
public partial class AssetSheet : Node3D
{
    private string outDir = "user://";
    private string? jsonsDir;
    private AssetRegistry registry = null!; // JUSTIFY: C007 assigned in _Ready before first use
    private RulesetClass ruleset = null!; // JUSTIFY: C007 assigned in _Ready before first use
    private readonly List<string> failures = [];

    public override async void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--out=", StringComparison.Ordinal)) outDir = arg["--out=".Length..];
            else if (arg.StartsWith("--jsons=", StringComparison.Ordinal)) jsonsDir = arg["--jsons=".Length..];
        }

        try
        {
            LoadRuleset();
            registry = AssetRegistry.ForRuleset(ruleset);
            var keyCount = ResolveEveryKey();
            BuildSheet();
            for (var i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            CaptureAndCheck();
            GD.Print($"asset-sheet: {keyCount} keys, {failures.Count} failures");
        }
        catch (Exception e)
        {
            failures.Add(e.ToString());
        }

        foreach (var failure in failures) GD.PrintErr("asset-sheet FAIL: " + failure);
        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }

    private void LoadRuleset()
    {
        var dir = jsonsDir;
        for (var d = ProjectSettings.GlobalizePath("res://"); dir == null && !string.IsNullOrEmpty(d); d = System.IO.Path.GetDirectoryName(d.TrimEnd('/')) ?? "")
        {
            var candidate = System.IO.Path.Combine(d, "android", "assets", "jsons");
            if (System.IO.Directory.Exists(candidate)) dir = candidate;
        }
        if (dir == null) throw new System.IO.DirectoryNotFoundException("android/assets/jsons not found; pass --jsons=DIR");
        RulesetCache.Clear();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(dir, System.IO.Path.Combine(dir, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    private int ResolveEveryKey()
    {
        var keys = RulesetAssetKeys.KeysFor(ruleset);
        foreach (var key in keys)
        {
            try
            {
                if (registry.Resolve(key).Source != AssetSourceKind.Procedural) failures.Add($"{key}: expected the procedural source");
                var ok = AssetKinds.Of(key) switch
                {
                    AssetKind.TerrainMaterial => registry.Get<Material>(key) != null && registry.Get<Texture2D>(key) != null,
                    AssetKind.Mesh => registry.Get<Mesh>(key) != null,
                    AssetKind.UnitBlock or AssetKind.Prop => FreeAfter(registry.Get<Node3D>(key)),
                    AssetKind.Icon => registry.Get<Texture2D>(key) != null,
                    AssetKind.Theme => registry.Get<Theme>(key) != null,
                    AssetKind.StyleBox => registry.Get<StyleBox>(key) != null,
                    AssetKind.Font => registry.Get<Font>(key) != null,
                    _ => false,
                };
                if (!ok) failures.Add($"{key}: not resolved");
            }
            catch (Exception e)
            {
                failures.Add($"{key}: {e.GetType().Name} {e.Message}");
            }
        }
        if (registry.Diagnostics.Count > 0) failures.Add("registry diagnostics: " + string.Join("; ", registry.Diagnostics));
        return keys.Count;
    }

    private static bool FreeAfter(Node3D node)
    {
        node.Free();
        return true;
    }

    private void BuildSheet()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = ProceduralAssets.ToColor(PlaceholderPalette.Background),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = ProceduralAssets.ToColor(PlaceholderPalette.Light),
                AmbientLightEnergy = 0.5f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 0.9f, RotationDegrees = new Vector3(-60f, 30f, 0f) };
        AddChild(sun);

        var hex = registry.Get<Mesh>(RulesetAssetKeys.HexMesh);
        var terrains = ruleset.Terrains.Values
            .Where(t => t.Type is Unciv.Models.Ruleset.Tile.TerrainType.Land or Unciv.Models.Ruleset.Tile.TerrainType.Water or Unciv.Models.Ruleset.Tile.TerrainType.TerrainFeature)
            .ToList();
        const int perRow = 8;
        const float step = 2.3f;
        for (var i = 0; i < terrains.Count; i++)
        {
            var key = AssetKey.Of(RulesetAssetKeys.Terrain, terrains[i].Name);
            var position = new Vector3((i % perRow) * step, 0f, (i / perRow) * step);
            AddChild(new MeshInstance3D { Mesh = hex, MaterialOverride = registry.Get<Material>(key), Position = position });
            AddChild(Label(terrains[i].Name, position + new Vector3(0f, 0.3f, 0.95f)));
        }

        var unitTypes = ruleset.UnitTypes.Keys.ToList();
        var terrainRows = (terrains.Count + perRow - 1) / perRow;
        for (var i = 0; i < unitTypes.Count; i++)
        {
            var block = registry.Get<Node3D>(AssetKey.Of(RulesetAssetKeys.UnitType, unitTypes[i]));
            block.Position = new Vector3((i % perRow) * step, 0f, (terrainRows + i / perRow) * step);
            AddChild(block);
            AddChild(Label(unitTypes[i], block.Position + new Vector3(0f, 0.01f, 0.7f)));
        }

        var rows = terrainRows + (unitTypes.Count + perRow - 1) / perRow;
        var centre = new Vector3((perRow - 1) * step / 2f, 0f, (rows - 1) * step / 2f);
        var camera = new Camera3D { Fov = 45f, Position = centre + new Vector3(0f, 17f, 9.5f), Current = true };
        AddChild(camera);
        camera.LookAt(centre + new Vector3(0f, 0f, 0.8f), Vector3.Up);

        BuildOverlay();
    }

    private static Label3D Label(string text, Vector3 position) => new()
    {
        Text = text,
        Position = position,
        RotationDegrees = new Vector3(-90f, 0f, 0f),
        PixelSize = 0.008f,
        FontSize = 36,
        Modulate = ProceduralAssets.ToColor(PlaceholderPalette.SheetLabel),
        OutlineSize = 6,
    };

    private void BuildOverlay()
    {
        var layer = new CanvasLayer();
        var root = new Control { Theme = registry.Get<Theme>(RulesetAssetKeys.UiTheme) };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;

        var icons = new HBoxContainer { Position = new Vector2(8f, 8f) };
        var iconKeys = Enum.GetNames<Unciv.Models.StatTypes.Stat>().Select(n => AssetKey.Of(RulesetAssetKeys.StatIcon, n))
            .Concat(ruleset.Technologies.Keys.Take(2).Select(n => AssetKey.Of(RulesetAssetKeys.TechIcon, n)))
            .Concat(ruleset.Policies.Keys.Take(1).Select(n => AssetKey.Of(RulesetAssetKeys.PolicyIcon, n)))
            .Concat(ruleset.UnitPromotions.Keys.Take(1).Select(n => AssetKey.Of(RulesetAssetKeys.PromotionIcon, n)))
            .Concat(ruleset.TileResources.Keys.Take(1).Select(n => AssetKey.Of(RulesetAssetKeys.ResourceIcon, n)))
            .ToList();
        foreach (var key in iconKeys)
        {
            icons.AddChild(new TextureRect { Texture = registry.Get<Texture2D>(key), CustomMinimumSize = new Vector2(48f, 48f), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        }
        root.AddChild(icons);

        var panel = new PanelContainer { Position = new Vector2(1000f, 560f) };
        var box = new VBoxContainer();
        box.AddChild(new Godot.Label { Text = "Theme panel" });
        box.AddChild(new Button { Text = "Theme button" });
        box.AddChild(new Button { Text = "Disabled", Disabled = true });
        panel.AddChild(box);
        root.AddChild(panel);

        layer.AddChild(root);
        AddChild(layer);
    }

    private void CaptureAndCheck()
    {
        var image = GetViewport().GetTexture().GetImage();
        var path = outDir.TrimEnd('/') + "/asset-sheet.png";
        if (image.SavePng(path) != Error.Ok) failures.Add("could not save " + path);
        var colours = new HashSet<uint>();
        for (var y = 0; y < image.GetHeight(); y += 7)
        {
            for (var x = 0; x < image.GetWidth(); x += 7) colours.Add(image.GetPixel(x, y).ToRgba32() >> 8);
        }
        if (colours.Count < 40) failures.Add($"frame looks blank: only {colours.Count} distinct sampled colours");
    }
}
