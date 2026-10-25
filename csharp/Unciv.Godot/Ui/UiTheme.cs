using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;

namespace Unciv.GodotClient.Ui;

/// <summary>
/// The UI kit's view of the asset registry: the theme (key <c>ui/theme</c>) at a UI scale, and the icons widgets ask for by key.
/// Widgets take their sizes through <see cref="Px"/> so one scale setting resizes fonts and spacing together; no widget names a file or colour.
/// </summary>
public sealed class UiTheme
{
    public const float MinScale = 0.5f;
    public const float MaxScale = 3f;
    public const int IconSize = 24;

    private readonly Theme baseTheme;

    public UiTheme(AssetRegistry registry, float scale = 1f)
    {
        Registry = registry;
        baseTheme = registry.Get<Theme>(RulesetAssetKeys.UiTheme);
        Theme = (Theme)baseTheme.Duplicate();
        SetScale(scale);
    }

    public AssetRegistry Registry { get; }

    /// <summary>The scaled theme every screen of the kit inherits; the same instance for the lifetime of this object, so a scale change reaches live controls.</summary>
    public Theme Theme { get; }

    public float Scale { get; private set; } = 1f;

    /// <summary>Raised after <see cref="SetScale"/> changed the scale, so widgets with fixed pixel sizes can resize.</summary>
    public event Action? ScaleChanged;

    public void SetScale(float scale)
    {
        Scale = Math.Clamp(scale, MinScale, MaxScale);
        Theme.DefaultFontSize = Px(baseTheme.DefaultFontSize);
        ScaleChanged?.Invoke();
    }

    /// <summary>A size in unscaled pixels at the current UI scale, rounded half away from zero.</summary>
    public int Px(int unscaled) => (int)MathF.Round(unscaled * Scale, MidpointRounding.AwayFromZero);

    public Texture2D Icon(AssetKey key) => Registry.Get<Texture2D>(key);

    public StyleBoxFlat Panel => (StyleBoxFlat)Theme.GetStylebox("panel", "PanelContainer");
}
