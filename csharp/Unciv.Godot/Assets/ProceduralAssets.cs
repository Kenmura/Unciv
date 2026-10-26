using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// Turns the engine-independent generators of <c>Unciv.Assets</c> into Godot objects. Everything here is a pure function of the key:
/// same key, same output. This file and its siblings in <c>Assets/</c> are the only client code allowed to write colours.
/// </summary>
public static class ProceduralAssets
{
    /// <summary>Radius 1, height 0.2, flat top; callers scale the node to the tile size they use.</summary>
    public const float HexRadius = 1f;
    public const float HexHeight = 0.2f;

    public static Color ToColor(Rgb c) => new(c.R / 255f, c.G / 255f, c.B / 255f);

    public static ImageTexture ToTexture(PixelImage image, bool mipmaps = false) =>
        ImageTexture.CreateFromImage(Image.CreateFromData(image.Width, image.Height, mipmaps, Image.Format.Rgba8, image.Rgba));

    /// <summary>The generated tones of a <c>sound/&lt;name&gt;</c> key as a 16-bit mono stream (C030).</summary>
    public static AudioStreamWav Sound(AssetKey key) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = ProceduralSounds.SampleRate,
        Stereo = false,
        Data = ProceduralSounds.Pcm(key.Name),
    };

    public static ArrayMesh ToMesh(MeshData data)
    {
        var positions = new Vector3[data.VertexCount];
        var normals = new Vector3[data.VertexCount];
        var uvs = new Vector2[data.VertexCount];
        var colours = data.Colors == null ? null : new Color[data.VertexCount];
        for (var i = 0; i < data.VertexCount; i++)
        {
            positions[i] = new Vector3(data.Positions[i * 3], data.Positions[i * 3 + 1], data.Positions[i * 3 + 2]);
            normals[i] = new Vector3(data.Normals[i * 3], data.Normals[i * 3 + 1], data.Normals[i * 3 + 2]);
            uvs[i] = new Vector2(data.Uvs[i * 2], data.Uvs[i * 2 + 1]);
            if (colours != null) colours[i] = new Color(data.Colors![i * 4], data.Colors[i * 4 + 1], data.Colors[i * 4 + 2], data.Colors[i * 4 + 3]);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = data.Indices;
        if (colours != null) arrays[(int)Mesh.ArrayType.Color] = colours;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    public static ArrayMesh HexMesh() => ToMesh(HexGeometry.Prism(HexRadius, HexHeight));

    /// <summary>The mesh a <c>mesh/...</c> key names: the plain hex prism, the grass tuft, or a terrain's displaced tile (C002).</summary>
    public static ArrayMesh MeshFor(AssetKey key)
    {
        if (key == VegetationGeometry.TuftMeshKey) return ToMesh(VegetationGeometry.Tuft());
        return TerrainRelief.TerrainOf(key) != null ? ToMesh(TerrainRelief.Build(key, HexRadius)) : HexMesh();
    }

    /// <summary>Flat albedo from the palette times a tiny generated noise texture.</summary>
    public static StandardMaterial3D TerrainMaterial(AssetKey key) => new()
    {
        AlbedoColor = ToColor(PlaceholderPalette.ForTerrain(key.Name)),
        AlbedoTexture = ToTexture(ProceduralImages.Noise(key)),
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        VertexColorUseAsAlbedo = true, // C002: relief meshes carry height/depth shading in vertex colours
        Roughness = 1f,
    };

    public static ImageTexture Icon(AssetKey key) => ToTexture(ProceduralImages.Icon(key));

    /// <summary>Fresh node tree per call: primitives standing on y = 0 plus (for units) a camera-facing initials billboard and a neutral <c>Banner</c> for civ colouring.</summary>
    public static Node3D Model(AssetKey key, ModelSpec spec, bool withBanner)
    {
        var root = new Node3D { Name = StableNodeName(key) };
        var index = 0;
        foreach (var p in spec.Primitives)
        {
            root.AddChild(new MeshInstance3D
            {
                Name = "Part" + index++,
                Mesh = PrimitiveMesh(p),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = ToColor(p.Colour), Roughness = 1f },
                Position = new Vector3(p.X, p.Y, p.Z),
                RotationDegrees = new Vector3(0f, p.Yaw, 0f),
            });
        }
        if (withBanner)
        {
            root.AddChild(new MeshInstance3D
            {
                Name = "Banner",
                Mesh = new BoxMesh { Size = new Vector3(0.28f, 0.14f, 0.04f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = ToColor(PlaceholderPalette.Light), Roughness = 1f },
                Position = new Vector3(0f, spec.BillboardHeight - 0.12f, 0.2f),
            });
        }
        if (spec.Initials.Length > 0)
        {
            root.AddChild(new Sprite3D
            {
                Name = "Glyphs",
                Texture = ToTexture(ProceduralImages.Billboard(spec.Initials)),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                PixelSize = 0.012f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                Position = new Vector3(0f, spec.BillboardHeight, 0f),
            });
        }
        return root;
    }

    private static string StableNodeName(AssetKey key) => new string(key.Name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ' ' ? c : '_').ToArray());

    private static Mesh PrimitiveMesh(PrimitiveSpec p) => p.Shape switch
    {
        PrimitiveShape.Box => new BoxMesh { Size = new Vector3(p.SizeX, p.SizeY, p.SizeZ) },
        PrimitiveShape.Cylinder => new CylinderMesh { TopRadius = p.SizeX / 2f, BottomRadius = p.SizeX / 2f, Height = p.SizeY },
        PrimitiveShape.Cone => new CylinderMesh { TopRadius = 0f, BottomRadius = p.SizeX / 2f, Height = p.SizeY },
        _ => new SphereMesh { Radius = p.SizeX / 2f, Height = p.SizeX },
    };

    public static StyleBoxFlat Panel() => Flat(PlaceholderPalette.PanelFill, PlaceholderPalette.PanelBorder);

    public static StyleBoxFlat Button() => Flat(PlaceholderPalette.ButtonFill, PlaceholderPalette.Accent);

    private static StyleBoxFlat Flat(Rgb fill, Rgb border)
    {
        var box = new StyleBoxFlat { BgColor = ToColor(fill), BorderColor = ToColor(border) };
        box.SetBorderWidthAll(ThemeSpec.BorderWidth);
        box.SetCornerRadiusAll(ThemeSpec.CornerRadius);
        box.SetContentMarginAll(ThemeSpec.ContentMargin);
        return box;
    }

    /// <summary>Dark slate panels with a gold accent; the default font is the engine's built-in one with the C025 fallback chain (<see cref="FontChain"/>) behind it.</summary>
    public static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFontSize = ThemeSpec.FontSize, DefaultFont = FontChain.Default };
        theme.SetStylebox("panel", "PanelContainer", Panel());
        theme.SetStylebox("panel", "Panel", Panel());
        var states = new (string Name, Rgb Fill, Rgb Border)[]
        {
            ("normal", PlaceholderPalette.ButtonFill, PlaceholderPalette.PanelBorder),
            ("hover", PlaceholderPalette.ButtonHover, PlaceholderPalette.AccentHover),
            ("pressed", PlaceholderPalette.ButtonPressed, PlaceholderPalette.Accent),
            ("disabled", PlaceholderPalette.PanelFill, PlaceholderPalette.PanelBorder),
            ("focus", PlaceholderPalette.ButtonFill, PlaceholderPalette.Accent),
        };
        foreach (var (name, fill, border) in states) theme.SetStylebox(name, "Button", Flat(fill, border));
        theme.SetColor("font_color", "Button", ToColor(PlaceholderPalette.Text));
        theme.SetColor("font_hover_color", "Button", ToColor(PlaceholderPalette.AccentHover));
        theme.SetColor("font_pressed_color", "Button", ToColor(PlaceholderPalette.Accent));
        theme.SetColor("font_disabled_color", "Button", ToColor(PlaceholderPalette.TextDisabled));
        theme.SetColor("font_color", "Label", ToColor(PlaceholderPalette.Text));
        return theme;
    }
}
